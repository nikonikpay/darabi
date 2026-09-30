using Mazesta.Core.Hardware; using Mazesta.Core.Health.Checkup; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Diagnostics;
using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu; using Mazesta.Hardware.Details; using Mazesta.Hardware.Nvidia; using Mazesta.Monitoring;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Services;

/// <summary>The checkup of one benchmark run: what its own measurements say (<see cref="Findings"/>) and where it stands among other systems with
/// the same part (<see cref="Peer"/>, set a moment later by whoever holds the comparison lists).</summary>
public sealed record CheckupRun(string Id, string NameKey, DateTimeOffset At, IReadOnlyList<Finding> Findings, Finding? Peer)
{
    public IEnumerable<Finding> All => Peer is null ? Findings : [Peer, .. Findings];
}

/// <summary>
/// The app's "is this machine working as it should" judge. It watches every benchmark: while a GPU one runs it samples what the NVIDIA driver says
/// holds the clock; when a CPU or GPU run ends it reads the monitor's record of that run and applies the checkup rules. The setup (memory, power
/// plan, drive links) is judged on request. Nothing is guessed: a rule that lacks its measurement says nothing.
/// </summary>
public sealed class CheckupService
{
    private static readonly TimeSpan PeerWait = TimeSpan.FromSeconds(20);
    private readonly PollingEngine _engine; private readonly InventoryCache _inventory; private readonly HardwareDetailsCache _details; private readonly ILogger _log;
    private readonly object _lock = new();
    private readonly Dictionary<string, CheckupRun> _runs = [];
    private readonly Dictionary<BenchmarkResult, TaskCompletionSource> _settled = new(ReferenceEqualityComparer.Instance);
    private NvidiaRunProbe? _probe; private bool? _onBattery;

    /// <summary>A run was judged, or its standing among other systems arrived. Raised on a worker thread.</summary>
    public event Action? Changed;

    public CheckupService(BenchmarkRunner runner, PollingEngine engine, InventoryCache inventory, HardwareDetailsCache details, ILogger<CheckupService> log)
    {
        _engine = engine; _inventory = inventory; _details = details; _log = log;
        runner.Started += OnStarted; runner.Finished += OnFinished;
    }

    public static bool IsCpu(string id) => id is "bench.cpu.single" or "bench.cpu.multi";

    private void OnStarted(IBenchmark benchmark, IReadOnlyDictionary<string, string> options)
    {
        try
        {
            lock (_lock) { _probe?.Dispose(); _probe = null; }
            _onBattery = PowerSettings.Read().OnMains is { } mains ? !mains : null;
            if (benchmark.Component == HardwareKind.Gpu) { var probe = NvidiaRunProbe.Start(GpuName(options)); lock (_lock) _probe = probe; }
        }
        catch (Exception e) { _log.LogWarning(e, "Checkup: watching the run failed"); }
    }

