using Mazesta.Core.Providers; using System.Security.Principal; using LibreHardwareMonitor.Hardware; using LibreHardwareMonitor.Hardware.Storage; using LibreHardwareMonitor.PawnIo;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Lhm;
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    public const string ReasonPawnIoMissing = "Provider.PawnIoMissing", ReasonNotElevated = "Provider.NotElevated", ReasonOpenFailed = "Provider.OpenFailed", ReasonNoHardware = "Provider.NoHardware", ReasonCpuSensorsUnread = "Provider.CpuSensorsUnread";
    private sealed class NodeState(MappedNode mapped) { public MappedNode Mapped = mapped; public DateTimeOffset? LastOk; public string? Failure; public DateTimeOffset? FailingSince; public int ConsecutiveFailures; }
    private readonly ILhmComputer _computer; private readonly Func<bool> _pawnIo, _elevated; private readonly IClock _clock; private readonly ILogger _log;
    private readonly Action? _beforeOpen;
    private readonly LhmHardwareMapper _mapper; private readonly List<NodeState> _nodes = [];
    private ProviderStatus _status = ProviderStatus.NotStarted; private bool _historyWarned;
    public string Name => "LibreHardwareMonitor";
    public ProviderStatus Status { get => _status; private set { _status = value; StatusChanged?.Invoke(value); } }
    public event Action<ProviderStatus>? StatusChanged;
    public IReadOnlyList<HardwareNode> Hardware { get; private set; } = [];

    public LibreHardwareMonitorProvider(ILhmComputer computer, Func<bool> isPawnIoInstalled, Func<bool> isElevated, Func<IHardware, string?> storageSerialResolver, IClock clock, ILogger<LibreHardwareMonitorProvider> logger, Action? beforeOpen = null)
    { _computer = computer; _pawnIo = isPawnIoInstalled; _elevated = isElevated; _clock = clock; _log = logger; _beforeOpen = beforeOpen; _mapper = new LhmHardwareMapper(storageSerialResolver); }

    public static LibreHardwareMonitorProvider CreateDefault(IClock clock, ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger<LibreHardwareMonitorProvider>();
        // LHM loads its PawnIO modules inside Open(), so the driver has to be in place before it.
        return new(new LhmComputerAdapter(), PawnIoDriver.IsInstalled, IsProcessElevated,
            hw => (hw as StorageDevice)?.Storage?.SerialNumber, clock, log, () => PawnIoDriver.EnsureInstalled(IsProcessElevated(), log));
    }
    private static bool IsProcessElevated() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }

    public void Start()
    {
        Status = ProviderStatus.Starting;
        try { _beforeOpen?.Invoke(); }
        catch (Exception ex) { _log.LogWarning(ex, "Preparing the sensor driver failed; continuing without it"); }
        try { _computer.Open(); }
        catch (Exception ex) { _log.LogError(ex, "LHM open failed"); Status = ProviderStatus.Failed(ReasonOpenFailed, ex.Message); return; }
        try
        {
            PrimeSensors(_computer.Hardware);
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
        else if (!HasCpuMsrEvidence() && !Probe(_pawnIo, "PawnIO driver")) Status = ProviderStatus.Degraded(ReasonPawnIoMissing, "PawnIO driver is not installed; CPU MSR sensors are unavailable.", count);
        else if (!HasCpuMsrEvidence() && HasCpuTemperatureSensors()) Status = ProviderStatus.Degraded(ReasonCpuSensorsUnread, "PawnIO is installed but no CPU temperature reads (driver blocked or not started).", count);
        else if (count == 0) Status = ProviderStatus.Degraded(ReasonNoHardware, "LHM returned no sensors.", 0);
        else Status = ProviderStatus.Ready(count);
    }

    /// <summary>
    /// The registry check can report "not installed" while the driver is loaded and serving MSR reads (observed with PawnIO 2.x on the dev
    /// box), so a CPU temperature that actually reads is taken as proof the driver works. The sensor merely existing is not proof: LHM
    /// creates the Intel core/package temperature sensors from CPUID alone and leaves them empty without the driver - three field reports
    /// (i7-10750H, Ryzen 9 9900X, Ryzen 5 3400G) were marked Ready while every CPU temperature was empty.
    /// </summary>
    private bool HasCpuMsrEvidence() => _nodes.Any(n => n.Mapped.Node.Kind == HardwareKind.Cpu
        && n.Mapped.Sensors.Any(s => s.Definition.Kind == SensorKind.Temperature && s.Source.Value is > 0));
    private bool HasCpuTemperatureSensors() => _nodes.Any(n => n.Mapped.Node.Kind == HardwareKind.Cpu && n.Mapped.Sensors.Any(s => s.Definition.Kind == SensorKind.Temperature));

    /// <summary>
    /// LHM activates some sensors only during the first Update() - the Nuvoton Super I/O's fans,
    /// temperatures and voltages are absent from a tree walked before it, leaving only the duty-cycle
    /// "Control" sensors. One update per node before mapping makes the exposed sensor set complete.
    /// </summary>
    private void PrimeSensors(IEnumerable<IHardware> roots)
    {
        foreach (var hw in roots)
        {
            try { hw.Update(); }
            catch (Exception ex) { _log.LogWarning(ex, "Initial update of {Hardware} failed", hw.Identifier); }
            PrimeSensors(hw.SubHardware);
        }
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
            // A sensor LHM activates after Start would keep the default one-day window. Re-applying
            // here is a cheap already-zero check per sensor and catches every sensor LHM ever exposes.
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
