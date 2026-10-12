using System.Net.NetworkInformation; using System.Net.Sockets;
namespace Mazesta.Diagnostics.Network;

/// <summary>The adapters that are connected now, and of them the one the internet goes out through, by the names Windows (and so the sensors) give them.</summary>
public sealed record ConnectedAdapters(string? Internet, IReadOnlyList<string> Up);

/// <summary>The network device a test went out through: its connection's name, the device's own model (the adapter description Windows has from the driver, "Realtek PCIe 2.5GbE Family Controller"),
/// the link speed it negotiated in Mbps (null when Windows does not know it) and whether it is a cable or Wi-Fi.</summary>
public sealed record AdapterInfo(string Name, string Model, long? LinkMbps, string Kind);

/// <summary>
/// Which adapter carries this computer's internet. A machine lists many (a cable port with nothing in it, Hyper-V switches, VPN and Tailscale tunnels),
/// and the first one is often not connected at all. The internet adapter is a connected one with a default gateway; a real card (cable or Wi-Fi) is
/// preferred to a tunnel, since a VPN's tunnel itself goes out through that card; among several, the one that moved the most data.
/// </summary>
public static class InternetAdapter
{
    private static readonly string[] Virtual = ["virtual", "hyper-v", "vethernet", "tap-", "wintun", "wireguard", "tailscale", "vpn", "tun", "loopback", "pseudo", "zerotier", "vmware", "virtualbox"];

    private static List<(NetworkInterface n, bool gateway, bool real, long bytes)> Rank()
    {
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return []; }
        var up = all.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback) && !Core.Hardware.NetworkAdapterFilter.IsVirtualBinding(n.Name)).ToList();
        return [.. up.Select(n => (n, gateway: HasGateway(n), real: IsReal(n), bytes: Bytes(n)))
            .OrderByDescending(x => x.gateway).ThenByDescending(x => x.real).ThenByDescending(x => x.bytes)];
    }

    public static ConnectedAdapters Find()
    {
        var ranked = Rank();
        string? internet = ranked.FirstOrDefault(x => x.gateway).n?.Name;
        return new(internet, [.. ranked.Select(x => x.n.Name)]);
    }

    /// <summary>The device the internet goes out through, or null when no connected adapter has a gateway.</summary>
    public static AdapterInfo? Primary()
    {
        var n = Rank().FirstOrDefault(x => x.gateway).n;
        if (n is null) return null;
        long speed = 0; try { speed = n.Speed; } catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException) { }
        string kind = n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : n.NetworkInterfaceType is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 or NetworkInterfaceType.Wman ? "Mobile" : "Ethernet";
        return new(n.Name, n.Description, speed > 0 ? speed / 1_000_000 : null, kind);
    }

    internal static bool IsReal(NetworkInterface n) =>
        n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.Wman or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2
        && !Virtual.Any(v => n.Name.Contains(v, StringComparison.OrdinalIgnoreCase) || n.Description.Contains(v, StringComparison.OrdinalIgnoreCase));

    private static bool HasGateway(NetworkInterface n)
    {
        try { return n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 && !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)); }
        catch (NetworkInformationException) { return false; }
    }

    private static long Bytes(NetworkInterface n)
    {
        try { var s = n.GetIPStatistics(); return s.BytesReceived + s.BytesSent; }
        catch (Exception e) when (e is NetworkInformationException or PlatformNotSupportedException) { return 0; }
    }
}
