using Xunit; using Mazesta.Core.Overlay;
namespace Mazesta.Core.Tests;

public class PingWindowTests
{
    [Fact] public void Nothing_is_given_before_it_is_measured()
    {
        var w = new PingWindow();
        Assert.Equal(new PingReading(null, null, null), w.Read());
        w.Add(20);
        Assert.Equal(new PingReading(20, null, null), w.Read());   // one reply: a ping, no loss or jitter yet
    }

    [Fact] public void Loss_is_the_share_of_echoes_without_a_reply_and_a_lost_echo_has_no_ping()
    {
        var w = new PingWindow();
        foreach (double? e in new double?[] { 20, 24, null, 22, null }) w.Add(e);
        var r = w.Read();
        Assert.Null(r.PingMs); Assert.Equal(40, r.LossPercent);
        Assert.Equal(4, r.JitterMs);   // only 20 -> 24 followed each other
    }

    [Fact] public void Only_the_last_echoes_count()
    {
        var w = new PingWindow(size: 5);
        foreach (double? e in new double?[] { null, null, null, 10, 12, 16, 16, 20 }) w.Add(e);
        var r = w.Read();
        Assert.Equal((20.0, 0.0, 2.5), (r.PingMs, r.LossPercent, r.JitterMs));
    }

    [Fact] public void The_game_set_shows_the_link() => Assert.Superset(new HashSet<string> { "net.ping", "net.loss", "net.jitter", "net.down", "net.up" }, OverlayCatalog.Presets["game"].Select(c => c.Id).ToHashSet());
}
