using Xunit;
using Mazesta.Diagnostics.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Diagnostics.Tests;

public class TestEngineTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly TestDefinition Def1 = new(new TestId("fake.a"), TestCategory.Cpu, "Test_Fake_A", 5, 1, true);
    private static readonly TestDefinition Def2 = new(new TestId("fake.b"), TestCategory.Cpu, "Test_Fake_B", 5, 1, true);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-diag-tests-" + Guid.NewGuid().ToString("N"));
    public TestEngineTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private JsonStore<TestSessionCheckpoint> Store() => new(Path.Combine(_dir, "checkpoint.json"), new SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, NullLogger.Instance);
    private static TestRunResult Passed(TestId id, TestExecutionRequest r, long errors = 0, string? detail = null) => new(id, TestOutcome.Passed, r.Clock.UtcNow, r.Clock.UtcNow, errors, detail);

    [Fact] public async Task Queue_runs_every_item_in_order_and_marks_the_checkpoint_completed()
    {
        var order = new List<string>();
        var e1 = new FakeTestExecutor(Def1, (r, ct) => { order.Add("a"); return Task.FromResult(Passed(Def1.Id, r)); });
        var e2 = new FakeTestExecutor(Def2, (r, ct) => { order.Add("b"); return Task.FromResult(Passed(Def2.Id, r)); });
        var store = Store();
        var engine = new TestEngine([e1, e2], store, new FakeClock(T0));
        var completed = new List<TestOutcome>();
        engine.TestCompleted += (_, r) => completed.Add(r.Outcome);

        await engine.RunAsync([QueuedTest.Once(Def1, 5), QueuedTest.Once(Def2, 5)], CancellationToken.None);

        Assert.Equal(["a", "b"], order);
        Assert.Equal([TestOutcome.Passed, TestOutcome.Passed], completed);
        Assert.Equal(TestEngineState.Stopped, engine.State);
        Assert.Null(engine.FindIncompleteSession());   // saved Completed = true, so nothing looks like a crash
    }

    [Fact] public async Task Cancelling_before_an_item_starts_reports_it_Cancelled_without_calling_its_executor()
    {
        var cts = new CancellationTokenSource();
        var e1 = new FakeTestExecutor(Def1, (r, ct) => { cts.Cancel(); return Task.FromResult(Passed(Def1.Id, r)); });
        var e2 = new FakeTestExecutor(Def2, (r, ct) => Task.FromResult(Passed(Def2.Id, r)));
        var engine = new TestEngine([e1, e2], Store(), new FakeClock(T0));
        var results = new Dictionary<string, TestOutcome>();
        engine.TestCompleted += (id, r) => results[id.Value] = r.Outcome;

        await engine.RunAsync([QueuedTest.Once(Def1, 5), QueuedTest.Once(Def2, 5)], cts.Token);

        Assert.Equal(TestOutcome.Passed, results["fake.a"]);
        Assert.Equal(TestOutcome.Cancelled, results["fake.b"]);
        Assert.Equal(0, e2.CallCount);   // never invoked - cancellation was caught before the call, not inside it
    }

    [Fact] public async Task Queue_entry_with_no_registered_executor_is_Unsupported_not_an_exception()
    {
        var engine = new TestEngine([], Store(), new FakeClock(T0));
        TestRunResult? result = null; engine.TestCompleted += (_, r) => result = r;

        await engine.RunAsync([QueuedTest.Once(Def1, 5)], CancellationToken.None);

        Assert.Equal(TestOutcome.Unsupported, result!.Outcome);
    }

    [Fact] public async Task Count_repeat_runs_the_executor_exactly_that_many_times_and_sums_errors()
    {
        var e1 = new FakeTestExecutor(Def1, (r, ct) => Task.FromResult(Passed(Def1.Id, r, errors: 1)));
        var engine = new TestEngine([e1], Store(), new FakeClock(T0));
        TestRunResult? result = null; engine.TestCompleted += (_, r) => result = r;

        await engine.RunAsync([new QueuedTest(Def1, 5, RepeatMode.Count, 3)], CancellationToken.None);

        Assert.Equal(3, e1.CallCount);
        Assert.Equal(TestOutcome.Passed, result!.Outcome);
        Assert.Equal(3, result.ErrorCount);   // 1 error reported per iteration, 3 iterations
    }

    [Fact] public async Task Unlimited_repeat_stops_at_cancellation_instead_of_looping_forever()
    {
        var cts = new CancellationTokenSource();
        // Cancel from inside the third call so the loop's own re-check (not an external timer) is what stops it.
        int calls = 0;
        var e2 = new FakeTestExecutor(Def2, (r, ct) => { calls++; if (calls == 3) cts.Cancel(); return Task.FromResult(Passed(Def2.Id, r)); });
        var engine = new TestEngine([e2], Store(), new FakeClock(T0));
        TestRunResult? result = null; engine.TestCompleted += (_, r) => result = r;

        await engine.RunAsync([new QueuedTest(Def2, 5, RepeatMode.Unlimited, 0)], cts.Token);

        Assert.Equal(3, calls);
        Assert.Equal(TestOutcome.Cancelled, result!.Outcome);   // the 3rd call itself passed, but the loop noticed the cancel before a 4th
    }

    [Fact] public async Task A_passed_result_still_carries_its_executor_s_detail_text()
    {
        // Regression test: RunQueuedAsync used to only copy TestRunResult.Detail across when an
        // iteration's outcome was NOT Passed, so a passing executor's own evidence (thread count,
        // iterations, measured load - CpuMatrixStressExecutor's real payload) was silently dropped
        // and the Test Center row showed nothing. Caught by actually running the published app, not
        // by any test that existed before this one.
        var e1 = new FakeTestExecutor(Def1, (r, ct) => Task.FromResult(Passed(Def1.Id, r, detail: "threads=4; iterations=9001")));
        var engine = new TestEngine([e1], Store(), new FakeClock(T0));
        TestRunResult? result = null; engine.TestCompleted += (_, r) => result = r;

        await engine.RunAsync([QueuedTest.Once(Def1, 5)], CancellationToken.None);

        Assert.Equal(TestOutcome.Passed, result!.Outcome);
        Assert.Equal("threads=4; iterations=9001", result.Detail);
    }

    [Fact] public void FindIncompleteSession_reports_a_checkpoint_left_over_from_a_crash()
    {
        var store = Store();
        store.Save(new TestSessionCheckpoint { SessionId = "s1", QueueTestIds = ["fake.a"], CurrentIndex = 0, StartedAt = T0, LastUpdatedAt = T0, Completed = false });
        var engine = new TestEngine([], store, new FakeClock(T0));

        var found = engine.FindIncompleteSession();
        Assert.NotNull(found); Assert.Equal("s1", found!.SessionId);

        engine.DismissIncompleteSession();
        Assert.Null(engine.FindIncompleteSession());
    }
}
