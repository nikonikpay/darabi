using Mazesta.Core.Time; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Benchmarks;

/// <param name="Options">The option values the run was started with, copied at its start: a record or comparison is filed under what was
/// measured (which GPU, which drive), never under what the page shows by the time the run ends. Null only where a run is built by hand.</param>
public sealed record RecordedBenchmark(TestDefinition Definition, BenchmarkResult Result, IReadOnlyDictionary<string, string>? Options = null);

/// <summary>One benchmark of a queue, with the length and options the technician chose for it.</summary>
public sealed record BenchmarkJob(IBenchmark Benchmark, int Seconds, IReadOnlyDictionary<string, string> Options);

/// <summary>
/// Runs one benchmark at a time for the whole app session, as <see cref="TestEngine"/> does for tests: a run keeps going and its result
/// stays available while no page is looking, and the Benchmarks page (built per visit) only shows this state. A queue runs its benchmarks one
/// after another, never side by side (two would measure each other); Cancel stops the running one and drops the rest. Events fire on the
/// worker's thread; marshalling to the UI is the subscriber's job. A benchmark that throws is reported as Failed, never lost.
/// </summary>
public sealed class BenchmarkRunner(IEnumerable<IBenchmark> benchmarks, IClock clock, PollingEngine? engine, WorkloadGate? gate = null)
{
    private readonly object _lock = new();
    private readonly Dictionary<TestId, BenchmarkResult> _last = [];
    private readonly Dictionary<TestId, RecordedBenchmark> _completed = [];
    private CancellationTokenSource? _cts; private bool _queueActive, _queueCancelled; private IDisposable? _lease;

    /// <summary>What refused the last start (a test queue or the GPU tuning holding the gate), or null.</summary>
    public Workload? BlockedBy { get; private set; }

    /// <summary>Takes the shared gate for a run or a queue; false (and <see cref="BlockedBy"/> set) while another activity holds it. Called under _lock.</summary>
    private bool Enter()
    {
        BlockedBy = null;
        if (gate is null) return true;
        _lease = gate.TryEnter(Workload.Benchmark);
        if (_lease is null) BlockedBy = gate.Holder;
        return _lease is not null;
    }
    private void Leave() { lock (_lock) { _lease?.Dispose(); _lease = null; } }

    public IReadOnlyList<IBenchmark> Benchmarks { get; } = [.. benchmarks];
    /// <summary>The benchmark running now, or null.</summary>
    public TestId? Running { get; private set; }
    /// <summary>A queue is in progress: true from its start to its end, also between two of its benchmarks.</summary>
    public bool InQueue { get { lock (_lock) return _queueActive; } }
    /// <summary>Something is running or a queue is under way: nothing else may start.</summary>
    public bool IsBusy { get { lock (_lock) return Running is not null || _queueActive; } }
    /// <summary>A run is about to start (alone or in a queue), with the options it runs with: whoever watches the machine during a run starts here.</summary>
    public event Action<IBenchmark, IReadOnlyDictionary<string, string>>? Started;
    public event Action<TestId, double>? Progress;
    /// <summary>An exception that escaped a benchmark, whole, for the app's log.</summary>
    public event Action<TestId, Exception>? Crashed;
    /// <summary>Every finished run, whatever its status. Completed ones are the ones worth saving (a report) and listing (the next test report).</summary>
    public event Action<RecordedBenchmark>? Finished;
    /// <summary>A queue's benchmark is about to start: its position (from 0) and the queue's length.</summary>
    public event Action<TestId, int, int>? QueueAdvanced;
    /// <summary>A queue ended (all done, or cancelled), with every run it made, in order.</summary>
    public event Action<IReadOnlyList<RecordedBenchmark>>? QueueFinished;
    /// <summary>Something started running (true) or everything ended (false), raised once per change, also between the benchmarks of a queue
    /// only at its start and end: the app quiets its other work for as long as the machine is being measured.</summary>
    public event Action<bool>? BusyChanged;
    private bool _wasBusy;
    private void NotifyBusy() { bool busy = IsBusy, changed; lock (_lock) { changed = busy != _wasBusy; _wasBusy = busy; } if (changed) BusyChanged?.Invoke(busy); }

    /// <summary>The last result of a benchmark in this session, whatever its status, for the page to show.</summary>
    public BenchmarkResult? Last(TestId id) { lock (_lock) return _last.GetValueOrDefault(id); }

    /// <summary>The most recent completed run of each benchmark, oldest first - what the next test report lists, each with its own time.</summary>
    public IReadOnlyList<RecordedBenchmark> Completed() { lock (_lock) return [.. _completed.Values.OrderBy(r => r.Result.FinishedAt)]; }

