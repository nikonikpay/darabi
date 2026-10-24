using Mazesta.Diagnostics.Gpu.Benchmarks; using Xunit;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class StallGuardTests
{
    [Fact] public void A_call_that_returns_gives_its_answer() { bool gone = false; Assert.True(StallGuard.Run(() => 7, 5, CancellationToken.None, ref gone, out int v)); Assert.Equal(7, v); Assert.False(gone); }

    [Fact] public void A_call_that_throws_throws_the_same_exception_here() { bool gone = false; Assert.Throws<InvalidOperationException>(() => StallGuard.Run<int>(() => throw new InvalidOperationException("no decoder"), 5, CancellationToken.None, ref gone, out _)); }

    [Fact] public void A_call_that_never_returns_is_given_up_after_the_limit_and_marked_abandoned()
    {
        bool gone = false; var forever = new ManualResetEventSlim();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(StallGuard.Run(() => { forever.Wait(); return 1; }, 0.5, CancellationToken.None, ref gone, out int _));
        Assert.True(gone); Assert.InRange(clock.Elapsed.TotalSeconds, 0.4, 5); forever.Set();
    }

    [Fact] public void Cancel_ends_the_wait_at_once_even_while_the_call_hangs()
    {
        bool gone = false; var forever = new ManualResetEventSlim(); using var cts = new CancellationTokenSource(); cts.CancelAfter(300);
        var clock = System.Diagnostics.Stopwatch.StartNew(); bool abandoned = false;
        Assert.Throws<OperationCanceledException>(() => StallGuard.Run(() => { forever.Wait(); return 1; }, 60, cts.Token, ref abandoned, out int _));
        Assert.True(abandoned); Assert.False(gone); Assert.True(clock.Elapsed.TotalSeconds < 10); forever.Set();
    }

    [Fact] public void A_call_that_obeys_Cancel_is_not_left_behind()
    {
        using var cts = new CancellationTokenSource(); cts.CancelAfter(200); bool abandoned = false;
        Assert.Throws<OperationCanceledException>(() => StallGuard.Run(() => { while (true) { Thread.Sleep(20); cts.Token.ThrowIfCancellationRequested(); } }, 60, cts.Token, ref abandoned, out int _));
        Assert.False(abandoned);
    }
}
