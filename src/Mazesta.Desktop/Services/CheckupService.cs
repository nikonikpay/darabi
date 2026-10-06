using Mazesta.Core.Hardware; using Mazesta.Core.Health.Checkup; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Diagnostics;
using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu; using Mazesta.Hardware.Details; using Mazesta.Hardware.Nvidia; using Mazesta.Monitoring;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Services;

/// <summary>The checkup of one benchmark run: what its own measurements say (<see cref="Findings"/>) and how it compares with this machine's
/// earlier runs (<see cref="Peer"/>, set a moment later by whoever holds the run log).</summary>
/// <summary>The checkup of one test of the last test session: how the test itself ended, and what the monitor's record of it says about the
/// part it loaded (nothing for a test that loads no part the checkup has rules for).</summary>
public sealed record TestCheck(string Id, string NameKey, DateTimeOffset At, TestOutcome Outcome, IReadOnlyList<Finding> Findings);

public sealed record CheckupRun(string Id, string NameKey, DateTimeOffset At, IReadOnlyList<Finding> Findings, Finding? Peer)
{
    public IEnumerable<Finding> All => Peer is null ? Findings : [Peer, .. Findings];
}

/// <summary>
/// The app's "is this machine working as it should" judge. It watches every benchmark: while a GPU one runs it samples what the NVIDIA driver says
/// holds the clock; when a CPU or GPU run ends it reads the monitor's record of that run and applies the checkup rules. The setup (memory, power
/// plan, drive links, drive health) is judged on request. Nothing is guessed: a rule that lacks its measurement says nothing.
/// </summary>
public sealed class CheckupService
{
    private static readonly TimeSpan PeerWait = TimeSpan.FromSeconds(20);
    private readonly PollingEngine _engine; private readonly InventoryCache _inventory; private readonly HardwareDetailsCache _details; private readonly Mazesta.Core.Providers.IDriveHealthProvider _drives; private readonly ILogger _log;
    private readonly object _lock = new();
    private readonly Dictionary<string, CheckupRun> _runs = [];
    private readonly Dictionary<BenchmarkResult, TaskCompletionSource> _settled = new(ReferenceEqualityComparer.Instance);
    private NvidiaRunProbe? _probe; private bool? _onBattery;
    private TestWatch? _sessionWatch; private IReadOnlyList<QueuedTest> _sessionQueue = []; private readonly List<TestRunResult> _sessionDone = []; private IReadOnlyList<TestCheck> _tests = [];

    /// <summary>A run was judged, or its standing among other systems arrived. Raised on a worker thread.</summary>
    public event Action? Changed;

    public CheckupService(BenchmarkRunner runner, PollingEngine engine, InventoryCache inventory, HardwareDetailsCache details, Mazesta.Core.Providers.IDriveHealthProvider drives, ILogger<CheckupService> log, TestEngine? tests = null)
    {
        _engine = engine; _inventory = inventory; _details = details; _drives = drives; _log = log;
        runner.Started += OnStarted; runner.Finished += OnFinished;
        if (tests is not null) { tests.SessionStarted += OnTestsStarted; tests.TestCompleted += OnTestDone; tests.StateChanged += OnTestsState; }
    }

    // ——— Every test session is judged as it ends: the diagnosis is made from the Tests page's own tests, whoever started them ———
    private void OnTestsStarted(IReadOnlyList<QueuedTest> queue)
    {
        string? gpu = queue.FirstOrDefault(q => q.Definition.Id.Value.StartsWith("gpu.", StringComparison.Ordinal)) is { } g ? GpuName(g.Options ?? new Dictionary<string, string>()) : null;
        var watch = WatchTests(gpu);
        lock (_lock) { _sessionWatch?.Dispose(); _sessionWatch = watch; _sessionQueue = queue; _sessionDone.Clear(); }
    }
    private void OnTestDone(TestId id, TestRunResult result) { lock (_lock) { _sessionDone.RemoveAll(r => r.Id == id); _sessionDone.Add(result); } }   // (a repeated test: its last run)
    private void OnTestsState(TestEngineState state)
    {
        if (state == TestEngineState.Running) return;
        TestWatch? watch; List<TestRunResult> done; IReadOnlyList<QueuedTest> queue;
        lock (_lock) { watch = _sessionWatch; _sessionWatch = null; done = [.. _sessionDone]; queue = _sessionQueue; }
        if (watch is null) return;
        try
        {
            var parts = watch.Judge(done);
            var checks = done.Select(r => new TestCheck(r.Id.Value, queue.FirstOrDefault(q => q.Definition.Id == r.Id)?.Definition.NameKey ?? r.Id.Value, r.FinishedAt ?? r.StartedAt, r.Outcome,
                parts.Select(p => p.ByTest?.GetValueOrDefault(r.Id.Value)).FirstOrDefault(f => f is not null) ?? [])).ToList();
            lock (_lock) _tests = checks;
            _log.LogInformation("Checkup of the test session: {Tests}", string.Join("; ", checks.Select(c => $"{c.Id} {c.Outcome} [{string.Join(", ", c.Findings.Select(f => $"{f.Code}/{f.Level}"))}]")));
        }
        catch (Exception e) { _log.LogWarning(e, "Checkup of the test session failed"); }
        finally { watch.Dispose(); }
        Changed?.Invoke();
    }

