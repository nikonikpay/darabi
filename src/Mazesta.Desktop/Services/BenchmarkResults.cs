using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Desktop.Services;

public sealed record RecordedBenchmark(TestDefinition Definition, BenchmarkResult Result);

/// <summary>The most recent completed run of each benchmark since the app started. The next test report lists these,
/// each with the time it was measured, so a benchmark run before the test session is not presented as part of it.</summary>
public sealed class BenchmarkResults
{
    private readonly object _lock = new(); private readonly Dictionary<TestId, RecordedBenchmark> _latest = [];

    public void Record(TestDefinition definition, BenchmarkResult result)
    {
        if (result.Status != BenchmarkStatus.Completed) return;   // a cancelled or unsupported run measured nothing
        lock (_lock) _latest[definition.Id] = new(definition, result);
    }

    public IReadOnlyList<RecordedBenchmark> Snapshot() { lock (_lock) return [.. _latest.Values.OrderBy(r => r.Result.FinishedAt)]; }
}
