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
/// any lookup is not chosen, however fast its other answers were; among those that answered all, the lowest median wins. Nothing is changed
/// here: the page applies the winner only when the user asks.
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

    /// <summary>Whether a packet is the answer to that query: the same id, the response bit, and no server failure or refusal (a name that does
    /// not exist is still an answer).</summary>
    public static bool IsAnswer(ReadOnlySpan<byte> packet, ushort id)
    {
        if (packet.Length < 12 || packet[0] != (byte)(id >> 8) || packet[1] != (byte)id || (packet[2] & 0x80) == 0) return false;
        int rcode = packet[3] & 0x0F;
        return rcode is 0 or 3;
    }

    /// <summary>One lookup: the time to a valid answer, or null on a timeout or anything else.</summary>
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
                if (IsAnswer(r.Buffer, id)) return watch.Elapsed.TotalMilliseconds;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (SocketException) { return null; }
    }

    /// <summary>Times every resolver at once (each one's lookups one after another, so they do not queue behind each other on one server).</summary>
    public static async Task<IReadOnlyList<DnsScore>> RunAsync(IEnumerable<(string Provider, string Server)> resolvers, CancellationToken ct)
    {
        var tasks = resolvers.Select(async r =>
        {
            if (!IPAddress.TryParse(r.Server, out var ip)) return new DnsScore(r.Provider, r.Server, 0, 0, null);
            var times = new List<double>(); int asked = 0;
            for (int round = 0; round < Rounds; round++)
                foreach (var name in Names)
                {
                    asked++;
                    if (await TimeAsync(ip, name, ct).ConfigureAwait(false) is { } ms) times.Add(ms);
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
