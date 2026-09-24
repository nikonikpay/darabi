using Mazesta.Core.Time; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Benchmarks;

public sealed record RecordedBenchmark(TestDefinition Definition, BenchmarkResult Result);

/// <summary>
/// Runs one benchmark at a time for the whole app session, as <see cref="TestEngine"/> does for tests: a run keeps going and its result
/// stays available while no page is looking, and the Benchmarks page (built per visit) only shows this state. Events fire on the
/// worker's thread; marshalling to the UI is the subscriber's job. A benchmark that throws is reported as Failed, never lost.
/// </summary>
public sealed class BenchmarkRunner(IEnumerable<IBenchmark> benchmarks, IClock clock, PollingEngine? engine)
{
    private readonly object _lock = new();
    private readonly Dictionary<TestId, BenchmarkResult> _last = [];
    private readonly Dictionary<TestId, RecordedBenchmark> _completed = [];
    private CancellationTokenSource? _cts;

    public IReadOnlyList<IBenchmark> Benchmarks { get; } = [.. benchmarks];
    /// <summary>The benchmark running now, or null.</summary>
    public TestId? Running { get; private set; }
    public event Action<TestId, double>? Progress;
    /// <summary>Every finished run, whatever its status. Completed ones are the ones worth saving (a report) and listing (the next test report).</summary>
    public event Action<RecordedBenchmark>? Finished;

    /// <summary>The last result of a benchmark in this session, whatever its status, for the page to show.</summary>
    public BenchmarkResult? Last(TestId id) { lock (_lock) return _last.GetValueOrDefault(id); }

    /// <summary>The most recent completed run of each benchmark, oldest first - what the next test report lists, each with its own time.</summary>
    public IReadOnlyList<RecordedBenchmark> Completed() { lock (_lock) return [.. _completed.Values.OrderBy(r => r.Result.FinishedAt)]; }

    /// <summary>Runs <paramref name="benchmark"/> unless one is already running (then null).</summary>
    public async Task<BenchmarkResult?> RunAsync(IBenchmark benchmark, int seconds, IReadOnlyDictionary<string, string> options)
    {
        var id = benchmark.Definition.Id; CancellationTokenSource cts;
        lock (_lock) { if (Running is not null) return null; Running = id; _cts = cts = new CancellationTokenSource(); }
        BenchmarkResult result;
        try
        {
            var request = new TestExecutionRequest(seconds, clock, p => Progress?.Invoke(id, p.PercentComplete), engine, new TestOptions(benchmark.Definition, options));
            result = await benchmark.RunAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) { result = BenchmarkResult.Failed(id, clock.UtcNow, clock.UtcNow, $"{e.GetType().Name}: {e.Message}"); }
        lock (_lock)
        {
            _last[id] = result;
            if (result.Status == BenchmarkStatus.Completed) _completed[id] = new(benchmark.Definition, result);   // a run that measured nothing replaces nothing
            Running = null; _cts = null;
        }
        cts.Dispose();
        Finished?.Invoke(new(benchmark.Definition, result));
        return result;
    }

    public void Cancel() { lock (_lock) _cts?.Cancel(); }
}
