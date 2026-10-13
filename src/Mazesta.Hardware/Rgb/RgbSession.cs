using System.Diagnostics; using Mazesta.Core.Rgb; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Rgb;

public enum RgbConnect { Connected, NotConnected, NotFound, NoServer }

/// <summary>How a look reached a device: by its own mode (Native), driven by the app frame by frame (Simulated), as a still colour or gradient (Static), or not at all (Skipped).</summary>
public enum RgbHow { Skipped, Native, Simulated, Static }

/// <summary>
/// The lights as the app and the tray use them: the OpenRGB server (found beside the app, started hidden when nothing listens yet), the client of its SDK, and
/// the makers' lighting programs that are kept stopped while the lights are ours. The scene the user set (<see cref="RgbScene"/>) is put back by
/// <see cref="ApplyAsync"/>; a header whose strip length nobody has given gets <see cref="RgbScene.DefaultLeds"/> LEDs, so nothing ever has to be asked.
/// </summary>
public sealed class RgbSession : IDisposable
{
    private readonly string _appFolder, _configDir; private readonly ILogger _log;
    private Process? _server;

    public RgbSession(string appFolder, string configDir, ILogger log) { _appFolder = appFolder; _configDir = configDir; _log = log; Client = new(); Runner = new(Client, log); }

    /// <summary>The port the server is on (after a connect).</summary>
    public int Port { get; private set; } = OpenRgbClient.DefaultPort;
    public OpenRgbClient Client { get; }
    /// <summary>Runs the effects the devices have not themselves (see <see cref="RgbEffects"/>).</summary>
    public RgbEffectRunner Runner { get; }
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


    /// <summary>Gives every device the look the scene has for it: its own, else the one for all. A device that refuses (no mode takes a colour) or has not the effect is left as it is.
    /// The effects some devices only get from the app run from here on, until the next call.</summary>
    public async Task ApplyAsync(RgbScene scene, CancellationToken ct)
    {
        if (!Client.Connected) return;
        Runner.Clear();
        foreach (var d in Client.Devices.ToList())
        {
            var look = scene.Dark ? new RgbLook(Off: true) : scene.Devices.TryGetValue(RgbScene.DeviceKey(d.Vendor, d.Name, d.Location), out var own) ? own : scene.All;
            if (look is null) continue;
            try { await ApplyLookAsync(d.Index, look, ct).ConfigureAwait(false); }
            catch (NotSupportedException e) { _log.LogInformation("{Device}: {Message}", d.Name, e.Message); }
        }
        await Client.RefreshAsync(ct).ConfigureAwait(false);
        Runner.Start();
    }

    /// <summary>One look on one device, and how it was done. An effect by one of the universal names (<see cref="RgbEffects"/>) is the device's own when it has it, else
    /// run by the app when the device takes a colour for each LED; an effect by a device's own name is sent as is. <see cref="RgbHow.Skipped"/>: nothing was sent.</summary>
    public async Task<RgbHow> ApplyLookAsync(int device, RgbLook look, CancellationToken ct)
    {
        Runner.Remove(device);
        var d = Client.Device(device) ?? throw new InvalidOperationException("That device is not in the list; refresh it.");
        var color = RgbColor.Parse(look.Color);
        var palette = RgbPalettes.Find(look.Palette) is { } p ? p.Colors.Select(RgbColor.Parse).OfType<RgbColor>().ToList() : null;
        if (look.Off) { await Client.SetColorAsync(device, null, ct).ConfigureAwait(false); return RgbHow.Static; }
        string? name = look.Mode is { Length: > 0 } m ? m : palette is not null && look.Leds is null ? RgbEffects.Static : null;
        if (name is not null)
        {
            if (RgbEffects.Universal(name) is not { } fx)
            {
                int own = Client.ModeIndex(device, name);   // a device's own effect, by its name
                if (own < 0) return RgbHow.Skipped;
                await Client.SetModeAsync(device, own, color, ct, look.Speed, look.Brightness).ConfigureAwait(false); return RgbHow.Native;
            }
            string? native = fx == RgbEffects.Static ? null : RgbEffects.NativeName(d.Modes.Select(x => x.Name), fx);
            bool palettePlays = palette is { Count: > 1 } && fx is RgbEffects.Rainbow or RgbEffects.Cycle;   // the device's own rainbow knows only its own colours
            var tint = color ?? palette?.FirstOrDefault() ?? new RgbColor(255, 255, 255);
            if (native is not null && !(palettePlays && Client.HasPerLed(device)))
            {
                await Client.SetModeAsync(device, Client.ModeIndex(device, native), color ?? palette?.FirstOrDefault(), ct, look.Speed, look.Brightness).ConfigureAwait(false); return RgbHow.Native;
            }
            if (fx == RgbEffects.Static)
            {
                if (palette is { Count: > 1 } && Client.HasPerLed(device)) { await Client.SetLedsAsync(device, RgbFrames.Gradient(palette, d.LedCount), ct).ConfigureAwait(false); return RgbHow.Static; }
                if (color is null && palette is null) return RgbHow.Skipped;
                await Client.SetColorAsync(device, tint, ct).ConfigureAwait(false); return RgbHow.Static;
            }
            if (!Client.HasPerLed(device)) return RgbHow.Skipped;
            Runner.Set(new RgbJob(device, fx, tint, palette, look.Speed ?? 50, look.Brightness ?? 100, d.LedCount)); Runner.Start(); return RgbHow.Simulated;
        }
        if (look.Leds is { Count: > 0 } leds) { await Client.SetLedsAsync(device, [.. leds.Select(h => RgbColor.Parse(h) ?? new RgbColor(0, 0, 0))], ct).ConfigureAwait(false); return RgbHow.Static; }
        if (color is null) return RgbHow.Skipped;
        await Client.SetColorAsync(device, color, ct).ConfigureAwait(false); return RgbHow.Static;
    }

