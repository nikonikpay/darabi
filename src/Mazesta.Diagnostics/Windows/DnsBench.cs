using System.Diagnostics; using System.Net; using System.Net.Sockets;
namespace Mazesta.Diagnostics.Windows;

/// <summary>One resolver's result: how many of the lookups it answered, and the median time of those answers (null when it answered none).</summary>
public sealed record DnsScore(string Provider, string Server, int Answered, int Asked, double? MedianMs)
{
    public bool Reliable => Asked > 0 && Answered == Asked;
}

/// <summary>
/// Times the DNS resolvers on this connection, as DNS Jumper does: the same names are looked up from each resolver's first server, straight over
/// UDP (port 53, an A query of our own, so Windows' cache is not in the way), and the time to a valid answer is measured. A resolver that misses
/// any lookup, or answers one wrongly (no address for a name that exists), is not chosen, however fast its other answers were; among those that
/// answered all, the lowest median wins. It is the fastest in this short sample on this connection now, not a measure of a resolver's general
/// reliability. Nothing is changed here: the page applies the winner only when the user asks.
/// </summary>
public static class DnsBench
{
    /// <summary>Names most people look up: international ones and Iranian ones, since a resolver can be quick for one and slow for the other.</summary>
    public static readonly string[] Names = ["google.com", "microsoft.com", "github.com", "wikipedia.org", "digikala.com", "aparat.com"];
    public const int Rounds = 2;
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>The query for one name: a fixed header (recursion wanted, one question) and the name as length-prefixed labels, type A, class IN.</summary>
    public static byte[] Query(string name, ushort id)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.TrimEnd('.').Split('.')) { bytes.Add((byte)label.Length); bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        bytes.AddRange([0, 0, 1, 0, 1]);
        return [.. bytes];
    }

    /// <summary>What a packet says to a query of ours.</summary>
    public enum Reply { NotOurs, Resolved, Wrong }

    /// <summary>Reads a packet against the query it may answer. Not ours: another id, not a response, or a different question (it is ignored and
    /// the wait goes on). Resolved: no error, not cut short, and an address (A) or an alias (CNAME) for the name in the answer section; every
    /// name asked exists, so "no such name" or an empty answer is a wrong answer however quick (RFC 1035 §4.1). Wrong: anything else, a cut-short
    /// reply (TC) or a packet that does not parse.</summary>
    public static Reply Read(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> query)
    {
        if (packet.Length < 12 || packet[0] != query[0] || packet[1] != query[1] || (packet[2] & 0x80) == 0) return Reply.NotOurs;
        // The question: one, and byte for byte the one asked (name, type, class), but for letter case, which a resolver may change.
        int qEnd = query.Length;
        if (packet[4] != 0 || packet[5] != 1 || packet.Length < qEnd) return Reply.NotOurs;
        for (int i = 12; i < qEnd; i++) if (char.ToLowerInvariant((char)packet[i]) != char.ToLowerInvariant((char)query[i])) return Reply.NotOurs;
        if ((packet[2] & 0x02) != 0 || (packet[3] & 0x0F) != 0) return Reply.Wrong;   // truncated, or an error (3 = no such name)
        int answers = packet[6] << 8 | packet[7], at = qEnd;
        for (int k = 0; k < answers; k++)
        {
            if (Skip(packet, ref at) is false || at + 10 > packet.Length) return Reply.Wrong;
            int type = packet[at] << 8 | packet[at + 1], cls = packet[at + 2] << 8 | packet[at + 3], len = packet[at + 8] << 8 | packet[at + 9];
            at += 10;
            if (at + len > packet.Length) return Reply.Wrong;
            if (cls == 1 && (type == 1 && len == 4 || type == 5)) return Reply.Resolved;
            at += len;
        }
        return Reply.Wrong;
    }

    /// <summary>Steps over a name in a packet: labels up to a zero, or ending in a pointer to one elsewhere (compression).</summary>
    private static bool Skip(ReadOnlySpan<byte> p, ref int at)
    {
        while (at < p.Length)
        {
            int b = p[at];
            if (b == 0) { at++; return true; }
            if ((b & 0xC0) == 0xC0) { at += 2; return at <= p.Length; }
            if ((b & 0xC0) != 0) return false;
            at += b + 1;
        }
        return false;
    }

    /// <summary>One lookup: the time to a resolved answer, or null on a timeout, a wrong answer or anything else.</summary>
    public static async Task<double?> TimeAsync(IPAddress server, string name, CancellationToken ct)
    {
        using var udp = new UdpClient(server.AddressFamily);
        ushort id = (ushort)Random.Shared.Next(1, 65535);
        var query = Query(name, id);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(Timeout);
        try
        {
            udp.Connect(server, 53);
            var watch = Stopwatch.StartNew();
            await udp.SendAsync(query, limit.Token).ConfigureAwait(false);
            while (true)
            {
                var r = await udp.ReceiveAsync(limit.Token).ConfigureAwait(false);
                switch (Read(r.Buffer, query))
                {
                    case Reply.Resolved: return watch.Elapsed.TotalMilliseconds;
                    case Reply.Wrong: return null;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (SocketException) { return null; }
    }

    /// <summary>Times every resolver at once: a few seconds in all, however many do not answer.</summary>
    public static async Task<IReadOnlyList<DnsScore>> RunAsync(IEnumerable<(string Provider, string Server)> resolvers, CancellationToken ct)
    {
        var tasks = resolvers.Select(async r =>
        {
            if (!IPAddress.TryParse(r.Server, out var ip)) return new DnsScore(r.Provider, r.Server, 0, 0, null);
            // The names of one round at once (a resolver that does not answer costs one timeout a round, not one a name); the rounds one after
            // another, so the second finds what the first put in the resolver's cache, as browsing does.
            var times = new List<double>(); int asked = 0;
            for (int round = 0; round < Rounds; round++)
            {
                var got = await Task.WhenAll(Names.Select(name => TimeAsync(ip, name, ct))).ConfigureAwait(false);
                asked += got.Length; times.AddRange(got.Where(x => x is not null).Select(x => x!.Value));
            }
            return new DnsScore(r.Provider, r.Server, times.Count, asked, Median(times));
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static double? Median(IReadOnlyCollection<double> xs)
    {
        if (xs.Count == 0) return null;
        var s = xs.Order().ToArray(); int m = s.Length / 2;
        return s.Length % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2;
    }

    /// <summary>The resolver to use: among those that answered every lookup, the lowest median; null when none did.</summary>
    public static DnsScore? Best(IEnumerable<DnsScore> scores) => scores.Where(s => s.Reliable && s.MedianMs is not null).OrderBy(s => s.MedianMs).FirstOrDefault();
}
