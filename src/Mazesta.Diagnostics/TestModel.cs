namespace Mazesta.Diagnostics;

public enum TestCategory { Cpu, Gpu, Memory, Storage, Network, Power }

public readonly record struct TestId(string Value) { public override string ToString() => Value; }

/// <summary>Static description of one runnable test. MinDurationSeconds replaces the old five-minute
/// floor (spec §8): any positive duration is valid; unlimited vs. counted repeat is a separate choice
/// on the queue entry (see <see cref="QueuedTest"/>), not part of the definition.</summary>
public sealed record TestDefinition(TestId Id, TestCategory Category, string NameKey, int DefaultDurationSeconds, int MinDurationSeconds, bool SupportsRepeat);

/// <summary>Spec §8: cancelled, never-run, unsupported and failed are distinct states - a missing or
/// skipped test must never be reported to the customer as a pass.</summary>
public enum TestOutcome { NotRun, Queued, Running, Passed, Failed, Cancelled, Unsupported }

public readonly record struct TestProgress(double PercentComplete, string StatusKey, TimeSpan Elapsed);

/// <summary>Detail is a plain technical string (English, internal), the same split as
/// <c>ProviderStatus.ReasonKey</c>/<c>Detail</c>: the Desktop layer maps Outcome (a closed enum) to a
/// localised customer-facing line and keeps Detail for the technical log/JSON only (spec §12) - it is
/// never shown to the customer verbatim.</summary>
public sealed record TestRunResult(TestId Id, TestOutcome Outcome, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long ErrorCount, string? Detail)
{
    public static TestRunResult Unsupported(TestId id, DateTimeOffset now, string detail) => new(id, TestOutcome.Unsupported, now, now, 0, detail);
    public static TestRunResult Cancelled(TestId id, DateTimeOffset started, DateTimeOffset now) => new(id, TestOutcome.Cancelled, started, now, 0, null);
}
