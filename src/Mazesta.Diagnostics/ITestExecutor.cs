using Mazesta.Core.Time; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics;

/// <summary>One run's real inputs. Engine is the live polling engine, nullable so an executor stays
/// unit-testable without a real hardware provider. Spec §8: "پایداری با بنچمارک تفاوت دارد" - a pass
/// must not rest on the executor's own claim alone, so an executor that wants measured evidence reads
/// Engine.History for the run's own time window after it finishes, rather than trusting a self-reported
/// counter. Progress is a plain callback, not IProgress&lt;T&gt;, so it fires synchronously on the
/// caller's thread the same way PollingEngine's own events do - marshalling to the UI thread is the
/// subscriber's job (ShellViewModel does the same for PollingEngine.StateChanged), not something this
/// type does implicitly via a captured SynchronizationContext.</summary>
public sealed record TestExecutionRequest(int DurationSeconds, IClock Clock, Action<TestProgress>? Progress, PollingEngine? Engine);

/// <summary>Runs exactly one pass of one test. Repeat handling (spec §8 loop mode) is
/// <see cref="TestEngine"/>'s job, not the executor's: an executor only ever reports what happened in
/// the single call it was given.</summary>
public interface ITestExecutor
{
    TestDefinition Definition { get; }
    Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct);
}
