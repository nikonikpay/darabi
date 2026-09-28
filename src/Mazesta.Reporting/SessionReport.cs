using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

public enum ReportOutcome { Passed, Failed, Cancelled, Unsupported, NotRun }

/// <summary>Passed only when every test that was asked for ran and passed. A cancelled, unsupported or never-run test makes the
/// report Incomplete - a skipped test is never presented as a pass (spec §8).</summary>
public enum ReportVerdict { Passed, Failed, Incomplete }

/// <summary>A test session (tests, with a verdict), or a benchmark run on its own: measurements with no verdict at all, since a speed
/// number is neither a pass nor a fail.</summary>
public enum ReportKind { TestSession, Benchmark }

public sealed record TestEntry(string Id, string Name, ReportOutcome Outcome, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, double DurationSeconds,
    long ErrorCount, string? Detail, IReadOnlyDictionary<string, string> Options);

public sealed record TracePoint(double Seconds, double Value);

/// <summary>One measured sensor over the session window. Everything comes from the readings the monitor recorded; a sensor without samples is not listed.</summary>
public sealed record SensorSummary(string Id, string Hardware, string Name, string Kind, string Unit, double Min, double Average, double Max, int Samples, IReadOnlyList<TracePoint> Trace,
    string? Role = null);   // the sensor's role (CpuPackageTemp, GpuHotSpotTemp…); absent in reports saved before it was recorded

/// <summary>The highest reading of one sensor between <see cref="From"/> and <see cref="To"/> - one test's or benchmark's own run - from every
/// recorded sample, not the down-sampled trace.</summary>
public sealed record WindowPeak(DateTimeOffset From, DateTimeOffset To, string SensorId, double Max);

/// <summary>One measured benchmark number as it was shown to the technician (Name already localised, like a test name).</summary>
public sealed record BenchmarkMetricEntry(string Name, double Value, string Unit);

/// <summary>A finished benchmark run. Benchmarks measure, they do not pass or fail, so they never change the report verdict.</summary>
public sealed record BenchmarkEntry(string Id, string Name, DateTimeOffset FinishedAt, IReadOnlyList<BenchmarkMetricEntry> Metrics, string? Detail, DateTimeOffset? StartedAt = null);

public sealed record ReportCounts(int Total, int Passed, int Failed, int Cancelled, int Unsupported, int NotRun, long Errors);

public sealed record SessionReport(int SchemaVersion, string Id, DateTimeOffset CreatedAt, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string ShopName, string AppVersion,
    ReportVerdict? Verdict, ReportCounts Counts, IReadOnlyList<TestEntry> Tests, IReadOnlyList<SensorSummary> Sensors, HardwareInventory Machine, IReadOnlyList<BenchmarkEntry>? Benchmarks = null,
    ReportKind Kind = ReportKind.TestSession,   // absent (TestSession) in reports saved before benchmark reports existed
    string? ServiceNumber = null,               // the shop's job number the technician entered (spec 7.1); absent when none was
    IReadOnlyList<WindowPeak>? Peaks = null)    // each test's and benchmark's own peak readings; absent in older reports
{
    public const int CurrentSchemaVersion = 1;
    public double DurationSeconds => Math.Max(0, (FinishedAt - StartedAt).TotalSeconds);

    public static SessionReport Create(string shopName, string appVersion, DateTimeOffset createdAt, IReadOnlyList<TestEntry> tests, IReadOnlyList<SensorSummary> sensors, HardwareInventory machine, string? id = null, IReadOnlyList<BenchmarkEntry>? benchmarks = null, string? serviceNumber = null)
    {
        var counts = new ReportCounts(tests.Count, tests.Count(t => t.Outcome == ReportOutcome.Passed), tests.Count(t => t.Outcome == ReportOutcome.Failed), tests.Count(t => t.Outcome == ReportOutcome.Cancelled),
            tests.Count(t => t.Outcome == ReportOutcome.Unsupported), tests.Count(t => t.Outcome == ReportOutcome.NotRun), tests.Sum(t => t.ErrorCount));
        var verdict = counts.Failed > 0 ? ReportVerdict.Failed : counts.Total > 0 && counts.Passed == counts.Total ? ReportVerdict.Passed : ReportVerdict.Incomplete;
        var started = tests.Count > 0 ? tests.Min(t => t.StartedAt) : createdAt; var finished = tests.Count > 0 ? tests.Max(t => t.FinishedAt) : createdAt;
        return new(CurrentSchemaVersion, id ?? Guid.NewGuid().ToString("N"), createdAt, started, finished, shopName, appVersion, verdict, counts, tests, sensors, machine, benchmarks is { Count: > 0 } ? benchmarks : null, ReportKind.TestSession, Service(serviceNumber));
    }

    /// <summary>A report of one or more benchmark runs on their own, spanning the runs.</summary>
    public static SessionReport CreateBenchmark(string shopName, string appVersion, DateTimeOffset createdAt, IReadOnlyList<BenchmarkEntry> benchmarks, IReadOnlyList<SensorSummary> sensors, HardwareInventory machine, string? serviceNumber = null)
        => new(CurrentSchemaVersion, Guid.NewGuid().ToString("N"), createdAt, benchmarks.Min(b => b.StartedAt ?? b.FinishedAt), benchmarks.Max(b => b.FinishedAt), shopName, appVersion,
            null, new(0, 0, 0, 0, 0, 0, 0), [], sensors, machine, benchmarks, ReportKind.Benchmark, Service(serviceNumber));

    private static string? Service(string? number) => string.IsNullOrWhiteSpace(number) ? null : number.Trim();
}
