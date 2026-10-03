using System.Net; using System.Net.NetworkInformation; using System.Net.Sockets; using System.Runtime.InteropServices; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

public enum NetCheckResult { Ok, Failed, Skipped }

/// <summary>One step of the connection check. <see cref="Detail"/> is what was measured or read (an address, a time, a setting), in plain
/// technical words for the page and the log; it is never a verdict.</summary>
public sealed record NetCheck(string Id, NetCheckResult Result, string? Detail);

/// <summary>The user's proxy settings (Internet Options): a fixed proxy server, a set-up script, or neither.</summary>
public sealed record ProxySettings(bool Enabled, string? Server, string? AutoConfigUrl)
{
    /// <summary>Anything a "clear" would remove: a proxy switched on, or a server or script still written there.</summary>
    public bool HasData => Enabled || !string.IsNullOrWhiteSpace(Server) || !string.IsNullOrWhiteSpace(AutoConfigUrl);
}

/// <summary>Where the connection stops, read from the steps in order: the first that fails names the place.</summary>
public enum NetVerdict { Connected, NoAdapter, NoAddress, NoGateway, NoInternet, DnsFails, ProxyFails, WebBlocked }

/// <summary>
/// "My internet does not connect": the steps a technician walks through, and the usual repairs. The check only reads and probes - is an adapter
/// up with an address, does the router answer, does an address on the internet answer, do names resolve, does a web page come back, and what
/// the proxy settings hold. The repairs change Windows and each one is asked for by the user: clear the proxy settings, put the DNS back to
/// automatic or on a public resolver, empty the DNS cache, reset Winsock and the IP stack (which needs a restart).
/// </summary>
public static class NetRepair
{
    public const string AdapterStep = "adapter", GatewayStep = "gateway", InternetStep = "internet", DnsStep = "dns", WebStep = "web", ProxyStep = "proxy";
    public const string FixProxy = "proxy", FixDns = "dns", FixFlush = "flush", FixReset = "reset";
    private const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    /// <summary>Public resolvers that answer on TCP 53 and, mostly, to an echo: any one answering means the line to the internet is up.</summary>
    private static readonly string[] Probes = ["8.8.8.8", "1.1.1.1", "4.2.2.4"];
    private static readonly string[] Names = ["www.msftconnecttest.com", "www.google.com"];
    private const string TestUrl = "http://www.msftconnecttest.com/connecttest.txt", TestBody = "Microsoft Connect Test";

    public static ProxySettings ReadProxy(IRegistryAccess registry) => new(
        registry.Get(RegistryHive.CurrentUser, InternetSettings, "ProxyEnable") is int on && on != 0,
        registry.Get(RegistryHive.CurrentUser, InternetSettings, "ProxyServer") as string, registry.Get(RegistryHive.CurrentUser, InternetSettings, "AutoConfigURL") as string);

    /// <summary>Where the connection stops. The router not answering an echo is no fault while the internet beyond it answers (many routers
    /// ignore echoes); a proxy is blamed only when a page comes back without it and not through it.</summary>
    public static NetVerdict Diagnose(IReadOnlyList<NetCheck> checks)
    {
        NetCheck? Of(string id) => checks.FirstOrDefault(c => c.Id == id);
        bool Failed(string id) => Of(id)?.Result == NetCheckResult.Failed;
        if (Failed(AdapterStep)) return Of(AdapterStep)!.Detail?.Contains("169.254.", StringComparison.Ordinal) == true ? NetVerdict.NoAddress : NetVerdict.NoAdapter;
        if (Failed(InternetStep)) return Failed(GatewayStep) ? NetVerdict.NoGateway : NetVerdict.NoInternet;
        if (Failed(DnsStep)) return NetVerdict.DnsFails;
        if (Failed(WebStep)) return NetVerdict.WebBlocked;
        return Failed(ProxyStep) ? NetVerdict.ProxyFails : NetVerdict.Connected;
    }

    /// <summary>The repairs worth ticking for a verdict. The reset is never ticked for the user: it needs a restart and is the last resort.</summary>
    public static IReadOnlyList<string> Suggested(NetVerdict verdict, ProxySettings proxy)
    {
        var steps = new List<string>();
        if (proxy.HasData && verdict != NetVerdict.Connected) steps.Add(FixProxy);
        if (verdict is NetVerdict.DnsFails or NetVerdict.WebBlocked) steps.Add(FixDns);
        if (verdict is NetVerdict.DnsFails or NetVerdict.WebBlocked or NetVerdict.ProxyFails) steps.Add(FixFlush);
        return steps;
    }

