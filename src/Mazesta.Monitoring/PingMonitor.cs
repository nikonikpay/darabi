using System.Net; using System.Net.NetworkInformation; using System.Net.Sockets;
using Mazesta.Core.Overlay;
namespace Mazesta.Monitoring;

/// <summary>Where the overlay gets the ping, the packet loss and the jitter from.</summary>
public interface IPingSource
{
    /// <summary>Starts sending echoes (idempotent).</summary>
    void Start();
    void Stop();
    /// <summary>The link over the last half minute; its figures are null until measured.</summary>
    PingReading Read();
    string Target { get; }
}

/// <summary>
/// One ICMP echo a second to one address, kept for the last thirty: what a game's own network graph shows. It runs only between
/// <see cref="Start"/> and <see cref="Stop"/> (the overlay on screen with a ping item on it), so a hidden overlay sends nothing. An echo with
/// no reply within a second counts as lost; a target that is not an address is resolved once per start.
/// </summary>
public sealed class PingMonitor(Func<string> target) : IPingSource, IDisposable
{
    public const string DefaultTarget = "8.8.8.8";
    private readonly PingWindow _window = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public string Target => target() is { Length: > 0 } t ? t.Trim() : DefaultTarget;

    public void Start()
    {
        lock (_lock)
        {
            if (_cts is not null) return;
            _window.Clear(); _cts = new();
            _ = Task.Run(() => LoopAsync(Target, _cts.Token));
        }
    }

    public void Stop() { lock (_lock) { _cts?.Cancel(); _cts?.Dispose(); _cts = null; } }
    public PingReading Read() { lock (_lock) return _window.Read(); }

    private async Task LoopAsync(string name, CancellationToken ct)
    {
        try
        {
            IPAddress? address = null;
            using var ping = new Ping();
            while (!ct.IsCancellationRequested)
            {
                double? ms = null;
                try
                {
                    address ??= IPAddress.TryParse(name, out var literal) ? literal : (await Dns.GetHostAddressesAsync(name, ct).ConfigureAwait(false)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (address is not null && await ping.SendPingAsync(address, TimeSpan.FromSeconds(1), cancellationToken: ct).ConfigureAwait(false) is { Status: IPStatus.Success } reply) ms = reply.RoundtripTime;
                }
                catch (Exception e) when (e is PingException or SocketException or InvalidOperationException) { }   // no route, no name: the echo is lost
                lock (_lock) if (!ct.IsCancellationRequested) _window.Add(ms);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => Stop();
}