    /// <summary>What the devices are doing now, written down so <see cref="RestoreAsync"/> can put it back (the mode with its own speed and colours, and each LED's colour).</summary>
    public RgbSnapshot Capture()
    {
        var snap = new RgbSnapshot();
        foreach (var d in Client.Devices)
        {
            if (d.ActiveMode < 0 || d.ActiveMode >= d.Modes.Count) continue;
            var m = d.Modes[d.ActiveMode];
            snap.Devices[RgbScene.DeviceKey(d.Vendor, d.Name, d.Location)] = new RgbSnapshotDevice
            {
                Mode = m.Name, Speed = m.Speed, Brightness = m.Brightness, Direction = m.Direction, ModeColors = [.. m.Colors.Select(c => c.Hex)], Leds = [.. d.Colors.Take(512).Select(c => c.Hex)],
            };
        }
        return snap;
    }

    /// <summary>Puts the devices back as <see cref="Capture"/> found them. Returns how many were put back.</summary>
    public async Task<int> RestoreAsync(RgbSnapshot snap, CancellationToken ct)
    {
        if (!Client.Connected) return 0;
        Runner.Clear(); int done = 0;
        foreach (var d in Client.Devices.ToList())
        {
            if (!snap.Devices.TryGetValue(RgbScene.DeviceKey(d.Vendor, d.Name, d.Location), out var was)) continue;
            var mode = d.Modes.FirstOrDefault(m => m.Name == was.Mode); if (mode is null) continue;
            var back = mode with { Speed = was.Speed, Brightness = was.Brightness, Direction = was.Direction, Colors = [.. was.ModeColors.Select(RgbColor.Parse).OfType<RgbColor>()] };
            try { await Client.RestoreModeAsync(d.Index, back, [.. was.Leds.Select(RgbColor.Parse).OfType<RgbColor>()], ct).ConfigureAwait(false); done++; }
            catch (Exception e) when (e is NotSupportedException or ArgumentOutOfRangeException or InvalidOperationException) { _log.LogInformation("{Device}: not put back: {Message}", d.Name, e.Message); }
        }
        await Client.RefreshAsync(ct).ConfigureAwait(false);
        return done;
    }

    /// <summary>After sleep the lights' devices are not what the server found at its start (a controller that lost its power has a new handle, an old one writes into nothing): the
    /// connection is dropped, the bundled server this app starts is ended and started again, and the devices are found afresh. A server somebody else runs (their own
    /// OpenRGB) is not touched: it is only connected to again.</summary>
    public async Task<RgbConnect> RecoverAsync(RgbScene scene, CancellationToken ct)
    {
        Runner.Stop(); Client.Disconnect();
        await Task.Run(StopBundledServer, ct).ConfigureAwait(false);
        try { _server?.Dispose(); } catch (InvalidOperationException) { }
        _server = null; await Task.Delay(500, ct).ConfigureAwait(false);
        return await ConnectAsync(true, scene.StopMakers, scene, ct).ConfigureAwait(false);
    }

    /// <summary>Ends the OpenRGB that sits in this app's own folder (the one started for the lights, by this process or another of the app's); the user's own install is left alone.</summary>
    private void StopBundledServer()
    {
        string own = Path.GetFullPath(_appFolder);
        foreach (var p in Process.GetProcessesByName("OpenRGB"))
        {
            try
            {
                string? file = p.MainModule?.FileName;
                if (file is not null && file.StartsWith(own, StringComparison.OrdinalIgnoreCase)) { p.Kill(true); p.WaitForExit(3000); }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Lets go of the lights: the client is closed and, unless <paramref name="leaveRunning"/>, the server this object started is ended and the makers' programs it
    /// stopped are started again. Left running, the server and the stopped programs stay as they are (the tray keeps the scene up after the window has closed).</summary>
    public void Release(bool leaveRunning = false)
    {
        Runner.Stop(); Runner.Clear(); Client.Disconnect();
        if (leaveRunning) { _server?.Dispose(); _server = null; return; }
        try { if (_server is { HasExited: false }) { _server.Kill(true); _server.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _server?.Dispose(); _server = null; Conflicts.RestoreAll();
    }

    public void Dispose() { Release(); Runner.Dispose(); Client.Dispose(); }
}