    public static async Task<IReadOnlyList<NetCheck>> CheckAsync(IRegistryAccess registry, CancellationToken ct)
    {
        var checks = new List<NetCheck>();
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (n.Name, Ip: n.GetIPProperties())).Select(x => (x.Name, x.Ip,
                Addresses: x.Ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList(),
                Gateways: x.Ip.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Select(g => g.Address).ToList()))
            .Where(x => x.Addresses.Count > 0).ToList();
        var routed = adapters.Where(a => a.Gateways.Count > 0).ToList();
        if (routed.Count > 0) checks.Add(new(AdapterStep, NetCheckResult.Ok, string.Join("; ", routed.Select(a => $"{a.Name}: {string.Join(", ", a.Addresses)}"))));
        else
        {
            // 169.254.x.x is the address Windows gives itself when no DHCP server answered.
            var self = adapters.Where(a => a.Addresses.Any(x => x.StartsWith("169.254.", StringComparison.Ordinal))).ToList();
            checks.Add(new(AdapterStep, NetCheckResult.Failed, self.Count > 0 ? string.Join("; ", self.Select(a => $"{a.Name}: {string.Join(", ", a.Addresses)}"))
                : adapters.Count > 0 ? string.Join("; ", adapters.Select(a => $"{a.Name}: {string.Join(", ", a.Addresses)} (no gateway)")) : "no connected adapter"));
        }

        var gateway = routed.SelectMany(a => a.Gateways).FirstOrDefault();
        if (gateway is null) checks.Add(new(GatewayStep, NetCheckResult.Skipped, null));
        else checks.Add(await PingAsync(gateway) is { } ms ? new(GatewayStep, NetCheckResult.Ok, $"{gateway}: {ms} ms") : new(GatewayStep, NetCheckResult.Failed, $"{gateway}: no echo reply"));

        if (routed.Count == 0) checks.Add(new(InternetStep, NetCheckResult.Skipped, null));
        else
        {
            var reached = (await Task.WhenAll(Probes.Select(async p => (p, how: await ReachAsync(p, ct).ConfigureAwait(false)))).ConfigureAwait(false)).Where(x => x.how is not null).ToList();
            checks.Add(reached.Count > 0 ? new(InternetStep, NetCheckResult.Ok, string.Join("; ", reached.Select(x => $"{x.p}: {x.how}")))
                : new(InternetStep, NetCheckResult.Failed, $"{string.Join(", ", Probes)}: no echo reply and no TCP connection on port 53"));
        }
        bool online = checks[^1].Result == NetCheckResult.Ok;

