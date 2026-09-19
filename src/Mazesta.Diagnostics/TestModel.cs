namespace Mazesta.Diagnostics;

public readonly record struct TestId(string Value) { public override string ToString() => Value; }

/// <summary>Static description of one runnable test. Any positive duration is valid (spec §8 dropped the
/// old five-minute floor); the executor rejects zero/negative as Unsupported.</summary>
public sealed record TestDefinition(TestId Id, string NameKey, int DefaultDurationSeconds);

/// <summary>Spec §8: cancelled, never-run, unsupported and failed are distinct - a skipped test must
/// never be reported as a pass. Running is a display state only; no executor returns it.</summary>
public enum TestOutcome { NotRun, Running, Passed, Failed, Cancelled, Unsupported }

public readonly record struct TestProgress(double PercentComplete, string StatusKey);

/// <summary>Detail is a technical English string for the log/JSON (spec §12); the UI localises Outcome and
/// shows Detail as measured evidence, never as the customer-facing verdict.</summary>
public sealed record TestRunResult(TestId Id, TestOutcome Outcome, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long ErrorCount, string? Detail)
{
    public static TestRunResult Unsupported(TestId id, DateTimeOffset now, string detail) => new(id, TestOutcome.Unsupported, now, now, 0, detail);
    public static TestRunResult Cancelled(TestId id, DateTimeOffset started, DateTimeOffset now) => new(id, TestOutcome.Cancelled, started, now, 0, null);

    /// <summary>Folds the next repeat iteration into this one: errors add up, the span widens, and the
    /// worse outcome wins together with its own Detail. Failed outranks everything so a fault caught
    /// on loop 3 is never hidden by a later cancel or a later clean pass; on a tie the earlier result
    /// (and its Detail) is kept.</summary>
    public TestRunResult Combine(TestRunResult next)
    {
        var worse = Rank(next.Outcome) > Rank(Outcome) ? next : this;
        return worse with { StartedAt = StartedAt, FinishedAt = next.FinishedAt, ErrorCount = ErrorCount + next.ErrorCount };
    }

    private static int Rank(TestOutcome o) => o switch { TestOutcome.Failed => 3, TestOutcome.Unsupported => 2, TestOutcome.Cancelled => 1, _ => 0 };
}
