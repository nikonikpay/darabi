namespace Mazesta.Diagnostics;

public readonly record struct TestId(string Value) { public override string ToString() => Value; }

/// <summary>Static description of one runnable test. Any positive duration is valid (spec §8 dropped the
/// old five-minute floor); the executor rejects zero/negative as Unsupported.</summary>
public sealed record TestDefinition(TestId Id, string NameKey, int DefaultDurationSeconds, IReadOnlyList<TestOption> Options)
{
    public TestDefinition(TestId id, string nameKey, int defaultDurationSeconds) : this(id, nameKey, defaultDurationSeconds, []) { }
}

/// <summary>Spec §8: cancelled, never-run, unsupported and failed are distinct - a skipped test must
/// never be reported as a pass. Running is a display state only; no executor returns it.
/// <see cref="Error"/> is the test itself breaking (an exception in the app, an allocation it could not make): nothing was learnt about the
/// part, so it is never labelled a hardware fault. <see cref="Inconclusive"/> ran without an observed error but could not cover what it was
/// asked to (a core Windows would not pin to, a network that answers no echo at all): not a fault, and not a pass either.
/// Both are appended so values saved as numbers keep their meaning.</summary>
public enum TestOutcome { NotRun, Running, Passed, Failed, Cancelled, Unsupported, Error, Inconclusive }

/// <param name="Stage">For a test that goes through stages (the processor's full-load test): the key of the stage's name. The engine writes it
/// to the session's checkpoint the moment it changes, so a session that ends in a reset says which stage it was in.</param>
public readonly record struct TestProgress(double PercentComplete, string StatusKey, string? Stage = null);

/// <summary>Detail is a technical English string for the log/JSON (spec §12); the UI localises Outcome and
/// shows Detail as measured evidence, never as the customer-facing verdict.</summary>
public sealed record TestRunResult(TestId Id, TestOutcome Outcome, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, long ErrorCount, string? Detail)
{
    public static TestRunResult Unsupported(TestId id, DateTimeOffset now, string detail) => new(id, TestOutcome.Unsupported, now, now, 0, detail);
    public static TestRunResult Cancelled(TestId id, DateTimeOffset started, DateTimeOffset now) => new(id, TestOutcome.Cancelled, started, now, 0, null);
    /// <summary>The test broke, not the part: the exception's type and message are kept for the log, and no error is counted against the hardware.</summary>
    public static TestRunResult Error(TestId id, DateTimeOffset started, DateTimeOffset now, Exception e) => new(id, TestOutcome.Error, started, now, 0, $"The test itself failed ({e.GetType().Name}: {e.Message}); nothing is known about the part from this run.");

    /// <summary>Folds the next repeat iteration into this one: errors add up, the span widens, and the
    /// worse outcome wins together with its own Detail. Failed outranks everything so a fault caught
    /// on loop 3 is never hidden by a later cancel or a later clean pass; on a tie the earlier result
    /// (and its Detail) is kept.</summary>
    public TestRunResult Combine(TestRunResult next)
    {
        var worse = Rank(next.Outcome) > Rank(Outcome) ? next : this;
        return worse with { StartedAt = StartedAt, FinishedAt = next.FinishedAt, ErrorCount = ErrorCount + next.ErrorCount };
    }

    private static int Rank(TestOutcome o) => o switch { TestOutcome.Failed => 5, TestOutcome.Error => 4, TestOutcome.Unsupported => 3, TestOutcome.Cancelled => 2, TestOutcome.Inconclusive => 1, _ => 0 };
}
