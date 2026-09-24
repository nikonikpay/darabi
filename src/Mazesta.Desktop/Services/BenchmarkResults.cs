using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Desktop.Services;

public sealed record RecordedBenchmark(TestDefinition Definition, BenchmarkResult Result);

/// <summary>The most recent completed run of each benchmark since the app started. Every completed run is announced through
/// <see cref="Recorded"/> (ReportService saves it as a benchmark report), and the next test report lists the latest of each,
/// with the time it was measured, so a benchmark run before the test session is not presented as part of it.</summary>
public sealed class BenchmarkResults
{
    private readonly object _lock = new(); private readonly Dictionary<TestId, RecordedBenchmark> _latest = [];
    public event Action<RecordedBenchmark>? Recorded;

    public void Record(TestDefinition definition, BenchmarkResult result)
    {
        if (result.Status != BenchmarkStatus.Completed) return;   // a cancelled or unsupported run measured nothing
        var recorded = new RecordedBenchmark(definition, result);
        lock (_lock) _latest[definition.Id] = recorded;
        Recorded?.Invoke(recorded);
    }

    public IReadOnlyList<RecordedBenchmark> Snapshot() { lock (_lock) return [.. _latest.Values.OrderBy(r => r.Result.FinishedAt)]; }
}
