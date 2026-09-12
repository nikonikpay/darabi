using Mazesta.Core.Providers; using System.Security.Principal; using LibreHardwareMonitor.Hardware; using LibreHardwareMonitor.Hardware.Storage; using LibreHardwareMonitor.PawnIo;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Lhm;
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    public const string ReasonPawnIoMissing = "Provider.PawnIoMissing", ReasonNotElevated = "Provider.NotElevated", ReasonOpenFailed = "Provider.OpenFailed", ReasonNoHardware = "Provider.NoHardware";
    private sealed class NodeState(MappedNode mapped) { public MappedNode Mapped = mapped; public DateTimeOffset? LastOk; public string? Failure; public DateTimeOffset? FailingSince; public int ConsecutiveFailures; }
    private readonly ILhmComputer _computer; private readonly Func<bool> _pawnIo, _elevated; private readonly IClock _clock; private readonly ILogger _log;
    private readonly LhmHardwareMapper _mapper; private readonly List<NodeState> _nodes = [];
    private ProviderStatus _status = ProviderStatus.NotStarted; private bool _historyWarned;
    public string Name => "LibreHardwareMonitor";
    public ProviderStatus Status { get => _status; private set { _status = value; StatusChanged?.Invoke(value); } }
    public event Action<ProviderStatus>? StatusChanged;
    public IReadOnlyList<HardwareNode> Hardware { get; private set; } = [];

    public LibreHardwareMonitorProvider(ILhmComputer computer, Func<bool> isPawnIoInstalled, Func<bool> isElevated, Func<IHardware, string?> storageSerialResolver, IClock clock, ILogger<LibreHardwareMonitorProvider> logger)
    { _computer = computer; _pawnIo = isPawnIoInstalled; _elevated = isElevated; _clock = clock; _log = logger; _mapper = new LhmHardwareMapper(storageSerialResolver); }

    public static LibreHardwareMonitorProvider CreateDefault(IClock clock, ILoggerFactory loggerFactory) => new(
        new LhmComputerAdapter(), () => PawnIo.IsInstalled, IsProcessElevated,
        hw => (hw as StorageDevice)?.Storage?.SerialNumber, clock, loggerFactory.CreateLogger<LibreHardwareMonitorProvider>());
    private static bool IsProcessElevated() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }

    public void Start()
    {
        Status = ProviderStatus.Starting;
        try { _computer.Open(); }
        catch (Exception ex) { _log.LogError(ex, "LHM open failed"); Status = ProviderStatus.Failed(ReasonOpenFailed, ex.Message); return; }
        try
        {
            DisableSensorHistoryTree(_computer.Hardware);
            foreach (var m in _mapper.Map(_computer.Hardware)) _nodes.Add(new NodeState(m));
            Hardware = _nodes.Select(n => n.Mapped.Node).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "LHM enumeration failed"); Status = ProviderStatus.Failed(ReasonOpenFailed, ex.Message); return; }
        int count = _nodes.Sum(n => n.Mapped.Sensors.Count);
        // Start() must never throw (spec §5.1): both probes touch the OS (WindowsIdentity, the
        // PawnIO driver query) and can fail. A probe that cannot answer is answered pessimistically
        // so the customer is told sensors may be missing rather than being promised a clean run.
        if (!Probe(_elevated, "elevation")) Status = ProviderStatus.Degraded(ReasonNotElevated, "Process is not elevated; CPU and motherboard sensors are unavailable.", count);
        else if (!Probe(_pawnIo, "PawnIO driver")) Status = ProviderStatus.Degraded(ReasonPawnIoMissing, "PawnIO driver is not installed; CPU MSR sensors are unavailable.", count);
        else if (count == 0) Status = ProviderStatus.Degraded(ReasonNoHardware, "LHM returned no sensors.", 0);
        else Status = ProviderStatus.Ready(count);
    }

    private bool Probe(Func<bool> probe, string what)
    {
        try { return probe(); }
        catch (Exception ex) { _log.LogWarning(ex, "{What} probe failed; assuming the pessimistic answer", what); return false; }
    }

    /// <summary>
    /// LHM keeps a per-sensor value history (<c>ISensor.Values</c>, a day-long window by default)
    /// that this app never reads - Mazesta.Monitoring.HistoryStore owns history. Zeroing the window
    /// makes the library keep none of it (and clears whatever it already holds).
    /// Applied to one node's currently-exposed sensors; never throws.
    /// </summary>
    private void DisableSensorHistory(IHardware hw)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (sensor.ValuesTimeWindow == TimeSpan.Zero) continue;
            try { sensor.ValuesTimeWindow = TimeSpan.Zero; }
            catch (Exception ex)
            {
                if (_historyWarned) continue;                 // one line, not one per sensor per tick
                _historyWarned = true; _log.LogWarning(ex, "Could not disable LHM value history for {Sensor}", sensor.Identifier);
            }
        }
    }
    private void DisableSensorHistoryTree(IEnumerable<IHardware> roots)
    { foreach (var hw in roots) { DisableSensorHistory(hw); DisableSensorHistoryTree(hw.SubHardware); } }

    public PollResult Poll(PollRequest request)
    {
        if (_nodes.Count == 0) return PollResult.Empty;
        foreach (var n in _nodes)
        {
            if (!request.NodesToUpdate.Contains(n.Mapped.Node.Id)) continue;
            try { n.Mapped.Source.Update(); n.LastOk = request.Now; n.Failure = null; n.FailingSince = null; n.ConsecutiveFailures = 0; }
            catch (Exception ex)
            {
                n.Failure = ex.Message; n.FailingSince ??= request.Now; n.ConsecutiveFailures++;
                if (n.ConsecutiveFailures == 3) _log.LogWarning(ex, "Node {Node} failed 3 consecutive updates", n.Mapped.Node.Id);
            }
            // Some LHM hardware (the Nuvoton SuperIO on this board) only activates its sensors
            // during the first Update(), so those sensors are not present when Start() walks the
            // tree and would keep the default one-day window. Re-applying here is a cheap
            // already-zero check per sensor and catches every sensor LHM ever exposes.
            DisableSensorHistory(n.Mapped.Source);
        }
        var readings = new List<SensorReading>(_nodes.Sum(n => n.Mapped.Sensors.Count));
        var status = new Dictionary<HardwareId, NodeStatus>(_nodes.Count);
        foreach (var n in _nodes)
        {
            bool failed = n.Failure is not null;
            status[n.Mapped.Node.Id] = failed ? NodeStatus.Failed(n.Failure!, n.FailingSince!.Value, n.LastOk) : n.LastOk is null ? NodeStatus.NeverUpdated : NodeStatus.Healthy(n.LastOk.Value);
            foreach (var s in n.Mapped.Sensors)
            {
                double? value = s.Source.Value;
                var quality = failed || n.LastOk is null ? DataQuality.Stale : ReadingValidator.Validate(s.Definition.Kind, value);
                readings.Add(new SensorReading(s.Definition.Id, value, n.LastOk ?? request.Now, quality, Name));
            }
        }
        return new PollResult(readings, status);
    }
    public void Dispose()
    {
        try { _computer.Close(); } catch (Exception ex) { _log.LogWarning(ex, "LHM close failed"); }
        try { _computer.Dispose(); } catch (Exception ex) { _log.LogWarning(ex, "LHM dispose failed"); }
    }
}