    /// <summary>The tests of the last test session of this run of the app, each with how it ended and what its measurements say, in the order they ran.</summary>
    public IReadOnlyList<TestCheck> TestRuns() { lock (_lock) return _tests; }

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
                var cpu = _inventory.IsLoaded ? _inventory.GetAsync().Result.Cpu : null;
                string? name = cpu?.Name ?? _engine.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Cpu && n.ParentId is null)?.Name;
                findings = CpuCheck.Evaluate(CheckupTraces.Cpu(_engine, run.Result.StartedAt, run.Result.FinishedAt, id == "bench.cpu.multi", cpu?.MaxClockMhz, _onBattery, CpuSpecs.Find(name)));
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

    /// <summary>The tests that keep every core busy: only over them is the processor's load and clock judged (one core at a time is another matter).</summary>
    private static readonly string[] FullCpuTests = ["cpu.stress", "cpu.matrix", "cpu.linpack", "cpu.vector", "cpu.integer", "cpu.fft", "cpu.hash"];
    /// <summary>The tests that hold the graphics card at a steady full load (the variable and pulsed ones leave it on purpose).</summary>
    private static readonly string[] FullGpuTests = ["gpu.steady", "gpu.scene.d3d", "gpu.scene.rt"];
    /// <summary>The median load under which a part is said not to have been fully used, as <see cref="CpuCheck"/> draws it.</summary>
    public const double FullLoadPercent = 85;
    private static readonly TimeSpan ShortestJudged = TimeSpan.FromSeconds(15);

    /// <summary>Starts watching a test run: on an NVIDIA card, what the driver says holds the clock. <see cref="TestWatch.Judge"/> ends it.</summary>
    public TestWatch WatchTests(string? gpuName)
    {
        NvidiaRunProbe? probe = null; bool? battery = null;
        try { battery = PowerSettings.Read().OnMains is { } mains ? !mains : null; if (gpuName is not null) probe = NvidiaRunProbe.Start(gpuName); }
        catch (Exception e) { _log.LogWarning(e, "Checkup: watching the tests failed"); }
        return new(this, probe, battery, gpuName);
    }

    /// <summary>What a part did while its tests ran, from the monitor's record: its hottest reading, its median load over the tests that load it
    /// fully (null when none of them ran or it has no such sensor), and the checkup's findings over those tests.</summary>
    public sealed record PartJudgment(HardwareKind Part, string? Name, double? TempMaxC, double? LoadPercent, IReadOnlyList<Finding> Findings, IReadOnlyDictionary<string, IReadOnlyList<Finding>>? ByTest = null)
    {
        public bool? FullyLoaded => LoadPercent is { } l ? l >= FullLoadPercent : null;
    }

    public sealed class TestWatch(CheckupService owner, NvidiaRunProbe? probe, bool? onBattery, string? gpuName) : IDisposable
    {
        private NvidiaRunProbe? _probe = probe;
        public void Dispose() { _probe?.Dispose(); _probe = null; }

        /// <summary>Judges the processor and the graphics card over the tests that ran (a test that did not run adds nothing).</summary>
        public IReadOnlyList<PartJudgment> Judge(IReadOnlyList<TestRunResult> results)
        {
            var probed = _probe?.Stop(); Dispose();
            var ran = results.Where(r => r.Outcome is TestOutcome.Passed or TestOutcome.Failed or TestOutcome.Inconclusive && r.FinishedAt - r.StartedAt >= ShortestJudged).ToList();
            var parts = new List<PartJudgment>();
            try
            {
                if (ran.Where(r => r.Id.Value.StartsWith("cpu.", StringComparison.Ordinal)).ToList() is { Count: > 0 } cpuRuns) parts.Add(owner.JudgeCpu(cpuRuns, onBattery));
                if (ran.Where(r => r.Id.Value.StartsWith("gpu.", StringComparison.Ordinal)).ToList() is { Count: > 0 } gpuRuns) parts.Add(owner.JudgeGpu(gpuRuns, gpuName, probed));
            }
            catch (Exception e) { owner._log.LogWarning(e, "Checkup of a test run failed"); }
            return parts;
        }
    }

    private PartJudgment JudgeCpu(IReadOnlyList<TestRunResult> runs, bool? onBattery)
    {
        var cpu = _inventory.IsLoaded ? _inventory.GetAsync().Result.Cpu : null;
        string? name = cpu?.Name ?? _engine.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Cpu && n.ParentId is null)?.Name;
        double? temp = null; var loads = new List<double>(); var findings = new List<Finding>(); var byTest = new Dictionary<string, IReadOnlyList<Finding>>();
        foreach (var r in runs)
        {
            bool full = FullCpuTests.Contains(r.Id.Value) && r.Detail?.Contains(Mazesta.Diagnostics.Cpu.CpuStressExecutor.PartLoadMark, StringComparison.Ordinal) != true;   // (a run asked to swing or hold back its load is not judged as a full one)
            var trace = CheckupTraces.Cpu(_engine, r.StartedAt, r.FinishedAt!.Value, full, cpu?.MaxClockMhz, onBattery, CpuSpecs.Find(name));
            if (trace.Temp is { Count: > 0 } t) temp = Math.Max(temp ?? double.MinValue, t.Max());
            if (!full) continue;
            if (trace.Load?.Between(CpuCheck.WarmupSeconds, double.MaxValue) is { Count: >= CpuCheck.MinSamples } load) loads.Add(load.Median());
            var found = CpuCheck.Evaluate(trace); findings.AddRange(found); byTest[r.Id.Value] = found;
        }
        return new(HardwareKind.Cpu, name?.Trim(), temp, loads.Count > 0 ? loads.Min() : null, Worst(findings), byTest);
    }

    private PartJudgment JudgeGpu(IReadOnlyList<TestRunResult> runs, string? name, (GpuThrottleCounts Counts, int? Gen, int? Width)? probed)
    {
        var slot = SlotOf(name);
        var link = slot is null ? null : new GpuLink(probed?.Gen ?? slot.Port.CurrentGen, probed?.Width ?? slot.Port.CurrentWidth, slot.CardMaxGen, slot.CardMaxWidth, slot.Port.MaxGen, slot.Port.MaxWidth, probed?.Gen is not null);
        double? temp = null; var loads = new List<double>(); var findings = new List<Finding>(); var byTest = new Dictionary<string, IReadOnlyList<Finding>>();
        foreach (var r in runs)
        {
            var trace = CheckupTraces.Gpu(_engine, r.StartedAt, r.FinishedAt!.Value, GpuDevices.SensorNode(_engine, name ?? ""), name, probed?.Counts, link);
            if (trace.CoreTemp is { Count: > 0 } t) temp = Math.Max(temp ?? double.MinValue, t.Max());
            if (!FullGpuTests.Contains(r.Id.Value)) continue;
            if (trace.Load?.Between(CpuCheck.WarmupSeconds, double.MaxValue) is { Count: >= CpuCheck.MinSamples } load) loads.Add(load.Median());
            var found = GpuCheck.Evaluate(trace); findings.AddRange(found); byTest[r.Id.Value] = found;
        }
        return new(HardwareKind.Gpu, name, temp, loads.Count > 0 ? loads.Min() : null, Worst(findings), byTest);
    }

    /// <summary>Each finding once, as its worst run had it, the gravest first.</summary>
    private static IReadOnlyList<Finding> Worst(IEnumerable<Finding> all) => [.. all.GroupBy(f => f.Code).Select(g => g.MaxBy(f => f.Level)!).OrderByDescending(f => f.Level)];

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
        foreach (var (name, errors) in CheckupTraces.PcieErrors(_engine)) found.AddRange(GpuCheck.PcieErrors(errors, name));
        try { found.AddRange(DriveCheck.Evaluate(await Task.Run(_drives.Read).ConfigureAwait(false))); }
        catch (Exception e) { _log.LogWarning(e, "Checkup: reading the drives' health failed"); }
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
