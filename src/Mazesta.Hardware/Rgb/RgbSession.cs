using System.Diagnostics; using Mazesta.Core.Rgb; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Rgb;

public enum RgbConnect { Connected, NotConnected, NotFound, NoServer }

/// <summary>
/// The lights as the app and the tray use them: the OpenRGB server (found beside the app, started hidden when nothing listens yet), the client of its SDK, and
/// the makers' lighting programs that are kept stopped while the lights are ours. The scene the user set (<see cref="RgbScene"/>) is put back by
/// <see cref="ApplyAsync"/>; a header whose strip length nobody has given gets <see cref="RgbScene.DefaultLeds"/> LEDs, so nothing ever has to be asked.
/// </summary>
public sealed class RgbSession : IDisposable
{
    private readonly string _appFolder, _configDir; private readonly ILogger _log;
    private Process? _server;

    public RgbSession(string appFolder, string configDir, ILogger log) { _appFolder = appFolder; _configDir = configDir; _log = log; }

    /// <summary>The port the server is on (after a connect).</summary>
    public int Port { get; private set; } = OpenRgbClient.DefaultPort;
    public OpenRgbClient Client { get; } = new();
    public RgbConflicts Conflicts { get; } = new();
    public string? Exe => OpenRgbHost.Find(_appFolder);
    /// <summary>True after a connect that gave a header its default length: the scene has changed and is to be saved.</summary>
    public bool ZonesDefaulted { get; private set; }

    /// <summary>Connects to the server, starting it first when <paramref name="start"/> (and stopping the makers' programs when <paramref name="stopMakers"/>), waits until
    /// its device scan has settled and gives the headers their lengths (the scene's, else the default).</summary>
    public async Task<RgbConnect> ConnectAsync(bool start, bool stopMakers, RgbScene scene, CancellationToken ct)
    {
        bool fresh = false; ZonesDefaulted = false;
        if (!Client.Connected)
        {
            if (await OpenRgbHost.FindListeningAsync(ct).ConfigureAwait(false) is { } found) Port = found;
            else
            {
                if (!start) return RgbConnect.NotConnected;
                if (Exe is not { } exe) return RgbConnect.NotFound;
                if (stopMakers)
                {
                    var stopped = await Task.Run(Conflicts.StopAll, ct).ConfigureAwait(false);
                    if (stopped.Count > 0) _log.LogInformation("Stopped for the lights: {Makers}", string.Join(", ", stopped));
                }
                foreach (int port in OpenRgbHost.Ports.Where(OpenRgbHost.CanBind).Take(3))
                {
                    _server = await OpenRgbHost.StartAsync(exe, port, _configDir, ct).ConfigureAwait(false);
                    if (_server is null) { _log.LogWarning("The lights' server did not come up on port {Port}", port); continue; }
                    Port = port; fresh = true; break;
                }
                if (_server is null) { Conflicts.RestoreAll(); return RgbConnect.NoServer; }
            }
            await Client.ConnectAsync(Port, ct).ConfigureAwait(false);
        }
        await WaitForDevicesAsync(fresh, ct).ConfigureAwait(false);
        if (start) await SizeZonesAsync(scene, ct).ConfigureAwait(false);
        _log.LogInformation("OpenRGB devices: {Devices}", string.Join(" | ", Client.Devices.Select(d => $"{d.Kind} {d.Vendor} {d.Name} [{string.Join(", ", d.Zones.Select(z => $"{z.Name}:{z.LedsCount}/{z.LedsMax}"))}]")));
        return RgbConnect.Connected;
    }

