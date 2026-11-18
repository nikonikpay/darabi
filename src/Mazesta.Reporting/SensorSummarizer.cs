using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Reporting;

/// <summary>Reads the sensors that matter for a test session out of the monitor's recorded history for the session window - never a fresh reading, never a guess.</summary>
public static class SensorSummarizer
{
    public const int MaxTracePoints = 120;

    private static readonly SensorRole[] KeyRoles =
    [
        SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuTotalLoad, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuPackagePower, SensorRole.CpuVcore,
        SensorRole.GpuCoreTemp, SensorRole.GpuHotSpotTemp, SensorRole.GpuVramTemp, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D, SensorRole.GpuCoreClock, SensorRole.GpuMemoryClock, SensorRole.GpuPower, SensorRole.GpuFanRpm,
        SensorRole.GpuLoadCompute, SensorRole.GpuLoadMemoryController, SensorRole.GpuVoltage, SensorRole.GpuFanPercent, SensorRole.GpuPowerPercent, SensorRole.GpuVramUsed, SensorRole.CpuCcdTemp, SensorRole.CpuFan,
        SensorRole.RamLoad, SensorRole.RamUsed, SensorRole.DimmTemp, SensorRole.BoardFan, SensorRole.StorageReadRate, SensorRole.StorageWriteRate, SensorRole.BoardTemp, SensorRole.ChipsetTemp, SensorRole.StorageTemp, SensorRole.NetUtilization
    ];

    public static IReadOnlyList<SensorSummary> Summarize(PollingEngine engine, DateTimeOffset from, DateTimeOffset to)
    {
        int startSec = engine.History.SecondsSinceEpoch(from), endSec = engine.History.SecondsSinceEpoch(to);
        var result = new List<SensorSummary>();
        foreach (var role in KeyRoles)
            foreach (var node in engine.Hardware)
                foreach (var def in node.Sensors.Where(s => s.Role == role))
                    if (Summarize(def, node.Name, engine.History.GetRaw(def.Id), startSec, endSec) is { } summary) result.Add(summary);
        return result;
    }

    /// <summary>Each temperature sensor's highest reading inside each window (a test's or a benchmark's own run), from every recorded sample.
    /// A window in which a sensor recorded nothing has no peak for it.</summary>
    public static IReadOnlyList<WindowPeak> Peaks(PollingEngine engine, IEnumerable<SensorSummary> sensors, IEnumerable<(DateTimeOffset From, DateTimeOffset To)> windows)
    {
        var temps = sensors.Where(s => s.Kind == nameof(SensorKind.Temperature) || s.Role is "CpuPackagePower" or "GpuPower") /* temperatures, and the two power draws the power test sums */.Select(s => (s.Id, Raw: engine.History.GetRaw(new SensorId(s.Id)))).ToList();
        var list = new List<WindowPeak>();
        foreach (var (from, to) in windows.Distinct())
        {
            int a = engine.History.SecondsSinceEpoch(from), b = engine.History.SecondsSinceEpoch(to);
            foreach (var (id, raw) in temps)
            {
                double max = double.NegativeInfinity;
                for (int i = 0; i < raw.Seconds.Length; i++) if (raw.Seconds[i] >= a && raw.Seconds[i] <= b && !float.IsNaN(raw.Values[i])) max = Math.Max(max, raw.Values[i]);
                if (!double.IsNegativeInfinity(max)) list.Add(new(from, to, id, max));
            }
        }
        return list;
    }

    /// <summary>Statistics and a down-sampled trace of the valid samples inside [startSec, endSec]; null when there are none.</summary>
    /// <summary>The key sensors over each test's own run (not the whole session), each with a short trace from the test's start.</summary>
    public static IReadOnlyList<TestSensorSet> PerTest(PollingEngine engine, IEnumerable<(string Id, DateTimeOffset From, DateTimeOffset To)> windows)
    {
        var result = new List<TestSensorSet>();
        foreach (var (id, from, to) in windows)
        {
            int a = engine.History.SecondsSinceEpoch(from), b = engine.History.SecondsSinceEpoch(to); var list = new List<SensorSummary>();
            foreach (var role in KeyRoles)
                foreach (var node in engine.Hardware)
                    foreach (var def in node.Sensors.Where(s => s.Role == role))
                        if (Summarize(def, node.Name, engine.History.GetRaw(def.Id), a, b, TestTracePoints) is { } s) list.Add(s);
            AddVramPercent(engine, a, b, list);
            if (list.Count > 0) result.Add(new(id, list));
        }
        return result;
    }
    private const int TestTracePoints = 60;

    /// <summary>The share of the graphics memory in use: the used amount over the total, both read from the card itself (no total, no figure).</summary>
    private static void AddVramPercent(PollingEngine engine, int a, int b, List<SensorSummary> list)
    {
        foreach (var node in engine.Hardware)
        {
            var used = node.Sensors.FirstOrDefault(s => s.Role == SensorRole.GpuVramUsed); var total = node.Sensors.FirstOrDefault(s => s.Role == SensorRole.GpuVramTotal);
            if (used is null || total is null) continue;
            var t = engine.History.GetRaw(total.Id); double size = 0; for (int i = t.Values.Length - 1; i >= 0; i--) if (!float.IsNaN(t.Values[i])) { size = t.Values[i]; break; }
            if (size <= 0 || Summarize(used, node.Name, engine.History.GetRaw(used.Id), a, b, TestTracePoints) is not { } u) continue;
            list.Add(new(u.Id + "#pct", u.Hardware, "GPU VRAM usage", "Load", "%", u.Min / size * 100, u.Average / size * 100, u.Max / size * 100, u.Samples, [.. u.Trace.Select(p => new TracePoint(p.Seconds, p.Value / size * 100))], "GpuVramUsage"));
        }
    }

    internal static SensorSummary? Summarize(SensorDefinition def, string hardwareName, RawSeries raw, int startSec, int endSec, int maxPoints = MaxTracePoints)
    {
        var points = new List<(int Sec, double Value)>();
        for (int i = 0; i < raw.Seconds.Length; i++)
            if (raw.Seconds[i] >= startSec && raw.Seconds[i] <= endSec && !float.IsNaN(raw.Values[i])) points.Add((raw.Seconds[i], raw.Values[i]));
        if (points.Count == 0) return null;
        int step = (int)Math.Ceiling(points.Count / (double)maxPoints);
        var trace = points.Where((_, i) => i % step == 0).Select(p => new TracePoint(p.Sec - startSec, p.Value)).ToList();
        return new(def.Id.Value, hardwareName, def.Name, def.Kind.ToString(), Units.Symbol(def.Unit), points.Min(p => p.Value), points.Average(p => p.Value), points.Max(p => p.Value), points.Count, trace, def.Role.ToString());
    }
}
