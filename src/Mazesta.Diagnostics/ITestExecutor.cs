using Mazesta.Core.Time; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics;

/// <summary>One run's inputs. Engine is nullable so executors stay unit-testable; an executor that wants
/// measured evidence (spec §8: a speed score alone is not a health pass) reads Engine.History for its
/// own time window. Progress is a plain callback fired on the caller's thread - marshalling to the UI
/// is the subscriber's job, as with PollingEngine's events.</summary>
public sealed record TestExecutionRequest(int DurationSeconds, IClock Clock, Action<TestProgress>? Progress, PollingEngine? Engine);

/// <summary>Runs exactly one pass of one test; repeat handling belongs to <see cref="TestEngine"/>.</summary>
public interface ITestExecutor
{
    TestDefinition Definition { get; }
    Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct);
}
