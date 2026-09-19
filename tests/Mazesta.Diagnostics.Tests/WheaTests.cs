using Xunit; using Mazesta.Diagnostics.Tests.Fakes; using Mazesta.Diagnostics.Whea; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Diagnostics.Tests;

public class WheaTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly TestDefinition Def = new(new TestId("fake.a"), "Test_Fake_A", 5);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-whea-tests-" + Guid.NewGuid().ToString("N"));
    public WheaTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeSource(Func<DateTimeOffset, IReadOnlyList<HardwareErrorEvent>> read) : IHardwareErrorSource
    { public IReadOnlyList<HardwareErrorEvent> Since(DateTimeOffset start) => read(start); }

    private TestEngine Engine(IHardwareErrorSource? source, TestOutcome outcome = TestOutcome.Passed)
    {
        var executor = new FakeTestExecutor(Def, (r, ct) => Task.FromResult(new TestRunResult(Def.Id, outcome, r.Clock.UtcNow, r.Clock.UtcNow, 0, "clean run")));
        var store = new JsonStore<TestSessionCheckpoint>(Path.Combine(_dir, "cp.json"), new SchemaMigrator([]), TestSessionCheckpoint.CurrentSchemaVersion, NullLogger.Instance);
        return new TestEngine([executor], store, new FakeClock(T0), null, source);
    }
    private static async Task<TestRunResult> Run(TestEngine engine)
    {
        TestRunResult? result = null; engine.TestCompleted += (_, r) => result = r;
        await engine.RunAsync([new QueuedTest(Def, 5, RepeatMode.Once, 1)]);
        return result!;
    }

    [Fact] public async Task A_hardware_error_logged_during_a_passing_test_makes_it_Failed_with_the_evidence()
    {
        var events = new[] { new HardwareErrorEvent(T0, 18, "A fatal hardware error has occurred."), new HardwareErrorEvent(T0, 19, "A corrected hardware error has occurred.") };
        var result = await Run(Engine(new FakeSource(_ => events)));
        Assert.Equal(TestOutcome.Failed, result.Outcome); Assert.Equal(2, result.ErrorCount);
        Assert.Contains("2 hardware error", result.Detail); Assert.Contains("18, 19", result.Detail);
    }
    [Fact] public async Task No_hardware_errors_leaves_the_result_untouched()
    {
        var result = await Run(Engine(new FakeSource(_ => [])));
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal("clean run", result.Detail);
    }
    [Fact] public async Task An_unreadable_log_is_noted_but_never_fails_the_test()
    {
        var result = await Run(Engine(new FakeSource(_ => throw new UnauthorizedAccessException())));
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Contains("WHEA log could not be read", result.Detail);
    }
    [Fact] public async Task A_test_that_did_not_run_is_not_blamed_for_errors_logged_meanwhile()
    {
        var result = await Run(Engine(new FakeSource(_ => [new(T0, 18, "x")]), TestOutcome.Unsupported));
        Assert.Equal(TestOutcome.Unsupported, result.Outcome);
    }
    [Fact] public async Task The_query_starts_at_the_moment_the_test_started()
    {
        DateTimeOffset? asked = null;
        await Run(Engine(new FakeSource(s => { asked = s; return []; })));
        Assert.Equal(T0, asked);
    }

    [Fact, Trait("Category", "Hardware")]
    public void The_real_system_log_can_be_queried_for_the_last_thirty_days()
    {
        var events = new WheaErrorSource().Since(DateTimeOffset.UtcNow.AddDays(-30));
        Assert.NotNull(events);   // usually empty on a healthy machine; the point is that the query itself works
    }
}
