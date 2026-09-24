namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>One measured number. Key is a localisation key for the metric's name; there is no score and no pass -
/// a benchmark reports what was measured, and a value that could not be measured is left out, never zero.</summary>
public sealed record BenchmarkMetric(string Key, double Value, string Unit);

public enum BenchmarkStatus { Completed, Cancelled, Unsupported, Failed }

public sealed record BenchmarkResult(TestId Id, BenchmarkStatus Status, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<BenchmarkMetric> Metrics, string? Detail)
{
    public static BenchmarkResult Unsupported(TestId id, DateTimeOffset now, string detail) => new(id, BenchmarkStatus.Unsupported, now, now, [], detail);
}

/// <summary>A measurement run. It reuses <see cref="TestDefinition"/> (name key, default duration, options) and
/// <see cref="TestExecutionRequest"/> so the page can render options the same way the Test Center does.</summary>
public interface IBenchmark
{
    TestDefinition Definition { get; }
    Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct);
}
