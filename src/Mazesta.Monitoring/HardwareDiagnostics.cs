using System.Globalization; using System.Text;
using Mazesta.Core.Hardware; using Microsoft.Extensions.Logging;
namespace Mazesta.Monitoring;

/// <summary>How one sensor behaved over the first polls: readings that were good, readings that were not, and the last quality seen.</summary>
public sealed record SensorTally(int Good, int Bad, DataQuality Last);

/// <summary>
/// The hardware report a technician brings back from a machine the app does not fully know: every device and sensor the provider found (with its
/// identifier, which is what a new mapping needs), the sensors no role was given to, the sensors that never produced a good reading, and the key
/// readings a part should have but lacks. Pure, so it is testable; <see cref="HardwareDiagnosticsRecorder"/> fills it from the running monitor.
/// </summary>
public static class HardwareDiagnosticsReport
{
    /// <summary>The readings each kind of part is expected to have, as alternatives: any one role of a set is enough.</summary>
    internal static readonly IReadOnlyDictionary<HardwareKind, (string What, SensorRole[] AnyOf)[]> Expected = new Dictionary<HardwareKind, (string, SensorRole[])[]>
    {
        [HardwareKind.Cpu] = [("temperature", [SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuCoreTemp]), ("load", [SensorRole.CpuTotalLoad]),
            ("clock", [SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuCoreClock]), ("package power", [SensorRole.CpuPackagePower]), ("core voltage", [SensorRole.CpuVcore])],
        [HardwareKind.Gpu] = [("core temperature", [SensorRole.GpuCoreTemp]), ("load", [SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D]), ("core clock", [SensorRole.GpuCoreClock]),
            ("power", [SensorRole.GpuPower]), ("VRAM used", [SensorRole.GpuVramUsed]), ("fan", [SensorRole.GpuFanRpm, SensorRole.GpuFanPercent])],
        [HardwareKind.Memory] = [("used", [SensorRole.RamUsed])],
        [HardwareKind.Storage] = [("temperature", [SensorRole.StorageTemp]), ("used space", [SensorRole.StorageUsedSpace])],
    };

    /// <summary>Short problem lines, one per finding, for the log. Empty when the machine is fully covered.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<HardwareNode> nodes, IReadOnlyDictionary<SensorId, SensorTally> tallies)
    {
        var lines = new List<string>();
        foreach (var n in nodes.Where(n => n.ParentId is null))
            foreach (var missing in Missing(n)) lines.Add($"{n.Kind} '{n.Name}': no {missing} sensor");
        foreach (var n in nodes)
        {
            int unmapped = n.Sensors.Count(s => s.Role == SensorRole.None);
            if (unmapped > 0) lines.Add($"{n.Kind} '{n.Name}': {unmapped} sensor(s) without a role");
            foreach (var s in n.Sensors.Where(s => tallies.TryGetValue(s.Id, out var t) && t.Good == 0 && t.Bad > 0))
                lines.Add($"{n.Kind} '{n.Name}': '{s.Name}' ({s.Id.Value}) never read a good value, last {tallies[s.Id].Last}");
        }
        return lines;
    }

    internal static IEnumerable<string> Missing(HardwareNode node)
    {
        if (!Expected.TryGetValue(node.Kind, out var expected)) yield break;
        var roles = node.Sensors.Select(s => s.Role).ToHashSet();
        foreach (var (what, anyOf) in expected) if (!anyOf.Any(roles.Contains)) yield return what;
    }

