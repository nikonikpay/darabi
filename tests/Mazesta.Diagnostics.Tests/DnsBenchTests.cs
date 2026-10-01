using Xunit; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Diagnostics.Tests;

public class DnsBenchTests
{
    [Fact] public void The_query_is_a_standard_recursive_a_lookup()
    {
        var q = DnsBench.Query("github.com", 0x1234);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 6, (byte)'g', (byte)'i', (byte)'t', (byte)'h', (byte)'u', (byte)'b', 3, (byte)'c', (byte)'o', (byte)'m', 0, 0, 1, 0, 1 }, q);
    }
    [Fact] public void Only_the_answer_to_this_query_counts()
    {
        byte[] ok = [0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0], noName = [0x12, 0x34, 0x81, 0x83, 0, 1, 0, 0, 0, 0, 0, 0];
        Assert.True(DnsBench.IsAnswer(ok, 0x1234)); Assert.True(DnsBench.IsAnswer(noName, 0x1234));
        Assert.False(DnsBench.IsAnswer(ok, 0x1235));
        Assert.False(DnsBench.IsAnswer([0x12, 0x34, 0x81, 0x82, 0, 1, 0, 0, 0, 0, 0, 0], 0x1234));   // server failure
        Assert.False(DnsBench.IsAnswer([0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0], 0x1234));   // a query, not a response
    }
    [Fact] public void The_fastest_that_answered_everything_wins()
    {
        var best = DnsBench.Best([new("fast-but-flaky", "1", 11, 12, 10), new("steady", "2", 12, 12, 40), new("slow", "3", 12, 12, 90), new("dead", "4", 0, 12, null)]);
        Assert.Equal("steady", best!.Provider);
        Assert.Null(DnsBench.Best([new("dead", "4", 0, 12, null)]));
        Assert.Equal(2.5, DnsBench.Median([4, 1, 3, 2]));
    }
}
