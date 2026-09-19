using Xunit; using Mazesta.Diagnostics.Network; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class NetworkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    private static TestExecutionRequest Request(int seconds = 1) => new(seconds, new FakeClock(T0), null, null);
    private static readonly IReadOnlyList<string> Up = ["Ethernet (1000 Mbps)"];

    [Fact] public async Task No_connected_adapter_is_Unsupported()
        => Assert.Equal(TestOutcome.Unsupported, (await new NetworkLatencyExecutor((_, _, _) => Task.FromResult<long?>(5), () => []).RunAsync(Request(), CancellationToken.None)).Outcome);

    [Fact] public async Task Every_echo_answered_passes_with_latency_and_jitter_in_the_evidence()
    {
        int n = 0;
        var r = await new NetworkLatencyExecutor((_, _, _) => Task.FromResult<long?>(10 + (n++ % 2 == 0 ? 0 : 4)), () => Up).RunAsync(Request(), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Equal(0, r.ErrorCount);
        Assert.Contains("lost=0.0%", r.Detail); Assert.Contains("jitter", r.Detail); Assert.Contains("Ethernet", r.Detail);
    }
    [Fact] public async Task An_adapter_that_gets_no_reply_at_all_is_Failed_not_Unsupported()
        => Assert.Equal(TestOutcome.Failed, (await new NetworkLatencyExecutor((_, _, _) => Task.FromResult<long?>(null), () => Up).RunAsync(Request(), CancellationToken.None)).Outcome);

    [Fact] public async Task Loss_above_five_percent_fails_and_the_lost_echoes_are_the_error_count()
    {
        int n = 0;
        var r = await new NetworkLatencyExecutor((_, _, _) => Task.FromResult<long?>(n++ % 2 == 0 ? 8 : null), () => Up).RunAsync(Request(), CancellationToken.None);
        Assert.Equal(TestOutcome.Failed, r.Outcome); Assert.True(r.ErrorCount > 0);
    }
    [Fact] public async Task A_target_that_is_neither_an_address_nor_resolvable_is_Unsupported()
    {
        var options = new TestOptions(NetworkLatencyExecutor.Definition, new Dictionary<string, string> { [NetworkLatencyExecutor.TargetOption] = "no-such-host.invalid" });
        var r = await new NetworkLatencyExecutor((_, _, _) => Task.FromResult<long?>(5), () => Up).RunAsync(new(1, new FakeClock(T0), null, null, options), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, r.Outcome);
    }
    [Fact] public void Jitter_is_the_mean_absolute_difference_of_consecutive_round_trips()
    {
        Assert.Equal(0, NetworkLatencyExecutor.Jitter([10]));
        Assert.Equal(4, NetworkLatencyExecutor.Jitter([10, 14, 9, 12]), 6);   // (|4| + |5| + |3|) / 3
    }
}
