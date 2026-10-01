using Xunit; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Diagnostics.Tests;

public class DnsBenchTests
{
    [Fact] public void The_query_is_a_standard_recursive_a_lookup()
    {
        var q = DnsBench.Query("github.com", 0x1234);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 6, (byte)'g', (byte)'i', (byte)'t', (byte)'h', (byte)'u', (byte)'b', 3, (byte)'c', (byte)'o', (byte)'m', 0, 0, 1, 0, 1 }, q);
    }
    // A reply to the query for github.com (id 0x1234): header flags, counts, the question echoed, then the answers.
    private static readonly byte[] Q = DnsBench.Query("github.com", 0x1234);
    private static byte[] Reply(byte flags2, int answers, params byte[][] records)
    {
        var p = new List<byte> { 0x12, 0x34, 0x81, flags2, 0, 1, 0, (byte)answers, 0, 0, 0, 0 };
        p.AddRange(Q[12..]); foreach (var r in records) p.AddRange(r); return [.. p];
    }
    private static readonly byte[] A = [0xC0, 12, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 140, 82, 121, 4];
    private static readonly byte[] Cname = [0xC0, 12, 0, 5, 0, 1, 0, 0, 0, 60, 0, 2, 0xC0, 12];

    [Fact] public void Only_an_address_or_an_alias_for_the_name_asked_is_an_answer()
    {
        Assert.Equal(DnsBench.Reply.Resolved, DnsBench.Read(Reply(0x80, 1, A), Q));
        Assert.Equal(DnsBench.Reply.Resolved, DnsBench.Read(Reply(0x80, 1, Cname), Q));
        Assert.Equal(DnsBench.Reply.Wrong, DnsBench.Read(Reply(0x83, 0), Q));   // "no such name" for a name that exists
        Assert.Equal(DnsBench.Reply.Wrong, DnsBench.Read(Reply(0x80, 0), Q));   // no error, but nothing in it
        Assert.Equal(DnsBench.Reply.Wrong, DnsBench.Read(Reply(0x82, 0), Q));   // server failure
        Assert.Equal(DnsBench.Reply.Wrong, DnsBench.Read([.. Reply(0x80, 1, A)[..^6]], Q));   // cut off mid-record
        var tc = Reply(0x80, 1, A); tc[2] |= 0x02; Assert.Equal(DnsBench.Reply.Wrong, DnsBench.Read(tc, Q));   // truncated
    }
    [Fact] public void A_packet_for_another_query_is_ignored()
    {
        var other = Reply(0x80, 1, A); other[1] = 0x35; Assert.Equal(DnsBench.Reply.NotOurs, DnsBench.Read(other, Q));   // another id
        var question = Reply(0x80, 1, A); question[13] = (byte)'x'; Assert.Equal(DnsBench.Reply.NotOurs, DnsBench.Read(question, Q));   // same id, another name
        var upper = Reply(0x80, 1, A); upper[13] = (byte)'G'; Assert.Equal(DnsBench.Reply.Resolved, DnsBench.Read(upper, Q));   // case may change
        Assert.Equal(DnsBench.Reply.NotOurs, DnsBench.Read(Q, Q));   // a query, not a response
    }
    [Fact] public void The_fastest_that_answered_everything_wins()
    {
        var best = DnsBench.Best([new("fast-but-flaky", "1", 11, 12, 10), new("steady", "2", 12, 12, 40), new("slow", "3", 12, 12, 90), new("dead", "4", 0, 12, null)]);
        Assert.Equal("steady", best!.Provider);
        Assert.Null(DnsBench.Best([new("dead", "4", 0, 12, null)]));
        Assert.Equal(2.5, DnsBench.Median([4, 1, 3, 2]));
    }
}
