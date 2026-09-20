using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

public enum ReportOutcome { Passed, Failed, Cancelled, Unsupported, NotRun }

/// <summary>Passed only when every test that was asked for ran and passed. A cancelled, unsupported or never-run test makes the
/// report Incomplete - a skipped test is never presented as a pass (spec §8).</summary>
public enum ReportVerdict { Passed, Failed, Incomplete }

public sealed record TestEntry(string Id, string Name, ReportOutcome Outcome, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, double DurationSeconds,
    long ErrorCount, string? Detail, IReadOnlyDictionary<string, string> Options);

public sealed record TracePoint(double Seconds, double Value);

/// <summary>One measured sensor over the session window. Everything comes from the readings the monitor recorded; a sensor without samples is not listed.</summary>
public sealed record SensorSummary(string Id, string Hardware, string Name, string Kind, string Unit, double Min, double Average, double Max, int Samples, IReadOnlyList<TracePoint> Trace);

public sealed record ReportCounts(int Total, int Passed, int Failed, int Cancelled, int Unsupported, int NotRun, long Errors);

public sealed record SessionReport(int SchemaVersion, string Id, DateTimeOffset CreatedAt, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string ShopName, string AppVersion,
    ReportVerdict Verdict, ReportCounts Counts, IReadOnlyList<TestEntry> Tests, IReadOnlyList<SensorSummary> Sensors, HardwareInventory Machine)
{
    public const int CurrentSchemaVersion = 1;
    public double DurationSeconds => Math.Max(0, (FinishedAt - StartedAt).TotalSeconds);

    public static SessionReport Create(string shopName, string appVersion, DateTimeOffset createdAt, IReadOnlyList<TestEntry> tests, IReadOnlyList<SensorSummary> sensors, HardwareInventory machine, string? id = null)
    {
        var counts = new ReportCounts(tests.Count, tests.Count(t => t.Outcome == ReportOutcome.Passed), tests.Count(t => t.Outcome == ReportOutcome.Failed), tests.Count(t => t.Outcome == ReportOutcome.Cancelled),
            tests.Count(t => t.Outcome == ReportOutcome.Unsupported), tests.Count(t => t.Outcome == ReportOutcome.NotRun), tests.Sum(t => t.ErrorCount));
        var verdict = counts.Failed > 0 ? ReportVerdict.Failed : counts.Total > 0 && counts.Passed == counts.Total ? ReportVerdict.Passed : ReportVerdict.Incomplete;
        var started = tests.Count > 0 ? tests.Min(t => t.StartedAt) : createdAt; var finished = tests.Count > 0 ? tests.Max(t => t.FinishedAt) : createdAt;
        return new(CurrentSchemaVersion, id ?? Guid.NewGuid().ToString("N"), createdAt, started, finished, shopName, appVersion, verdict, counts, tests, sensors, machine);
    }
}