    public static string Build(DateTimeOffset time, string app, string machine, ProviderStatus status, IReadOnlyList<HardwareNode> nodes, IReadOnlyDictionary<SensorId, SensorTally> tallies)
    {
        var b = new StringBuilder();
        string I(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);
        b.AppendLine("Mazesta hardware report").AppendLine(I($"Time:     {time:yyyy-MM-dd HH:mm:ss zzz}")).AppendLine($"App:      {app}").AppendLine($"Machine:  {machine}")
         .AppendLine($"Provider: {status.State}, {status.SensorCount} sensors{(status.ReasonKey is { } r ? $", {r}" : "")}{(status.Detail is { } d ? $" - {d}" : "")}").AppendLine();
        var problems = Problems(nodes, tallies);
        b.AppendLine(I($"Findings ({problems.Count})"));
        foreach (var p in problems) b.AppendLine("  - " + p);
        if (problems.Count == 0) b.AppendLine("  none");
        b.AppendLine();
        foreach (var n in nodes)
        {
            b.AppendLine(I($"[{n.Kind}] {n.Name}  vendor={n.Vendor} id={n.Id.Value}{(n.ParentId is { } p ? $" parent={p.Value}" : "")}{(n.IdIsStable ? "" : " (unstable id)")}"));
            foreach (var s in n.Sensors.OrderBy(s => s.Kind).ThenBy(s => s.Ordinal))
            {
                var t = tallies.GetValueOrDefault(s.Id);
                string seen = t is null ? "not polled" : I($"good {t.Good}, bad {t.Bad}, last {t.Last}");
                b.AppendLine(I($"    {s.Kind,-12} {s.Name,-32} role={(s.Role == SensorRole.None ? "-" : s.Role.ToString()),-26} unit={s.Unit,-14} {s.Id.Value}  [{seen}]"));
            }
        }
        return b.ToString();
    }
}

/// <summary>
/// Watches the monitor's first polls, then writes <see cref="HardwareDiagnosticsReport"/> to <c>Data/logs/hardware-report.txt</c> and puts each finding
/// in the app log as a warning, so every machine the app runs on leaves a record of what it could not read. It stops listening after that: the
/// cost is a counter per sensor for the first <see cref="Polls"/> polls. <see cref="Write"/> refreshes the report on demand (for an export).
/// </summary>
public sealed class HardwareDiagnosticsRecorder : IDisposable
{
    public const int Polls = 15;
    private readonly PollingEngine _engine; private readonly string _file; private readonly string _app; private readonly ILogger _log;
    private readonly Dictionary<SensorId, SensorTally> _tallies = []; private readonly object _lock = new();
    private int _polls; private bool _done;

    public HardwareDiagnosticsRecorder(PollingEngine engine, string logsDir, string app, ILogger log)
    {
        _engine = engine; _file = Path.Combine(logsDir, "hardware-report.txt"); _app = app; _log = log;
        engine.SnapshotPublished += OnSnapshot;
        engine.Provider.StatusChanged += OnStatus;
    }

    public string ReportFile => _file;

    private void OnStatus(ProviderStatus s)
    {
        if (s.State is ProviderState.Degraded or ProviderState.Failed) _log.LogWarning("Sensor provider {State}: {Reason} {Detail}", s.State, s.ReasonKey, s.Detail);
    }

    private void OnSnapshot(SensorSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_done) return;
            foreach (var r in snapshot.Readings)
            {
                var t = _tallies.GetValueOrDefault(r.Id) ?? new SensorTally(0, 0, r.Quality);
                _tallies[r.Id] = r.Quality == DataQuality.Ok && r.Value is not null ? t with { Good = t.Good + 1, Last = r.Quality } : t with { Bad = t.Bad + 1, Last = r.Quality };
            }
            foreach (var (node, status) in snapshot.NodeStatus)
                if (!status.IsOk && _polls == Polls - 1) _log.LogWarning("Hardware {Node} is failing: {Reason}", node.Value, status.FailureReason);
            if (++_polls < Polls) return;
            _done = true;
        }
        _engine.SnapshotPublished -= OnSnapshot;
        foreach (var p in HardwareDiagnosticsReport.Problems(_engine.Hardware, Snapshot())) _log.LogWarning("Hardware coverage: {Problem}", p);
        Write();
    }

    private Dictionary<SensorId, SensorTally> Snapshot() { lock (_lock) return new(_tallies); }

    /// <summary>Writes the report now, with whatever has been seen so far. Never throws: a report that cannot be written is only logged.</summary>
    public string? Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, HardwareDiagnosticsReport.Build(DateTimeOffset.Now, _app, Environment.MachineName, _engine.Provider.Status, _engine.Hardware, Snapshot()), Encoding.UTF8);
            return _file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Hardware report not written"); return null; }
    }

    public void Dispose() { _engine.SnapshotPublished -= OnSnapshot; _engine.Provider.StatusChanged -= OnStatus; }
}