    /// <summary>The server listens before its first scan has finished: the devices are read again until their number has stayed the same for two reads.</summary>
    private async Task WaitForDevicesAsync(bool fresh, CancellationToken ct)
    {
        if (fresh) await Task.Delay(2500, ct).ConfigureAwait(false);
        int last = -1;
        for (int i = 0; i < (fresh ? 24 : 4); i++)
        {
            await Client.RefreshAsync(ct).ConfigureAwait(false);
            if (Client.Devices.Count > 0 && Client.Devices.Count == last) return;
            last = Client.Devices.Count;
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
    }

    private async Task SizeZonesAsync(RgbScene scene, CancellationToken ct)
    {
        bool resized = false;
        foreach (var d in Client.Devices.ToList())
            foreach (var z in d.Zones.Where(z => z.Resizable))
            {
                string key = RgbScene.ZoneKey(d.Vendor, d.Name, d.Location, z.Name);
                int want;
                if (scene.Zones.TryGetValue(key, out int saved)) want = saved;
                else if (z.LedsCount == 0) { want = (int)Math.Min(RgbScene.DefaultLeds, z.LedsMax); scene.Zones[key] = want; ZonesDefaulted = true; }
                else continue;
                if (want == z.LedsCount || want < z.LedsMin || want > z.LedsMax) continue;
                try { await Client.ResizeZoneAsync(d.Index, z.Index, want, ct).ConfigureAwait(false); resized = true; }
                catch (Exception e) when (e is NotSupportedException or ArgumentOutOfRangeException) { _log.LogInformation("{Device}/{Zone} not resized: {Message}", d.Name, z.Name, e.Message); }
            }
        if (resized) await Client.RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Gives every device the look the scene has for it: its own, else the one for all. A device that refuses (no mode takes a colour) or has not the effect is left as it is.</summary>
    public async Task ApplyAsync(RgbScene scene, CancellationToken ct)
    {
        if (!Client.Connected) return;
        foreach (var d in Client.Devices.ToList())
        {
            var look = scene.Dark ? new RgbLook(Off: true) : scene.Devices.TryGetValue(RgbScene.DeviceKey(d.Vendor, d.Name, d.Location), out var own) ? own : scene.All;
            if (look is null) continue;
            try { await ApplyLookAsync(d.Index, look, ct).ConfigureAwait(false); }
            catch (NotSupportedException e) { _log.LogInformation("{Device}: {Message}", d.Name, e.Message); }
        }
        await Client.RefreshAsync(ct).ConfigureAwait(false);
    }

    /// <summary>One look on one device. False when the device has not the effect asked for (nothing was sent).</summary>
    public async Task<bool> ApplyLookAsync(int device, RgbLook look, CancellationToken ct)
    {
        var color = RgbColor.Parse(look.Color);
        if (look.Off) { await Client.SetColorAsync(device, null, ct).ConfigureAwait(false); return true; }
        if (look.Mode is { Length: > 0 } name)
        {
            int m = Client.ModeIndex(device, name);
            if (m < 0) return false;
            await Client.SetModeAsync(device, m, color, ct, look.Speed, look.Brightness).ConfigureAwait(false); return true;
        }
        if (look.Leds is { Count: > 0 } leds) { await Client.SetLedsAsync(device, [.. leds.Select(h => RgbColor.Parse(h) ?? new RgbColor(0, 0, 0))], ct).ConfigureAwait(false); return true; }
        if (color is null) return false;
        await Client.SetColorAsync(device, color, ct).ConfigureAwait(false); return true;
    }

    /// <summary>Lets go of the lights: the client is closed and, unless <paramref name="leaveRunning"/>, the server this object started is ended and the makers' programs it
    /// stopped are started again. Left running, the server and the stopped programs stay as they are (the tray keeps the scene up after the window has closed).</summary>
    public void Release(bool leaveRunning = false)
    {
        Client.Disconnect();
        if (leaveRunning) { _server?.Dispose(); _server = null; return; }
        try { if (_server is { HasExited: false }) { _server.Kill(true); _server.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _server?.Dispose(); _server = null; Conflicts.RestoreAll();
    }

    public void Dispose() { Release(); Client.Dispose(); }
}
