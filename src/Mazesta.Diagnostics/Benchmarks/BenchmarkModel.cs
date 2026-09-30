using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Benchmarks;

/// <summary>One measured number. Key is a localisation key for the metric's name; there is no score and no pass -
/// a benchmark reports what was measured, and a value that could not be measured is left out, never zero.</summary>
public sealed record BenchmarkMetric(string Key, double Value, string Unit);

public enum BenchmarkStatus { Completed, Cancelled, Unsupported, Failed }

/// <param name="Setup">How the run was set up (its length, its options, a fixed resolution), group <see cref="BenchmarkDetails.RunGroup"/>: kept with the
/// run so two results can be told apart by how they were made, not only by what they measured.</param>
public sealed record BenchmarkResult(TestId Id, BenchmarkStatus Status, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<BenchmarkMetric> Metrics, string? Detail,
    IReadOnlyList<SpecItem>? Setup = null)
{
    public static BenchmarkResult Unsupported(TestId id, DateTimeOffset now, string detail) => new(id, BenchmarkStatus.Unsupported, now, now, [], detail);
    public static BenchmarkResult Cancelled(TestId id, DateTimeOffset started, DateTimeOffset now) => new(id, BenchmarkStatus.Cancelled, started, now, [], null);
    public static BenchmarkResult Failed(TestId id, DateTimeOffset started, DateTimeOffset now, string detail) => new(id, BenchmarkStatus.Failed, started, now, [], detail);
}

public static class BenchmarkRequestExtensions
{
    public static void Report(this TestExecutionRequest request, double fraction) => request.Progress?.Invoke(new TestProgress(Math.Clamp(fraction, 0, 1), "Test_Status_Running"));

    /// <summary>Adds what the monitor measured for a role over the run (its average, or its peak), and nothing when it measured nothing.</summary>
    public static void AddSensor(this List<BenchmarkMetric> metrics, TestExecutionRequest request, HardwareKind kind, SensorRole role, DateTimeOffset from, DateTimeOffset to, string key, Unit unit,
        bool peak = false, Func<HardwareNode, bool>? node = null, Func<SensorDefinition, bool>? sensor = null)
    {
        if (SensorEvidence.Read(request.Engine, kind, role, from, to, node, sensor) is { } s) metrics.Add(new(key, peak ? s.Max : s.Average, Units.Symbol(unit)));
    }

    /// <summary>The first of <paramref name="roles"/> the machine measured (the same quantity is another sensor per vendor).</summary>
    public static void AddFirst(this List<BenchmarkMetric> metrics, TestExecutionRequest request, HardwareKind kind, DateTimeOffset from, DateTimeOffset to, string key, Unit unit, bool peak,
        Func<HardwareNode, bool>? node, params SensorRole[] roles)
    {
        foreach (var role in roles)
            if (SensorEvidence.Read(request.Engine, kind, role, from, to, node, null) is { } s) { metrics.Add(new(key, peak ? s.Max : s.Average, Units.Symbol(unit))); return; }
    }
}

/// <summary>A measurement run. It reuses <see cref="TestDefinition"/> (name key, default duration, options) and
/// <see cref="TestExecutionRequest"/> so the page can render options the same way the Test Center does.</summary>
public interface IBenchmark
{
    TestDefinition Definition { get; }
    /// <summary>The part it measures: the component pages (CPU, GPU, Storage, Network) show their own benchmarks.</summary>
    HardwareKind Component { get; }
    Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct);
}