    private void OnFinished(RecordedBenchmark run)
    {
        NvidiaRunProbe? probe; lock (_lock) { probe = _probe; _probe = null; }
        var probed = probe?.Stop(); probe?.Dispose();
        if (run.Result.Status != BenchmarkStatus.Completed) return;
        lock (_lock) _settled[run.Result] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            IReadOnlyList<Finding> findings = [];
            string id = run.Definition.Id.Value;
            if (IsCpu(id))
            {
                int? baseMhz = _inventory.IsLoaded ? _inventory.GetAsync().Result.Cpu?.MaxClockMhz : null;
                findings = CpuCheck.Evaluate(CheckupTraces.Cpu(_engine, run.Result.StartedAt, run.Result.FinishedAt, id == "bench.cpu.multi", baseMhz, _onBattery));
            }
            else if (run.Definition.Id.Value.StartsWith("bench.gpu.", StringComparison.Ordinal))
            {
                string? name = GpuName(run.Options ?? new Dictionary<string, string>());
                var slot = SlotOf(name);
                var link = slot is null ? null : new GpuLink(probed?.Gen ?? slot.Port.CurrentGen, probed?.Width ?? slot.Port.CurrentWidth, slot.CardMaxGen, slot.CardMaxWidth, slot.Port.MaxGen, slot.Port.MaxWidth, probed?.Gen is not null);
                findings = GpuCheck.Evaluate(CheckupTraces.Gpu(_engine, run.Result.StartedAt, run.Result.FinishedAt, GpuDevices.SensorNode(_engine, name ?? ""), name, probed?.Counts, link));
            }
            lock (_lock) _runs[id] = new(id, run.Definition.NameKey, run.Result.FinishedAt, findings, _runs.GetValueOrDefault(id) is { } had && had.At == run.Result.FinishedAt ? had.Peer : null);
            _log.LogInformation("Checkup of {Id}: {Findings}", id, string.Join(", ", findings.Select(f => $"{f.Code}/{f.Level}")));
        }
        catch (Exception e) { _log.LogWarning(e, "Checkup of a benchmark run failed"); }
        Changed?.Invoke();
    }

    /// <summary>The run's standing among other systems with the same part, or null when there is none to give; either way the run is settled.</summary>
    public void SetPeer(RecordedBenchmark run, Finding? peer)
    {
        TaskCompletionSource? done;
        lock (_lock)
        {
            string id = run.Definition.Id.Value;
            if (peer is not null)
                _runs[id] = _runs.TryGetValue(id, out var r) && r.At == run.Result.FinishedAt ? r with { Peer = peer } : new(id, run.Definition.NameKey, run.Result.FinishedAt, [], peer);
            _settled.Remove(run.Result, out done);
        }
        done?.TrySetResult();
        if (peer is not null) Changed?.Invoke();
    }

    /// <summary>A run's findings for its report, once its standing among other systems has arrived (or after a short wait for it).</summary>
    public async Task<IReadOnlyList<Finding>> ForRunAsync(RecordedBenchmark run)
    {
        Task? wait; lock (_lock) wait = _settled.GetValueOrDefault(run.Result)?.Task;
        if (wait is not null) await Task.WhenAny(wait, Task.Delay(PeerWait)).ConfigureAwait(false);
        lock (_lock) return _runs.TryGetValue(run.Definition.Id.Value, out var r) && r.At == run.Result.FinishedAt ? [.. r.All] : [];
    }

    /// <summary>The latest judged run of each benchmark in this session, newest first.</summary>
    public IReadOnlyList<CheckupRun> Runs() { lock (_lock) return [.. _runs.Values.OrderByDescending(r => r.At)]; }

    /// <summary>The machine's setup as it is now: memory, the power plan, drive links. Waits for the hardware details (the SPD chips are read once
    /// the sensor driver is up).</summary>
    public async Task<IReadOnlyList<Finding>> SetupAsync()
    {
        var inv = await _inventory.GetAsync().ConfigureAwait(false);
        var details = await _details.GetAsync().ConfigureAwait(false);
        var found = new List<Finding>();
        found.AddRange(PlatformCheck.Power(PowerSettings.Read()));
        found.AddRange(MemoryCheck.Evaluate(inv.MemoryModules, details.Spd));
        found.AddRange(PlatformCheck.Drives(details.Drives.Where(d => d.Slot is not null).Select(d => PlatformCheck.Of(d.Name, d.Slot!))));
        return found;
    }

    private PciSlotLink? SlotOf(string? gpuName)
    {
        if (!_details.GetAsync().IsCompletedSuccessfully || !_inventory.IsLoaded) return null;
        var gpus = _details.GetAsync().Result.Gpus; var inv = _inventory.GetAsync().Result.Gpus;
        string want = BenchmarkPeers.PartName(gpuName);
        var pnp = inv.Count == 1 ? inv[0].PnpDeviceId : inv.FirstOrDefault(g => string.Equals(BenchmarkPeers.PartName(g.Name), want, StringComparison.OrdinalIgnoreCase))?.PnpDeviceId;
        return pnp is null ? null : gpus.FirstOrDefault(g => g.PnpDeviceId == pnp)?.Slot;
    }

    /// <summary>What a run measured on the side, for explaining a gap to other systems: the part's power, its hottest reading and its clock under load.</summary>
    public static RunConditions ConditionsOf(IEnumerable<BenchmarkMetric> metrics)
    {
        var m = metrics.ToList();
        double? V(params string[] keys) { foreach (var k in keys) if (m.FirstOrDefault(x => x.Key == k) is { } x) return x.Value; return null; }
        return new(V("Bench_Cpu_Power", "Bench_Gpu_Power"), V("Bench_Cpu_TempMax", "Bench_Gpu_TempMax"), V("Bench_Cpu_PClock", "Bench_Cpu_Clock", "Bench_Cpu_ClockPeak", "Bench_Gpu_Clock"));
    }

    /// <summary>The GPU a run chose (the option holds "name|LUID"), or the one it runs on by default.</summary>
    public static string? GpuName(IReadOnlyDictionary<string, string> options)
    {
        string key = options.GetValueOrDefault(GpuDevices.OptionKey) is { Length: > 0 } chosen ? chosen : GpuDevices.Choices().FirstOrDefault()?.Value ?? "";
        int bar = key.LastIndexOf('|');
        return (bar > 0 ? key[..bar] : key) is { Length: > 0 } name ? name : null;
    }
}
