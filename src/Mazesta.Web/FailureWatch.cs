using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks;
using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

/// <summary>
/// Every test or benchmark that broke or found a fault is written to the app's log with the reason and, for an exception, its whole stack (the live test
/// log and the result keep only a line), so the export in Settings carries what a support person needs. When the run is over, the page is told what
/// broke and what it most likely points at (<see cref="FailureAdvice"/>), once for the whole run, never while a run is still going.
/// </summary>
public sealed class FailureWatch : IDisposable
{
    private readonly TestEngine _tests; private readonly BenchmarkRunner _bench; private readonly IReadOnlyList<ITestExecutor> _executors; private readonly ILogger _log; private readonly Action<object> _tell;
    private readonly List<object> _pending = []; private readonly object _lock = new(); private bool _benchBusy;

    public FailureWatch(TestEngine tests, BenchmarkRunner bench, IEnumerable<ITestExecutor> executors, ILogger log, Action<object> tell)
    {
        _tests = tests; _bench = bench; _executors = [.. executors]; _log = log; _tell = tell;
        tests.TestCompleted += OnTest; tests.Crashed += OnTestCrashed; tests.StateChanged += OnTestState;
        bench.Finished += OnBench; bench.Crashed += OnBenchCrashed; bench.BusyChanged += OnBusy;
    }

    private void OnTestCrashed(TestId id, Exception e) => _log.LogError(e, "Test {Id} crashed", id.Value);
    private void OnBenchCrashed(TestId id, Exception e) => _log.LogError(e, "Benchmark {Id} crashed", id.Value);

    private void OnTest(TestId id, TestRunResult r)
    {
        string name = _executors.FirstOrDefault(e => e.Definition.Id == id)?.Definition.NameKey is { } key ? Loc.Get(key) : id.Value;
        Record(id.Value, name, r.Outcome.ToString(), r.Outcome is TestOutcome.Failed or TestOutcome.Error, r.Outcome is TestOutcome.Unsupported or TestOutcome.Inconclusive, r.Detail, r.ErrorCount);
    }

    private void OnBench(RecordedBenchmark run)
    {
        var r = run.Result;
        Record(run.Definition.Id.Value, Loc.Get(run.Definition.NameKey), r.Status.ToString(), r.Status is BenchmarkStatus.Failed or BenchmarkStatus.Error, r.Status == BenchmarkStatus.Unsupported, r.Detail, 0);
    }

    private void Record(string id, string name, string outcome, bool broke, bool skipped, string? detail, long errors)
    {
        if (skipped) { _log.LogWarning("{Id} {Outcome}: {Detail}", id, outcome, detail); return; }
        if (!broke) return;
        _log.LogError("{Id} {Outcome} ({Errors} error(s)): {Detail}", id, outcome, errors, detail);
        var kind = FailureAdvice.Classify(id, detail);
        lock (_lock) _pending.Add(new { id, name, outcome, kind = kind.ToString(), detail = Clip(detail), errors });
    }

    private static string? Clip(string? s) => s is { Length: > 600 } ? s[..600] + "…" : s;

    private void OnTestState(TestEngineState s) { if (s != TestEngineState.Running) Flush(); }
    private void OnBusy(bool busy) { _benchBusy = busy; if (!busy) Flush(); }

    /// <summary>Tells the page once nothing is running any more.</summary>
    private void Flush()
    {
        if (_tests.State == TestEngineState.Running || _benchBusy) return;
        List<object> items;
        lock (_lock) { if (_pending.Count == 0) return; items = [.. _pending]; _pending.Clear(); }
        _tell(new { items = items.Take(8), more = Math.Max(0, items.Count - 8) });
    }

    public void Dispose()
    {
        _tests.TestCompleted -= OnTest; _tests.Crashed -= OnTestCrashed; _tests.StateChanged -= OnTestState;
        _bench.Finished -= OnBench; _bench.Crashed -= OnBenchCrashed; _bench.BusyChanged -= OnBusy;
    }
}
