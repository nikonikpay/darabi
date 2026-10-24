using System.Net; using System.Net.Http; using System.Net.Sockets; using System.Text.Json;
namespace Mazesta.Diagnostics.Ai;

/// <summary>
/// Name lookup for the AI downloads through Google's public DNS (8.8.8.8, asked over HTTPS, so no setting of Windows changes): on some lines the provider's own
/// DNS hands out a far or congested address of the file host's network and a multi-gigabyte download crawls, where Google's answer is a nearby one. If Google's DNS does not answer
/// in a few seconds (blocked, offline) the system's own lookup is used, so a download never fails for this. Used as the handler's <c>ConnectCallback</c>: the connection
/// is to the address found, while the TLS name stays the file host's.
/// </summary>
public static class GoogleDns
{
    private static readonly HttpClient Doh = new() { Timeout = TimeSpan.FromSeconds(4), DefaultRequestHeaders = { { "Accept", "application/dns-json" } } };
    private static readonly Dictionary<string, (IPAddress[] Addresses, DateTime Until)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint; Exception? last = null;
        foreach (var address in await ResolveAsync(endpoint.Host, ct).ConfigureAwait(false))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
            catch (SocketException e) { socket.Dispose(); last = e; }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException($"Could not connect to {endpoint.Host}.", last);
    }

    /// <summary>The addresses of a host: Google's answer (kept for its time to live), else the system's.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        lock (Gate) if (Cache.TryGetValue(host, out var hit) && hit.Until > DateTime.UtcNow) return hit.Addresses;
        try
        {
            using var response = await Doh.GetAsync($"https://8.8.8.8/resolve?name={Uri.EscapeDataString(host)}&type=A", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var (addresses, ttl) = Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (addresses.Length > 0) { lock (Gate) Cache[host] = (addresses, DateTime.UtcNow.AddSeconds(Math.Clamp(ttl, 30, 600))); return addresses; }
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or JsonException or IOException) && !ct.IsCancellationRequested) { }
        return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
    }

    /// <summary>The A records of a Google DNS JSON answer (type 1) and the shortest time to live among them.</summary>
    public static (IPAddress[] Addresses, int Ttl) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var list = new List<IPAddress>(); int ttl = 600;
        if (doc.RootElement.TryGetProperty("Answer", out var answer) && answer.ValueKind == JsonValueKind.Array)
            foreach (var a in answer.EnumerateArray())
                if (a.TryGetProperty("type", out var type) && type.GetInt32() == 1 && a.TryGetProperty("data", out var data) && IPAddress.TryParse(data.GetString(), out var ip))
                { list.Add(ip); if (a.TryGetProperty("TTL", out var t) && t.TryGetInt32(out int s)) ttl = Math.Min(ttl, s); }
        return ([.. list], ttl);
    }
}
