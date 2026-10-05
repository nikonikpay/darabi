using System.IO; using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Hardware.Rgb; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// Light colours of the memory, graphics card, board and what plugs into it, through OpenRGB (a separate program, run as its server; see
    /// <see cref="OpenRgbClient"/>). The page can only ask for a colour (as "#rrggbb", or none for off) and a mode by its number; the program's place is found here.
    /// </summary>
    private void RegisterRgb()
    {
        var client = new OpenRgbClient(); _cleanup.Add(client.Dispose);
        const int port = OpenRgbClient.DefaultPort; bool busy = false;
        string? Exe() => OpenRgbHost.Find(AppContext.BaseDirectory);

        object State(string? error = null) => new
        {
            found = Exe() is not null, connected = client.Connected, error,
            devices = client.Devices.Select(d => new
            {
                index = d.Index, kind = d.Kind.ToString(), name = d.Name, vendor = d.Vendor, location = d.Location, leds = d.LedCount, active = d.ActiveMode,
                color = d.Colors.Count > 0 ? d.Colors[0].Hex : null,
                modes = d.Modes.Select((m, i) => new { index = i, name = m.Name, color = m.PerLed || m.ModeColors }),
            }),
        };

        async Task<object?> Connect(bool start)
        {
            if (!client.Connected)
            {
                if (!await OpenRgbHost.ListeningAsync(port, CancellationToken.None).ConfigureAwait(true))
                {
                    if (!start) return State();
                    if (Exe() is not { } exe) return State(Loc.Get("Rgb_NotFound"));
                    if (!await OpenRgbHost.StartAsync(exe, port, CancellationToken.None).ConfigureAwait(true)) return State(Loc.Get("Rgb_NoServer"));
                    await Task.Delay(3000).ConfigureAwait(true);   // the port opens before the first device scan has finished
                }
                await client.ConnectAsync(port, CancellationToken.None).ConfigureAwait(true);
            }
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("OpenRGB devices: {Devices}", string.Join(" | ", client.Devices.Select(d => $"{d.Kind} {d.Vendor} {d.Name}")));
            return State();
        }

        async Task<object?> Guard(Func<Task<object?>> run)
        {
            if (busy) throw new InvalidOperationException(Loc.Get("Tweaks_Busy"));
            busy = true;
            try { return await run().ConfigureAwait(true); }
            catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or FormatException or OperationCanceledException)
            {
                _log.LogWarning(e, "OpenRGB call failed"); client.Disconnect(); return State(Loc.Format("Rgb_Failed", e.Message));
            }
            finally { busy = false; }
        }

        MethodAsync("rgb.state", _ => Guard(() => Connect(false)));
        MethodAsync("rgb.start", _ => Guard(() => Connect(true)));
        MethodAsync("rgb.set", p => Guard(async () =>
        {
            if (!client.Connected) return State(Loc.Get("Rgb_NotConnected"));
            int device = p.TryGetProperty("device", out var dv) && dv.TryGetInt32(out var di) ? di : -1;
            int? mode = p.TryGetProperty("mode", out var mv) && mv.TryGetInt32(out var mi) ? mi : null;
            string hex = Str(p, "color"); var color = RgbColor.Parse(hex);
            if (hex.Length > 0 && color is null) throw new ArgumentException("color");
            var targets = device < 0 ? client.Devices.Select(d => d.Index).ToList() : [device]; var failed = new List<string>();
            foreach (var i in targets)
            {
                try
                {
                    if (mode is { } m) await client.SetModeAsync(i, m, color, CancellationToken.None).ConfigureAwait(true);
                    else await client.SetColorAsync(i, color, CancellationToken.None).ConfigureAwait(true);
                }
                catch (NotSupportedException e) { failed.Add(e.Message); }
            }
            _log.LogInformation("RGB set: device {Device}, mode {Mode}, colour {Color}; refused: {Refused}", device, mode, hex.Length > 0 ? hex : "off", failed.Count);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            return State(failed.Count > 0 ? string.Join(" ", failed) : null);
        }));
    }
}