        if (!online) { checks.Add(new(DnsStep, NetCheckResult.Skipped, null)); checks.Add(new(WebStep, NetCheckResult.Skipped, null)); }
        else
        {
            var resolved = (await Task.WhenAll(Names.Select(n => ResolveAsync(n, ct))).ConfigureAwait(false)).OfType<string>().ToList();
            string servers = string.Join(", ", routed.SelectMany(a => a.Ip.DnsAddresses).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).Distinct());
            checks.Add(resolved.Count > 0 ? new(DnsStep, NetCheckResult.Ok, $"{string.Join("; ", resolved)} (DNS {servers})") : new(DnsStep, NetCheckResult.Failed, $"{string.Join(", ", Names)}: not resolved (DNS {servers})"));
            if (resolved.Count == 0) checks.Add(new(WebStep, NetCheckResult.Skipped, null));
            else { var (ok, what) = await WebAsync(useProxy: false, ct).ConfigureAwait(false); checks.Add(new(WebStep, ok ? NetCheckResult.Ok : NetCheckResult.Failed, what)); }
        }

        var proxy = ReadProxy(registry);
        string said = string.Join("; ", new[] { proxy.Enabled ? "on" : "off", proxy.Server is { Length: > 0 } s ? $"server {s}" : null, proxy.AutoConfigUrl is { Length: > 0 } u ? $"script {u}" : null }.OfType<string>());
        if (!proxy.HasData) checks.Add(new(ProxyStep, NetCheckResult.Ok, "none set"));
        else if (checks.First(c => c.Id == WebStep).Result != NetCheckResult.Ok) checks.Add(new(ProxyStep, NetCheckResult.Skipped, said));   // nothing to compare the proxy with
        else { var (ok, what) = await WebAsync(useProxy: true, ct).ConfigureAwait(false); checks.Add(new(ProxyStep, ok ? NetCheckResult.Ok : NetCheckResult.Failed, $"{said}; through it: {what}")); }
        return checks;
    }

    private static async Task<long?> PingAsync(IPAddress address)
    {
        using var ping = new Ping();
        for (int i = 0; i < 2; i++)
            try { var r = await ping.SendPingAsync(address, 1500).ConfigureAwait(false); if (r.Status == IPStatus.Success) return r.RoundtripTime; }
            catch (Exception e) when (e is PingException or InvalidOperationException or SocketException) { }
        return null;
    }

    /// <summary>An echo, or failing that a TCP connection on the DNS port (networks that drop echoes still let that through).</summary>
    private static async Task<string?> ReachAsync(string address, CancellationToken ct)
    {
        var ip = IPAddress.Parse(address);
        if (await PingAsync(ip).ConfigureAwait(false) is { } ms) return $"echo {ms} ms";
        try
        {
            using var tcp = new TcpClient(); using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(2500);
            await tcp.ConnectAsync(ip, 53, limit.Token).ConfigureAwait(false);
            return "TCP 53 open";
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException) { ct.ThrowIfCancellationRequested(); return null; }
    }

    private static async Task<string?> ResolveAsync(string name, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(5000);
            var found = await Dns.GetHostAddressesAsync(name, limit.Token).ConfigureAwait(false);
            return found.Length > 0 ? $"{name} = {found[0]}" : null;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException) { ct.ThrowIfCancellationRequested(); return null; }
    }

    /// <summary>Windows' own connection test page: its body is fixed, so a sign-in page or a filter's page in its place shows.</summary>
    private static async Task<(bool Ok, string What)> WebAsync(bool useProxy, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = useProxy, AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            using var r = await http.GetAsync(TestUrl, ct).ConfigureAwait(false);
            string body = r.IsSuccessStatusCode ? (await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim() : "";
            return body == TestBody ? (true, $"{TestUrl}: HTTP {(int)r.StatusCode}") : (false, $"{TestUrl}: HTTP {(int)r.StatusCode}, not the expected page (a sign-in page or a filter answers instead)");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { ct.ThrowIfCancellationRequested(); return (false, $"{TestUrl}: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Switches the proxy off and removes the server and the script, for the user's programs (Internet Options) and for Windows' services
    /// (WinHTTP). Returns what was there, so it can be written down and put back.</summary>
    public static async Task<(ProxySettings Before, string? Error)> ClearProxyAsync(IRegistryAccess registry, ICommandRunner runner, CancellationToken ct)
    {
        var before = ReadProxy(registry);
        registry.Set(RegistryHive.CurrentUser, InternetSettings, "ProxyEnable", 0, RegistryValueKind.DWord);
        registry.Delete(RegistryHive.CurrentUser, InternetSettings, "ProxyServer");
        registry.Delete(RegistryHive.CurrentUser, InternetSettings, "AutoConfigURL");
        var r = await runner.RunAsync("netsh.exe", "winhttp reset proxy", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        return (before, r.ExitCode == 0 ? null : Last(r));
    }

    /// <summary>Tells the running programs that the proxy settings changed, so they do not keep the old ones until they restart.</summary>
    public static void AnnounceProxyChange()
    {
        const int SettingsChanged = 39, Refresh = 37;
        InternetSetOption(IntPtr.Zero, SettingsChanged, IntPtr.Zero, 0); InternetSetOption(IntPtr.Zero, Refresh, IntPtr.Zero, 0);
    }

    public static async Task<string?> FlushDnsAsync(ICommandRunner runner, CancellationToken ct)
    {
        var r = await runner.RunAsync("ipconfig.exe", "/flushdns", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        return r.ExitCode == 0 ? null : Last(r);
    }

    /// <summary>Winsock's catalog and the IP stack back to their defaults. Both take effect after a restart. "netsh int ip reset" ends with an
    /// error on many healthy systems (one registry key it may not write, even elevated) after resetting the rest: its lines are returned as they
    /// are, and only a failed Winsock reset counts as the step failing.</summary>
    public static async Task<(string? Error, IReadOnlyList<string> Output)> ResetStackAsync(ICommandRunner runner, CancellationToken ct)
    {
        var sock = await runner.RunAsync("netsh.exe", "winsock reset", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        var ip = await runner.RunAsync("netsh.exe", "int ip reset", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        return (sock.ExitCode == 0 ? null : Last(sock), [.. sock.Output, .. ip.Output]);
    }

    private static string Last(CommandResult r) => r.Output.Count > 0 ? r.Output[^1] : $"exit code {r.ExitCode}";

    [DllImport("wininet.dll", SetLastError = true)] private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);
}
