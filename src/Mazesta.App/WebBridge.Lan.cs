using System.Net.NetworkInformation; using System.Net.Sockets;
using Mazesta.Diagnostics.Network; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>The LAN partner: listening only while the technician keeps it on (it is off at every start), so no port is open otherwise.</summary>
    private void RegisterLan()
    {
        LanPeerServer? server = null; string? error = null;
        _cleanup.Add(() => { var s = server; server = null; _ = s?.DisposeAsync(); });
        // The addresses the other computer can use: the IPv4 addresses of the adapters that are up, loopback and link-local left out.
        static IEnumerable<string> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => (n.Name, a.Address)))
            .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(x => $"{x.Address}  ({x.Name})");
        object State() => new { listening = server is not null, port = LanPeerServer.Port, addresses = Addresses(), sessions = server?.Sessions ?? 0, error };
        Method("lan.state", _ => State());
        MethodAsync("lan.set", async p =>
        {
            error = null;
            if (Bool(p, "on") && server is null)
            {
                try { server = new LanPeerServer(); _log.LogInformation("LAN partner listening on port {Port}", LanPeerServer.Port); }
                catch (SocketException e) { error = e.Message; }
            }
            else if (!Bool(p, "on") && server is { } s) { server = null; await s.DisposeAsync(); }
            return State();
        });
    }
}