    /// <summary>Runs <paramref name="benchmark"/> unless something is already running (then null).</summary>
    public async Task<BenchmarkResult?> RunAsync(IBenchmark benchmark, int seconds, IReadOnlyDictionary<string, string> options)
    {
        CancellationTokenSource cts;
        lock (_lock) { if (Running is not null || _queueActive || !Enter()) return null; Running = benchmark.Definition.Id; _cts = cts = new CancellationTokenSource(); }
        NotifyBusy();
        try { return (await RunOneAsync(benchmark, seconds, options, cts).ConfigureAwait(false)).Result; }
        finally { Leave(); NotifyBusy(); }
    }

    /// <summary>Runs the jobs one after another, in the given order, unless something is already running (then an empty list). A cancel stops the
    /// running benchmark and skips the ones after it; the list holds what did run.</summary>
    public async Task<IReadOnlyList<RecordedBenchmark>> RunQueueAsync(IReadOnlyList<BenchmarkJob> jobs)
    {
        lock (_lock) { if (Running is not null || _queueActive || jobs.Count == 0 || !Enter()) return []; _queueActive = true; _queueCancelled = false; }
        NotifyBusy();
        var runs = new List<RecordedBenchmark>();
        try
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i]; CancellationTokenSource cts;
                lock (_lock) { if (_queueCancelled) break; Running = job.Benchmark.Definition.Id; _cts = cts = new CancellationTokenSource(); }
                QueueAdvanced?.Invoke(job.Benchmark.Definition.Id, i, jobs.Count);
                runs.Add(await RunOneAsync(job.Benchmark, job.Seconds, job.Options, cts).ConfigureAwait(false));
            }
        }
        finally { lock (_lock) _queueActive = false; Leave(); NotifyBusy(); }
        QueueFinished?.Invoke(runs);
        return runs;
    }

    private async Task<RecordedBenchmark> RunOneAsync(IBenchmark benchmark, int seconds, IReadOnlyDictionary<string, string> options, CancellationTokenSource cts)
    {
        var id = benchmark.Definition.Id; BenchmarkResult result;
        var snapshot = new Dictionary<string, string>(options);   // the run's own copy: the caller's dictionary may be the page's live one
        options = snapshot;
        try
        {
            Started?.Invoke(benchmark, snapshot);
            var request = new TestExecutionRequest(seconds, clock, p => Progress?.Invoke(id, p.PercentComplete), engine, new TestOptions(benchmark.Definition, options));
            using var footprint = Evidence.RunFootprint.Start();   // what this program itself takes of the machine while it measures: kept with the result
            result = await benchmark.RunAsync(request, cts.Token).ConfigureAwait(false);
            if (result.Status == BenchmarkStatus.Completed) result = result with { Metrics = [.. result.Metrics, .. footprint.Stop().Metrics()] };
        }
        catch (OperationCanceledException) { result = BenchmarkResult.Cancelled(id, clock.UtcNow, clock.UtcNow); }
        catch (Exception e) { result = BenchmarkResult.Error(id, clock.UtcNow, clock.UtcNow, e); Crashed?.Invoke(id, e); }   // escaped the benchmark's own handling: the program's fault
        if (result.Status == BenchmarkStatus.Completed) result = result with { Setup = [.. Setup(benchmark.Definition, seconds, snapshot), .. result.Setup ?? []] };
        lock (_lock)
        {
            _last[id] = result;
            if (result.Status == BenchmarkStatus.Completed) _completed[id] = new(benchmark.Definition, result);   // a run that measured nothing replaces nothing
            Running = null; _cts = null;
        }
        cts.Dispose();
        var run = new RecordedBenchmark(benchmark.Definition, result, snapshot);
        Finished?.Invoke(run);
        return run;
    }

    /// <summary>How a run was set up, as the technician chose it: its length, the workload's version, and each option that changes the work (the
    /// device options name the part, which is kept apart). Values are as chosen; a choice keeps its label.</summary>
    internal static IEnumerable<SpecItem> Setup(TestDefinition definition, int seconds, IReadOnlyDictionary<string, string> options)
    {
        yield return new(BenchmarkDetails.RunGroup, "Bench_Set_Duration", $"{seconds} s");
        if (BenchmarkRecords.Headline(definition.Id.Value) is { } h) yield return new(BenchmarkDetails.RunGroup, "Bench_Set_Version", h.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var o in definition.Options.Where(o => !BenchmarkPeers.DeviceOptions.Contains(o.Key)))
        {
            string value = options.GetValueOrDefault(o.Key) is { Length: > 0 } v ? v : o.Default;
            var choice = o.Choices?.Invoke().FirstOrDefault(c => c.Value == value);
            string shown = choice?.Summary ?? (choice is { Localized: false } ? choice.Label : value);
            if (shown.Length > 0) yield return new(BenchmarkDetails.RunGroup, o.LabelKey, shown);
        }
    }

    public void Cancel() { lock (_lock) { if (_queueActive) _queueCancelled = true; _cts?.Cancel(); } }
}
