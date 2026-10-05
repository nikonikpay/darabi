using System.Diagnostics; using System.IO; using System.Text.Json; using Mazesta.Core.Rgb; using Mazesta.Core.Tray; using Mazesta.Desktop.Localization; using Mazesta.Hardware.Rgb; using Mazesta.Persistence; using Microsoft.Extensions.Logging;
using Microsoft.Win32;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private static bool s_rgbAutoStarted;

    /// <summary>
    /// Light colours of the memory, graphics card, board and what plugs into it, through OpenRGB (a separate program, run hidden as its server; see
    /// <see cref="RgbSession"/>). The page can only ask for a colour (as "#rrggbb", or none for off), a mode by its name or number, a speed and brightness
    /// in percent, and the number of LEDs of a zone; the program's place is found here. What is set is kept as the <see cref="RgbScene"/>: the next start of
    /// the app (or of the tray) puts it back, with no search and no question. The makers' own lighting programs are stopped while the lights are ours and
    /// put back when the page lets go (the button, or the app closing without the tray to hold the scene).
    /// </summary>
    private void RegisterRgb()
    {
        var session = new RgbSession(AppContext.BaseDirectory, Path.Combine(_paths.DataRoot, "openrgb"), _log); var client = session.Client; var conflicts = session.Conflicts;
        var scene = RgbSceneStore.Read(_paths); int busy = 0, starting = 0;   // busy: a call is being served; starting: the server is being brought up (not the same: a state call is busy too)
        void Save() { try { RgbSceneStore.Write(_paths, scene); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the lights' scene"); } }
        static bool TrayRuns() { var p = Process.GetProcessesByName(OverlaySignals.TrayProcess); foreach (var x in p) x.Dispose(); return p.Length > 0; }
        _cleanup.Add(() => session.Release(leaveRunning: scene.Enabled && TrayRuns()));

        object State(string? error = null) => new
        {
            found = session.Exe is not null, connected = client.Connected, enabled = scene.Enabled, dark = scene.Dark, error,
            connecting = Volatile.Read(ref starting) != 0,   // also while the devices are still being scanned after the socket is up
            makers = conflicts.StoppedNow,   // the makers' programs stopped for now, to be put back by "let go"
            running = client.Connected ? Array.Empty<string>() : conflicts.Running(),
            modeNames = client.Devices.SelectMany(d => d.Modes.Select(m => m.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            devices = client.Devices.Select(d => new
            {
                index = d.Index, kind = d.Kind.ToString(), name = d.Name, vendor = d.Vendor, location = d.Location, leds = d.LedCount, active = d.ActiveMode,
                color = d.Colors.Count > 0 ? d.Colors[0].Hex : null, colors = d.Colors.Take(64).Select(c => c.Hex),
                zones = d.Zones.Select(z => new { index = z.Index, name = z.Name, leds = z.LedsCount, min = z.LedsMin, max = z.LedsMax, resizable = z.Resizable }),
                modes = d.Modes.Select((m, i) => new
                {
                    index = i, name = m.Name, color = m.PerLed || m.ModeColors, speed = m.Flags.HasFlag(RgbModeFlags.Speed), brightness = m.Flags.HasFlag(RgbModeFlags.Brightness),
                    speedNow = Percent(m.SpeedMin, m.SpeedMax, m.Speed), brightnessNow = Percent(m.BrightnessMin, m.BrightnessMax, m.Brightness),
                }),
            }),
        };
        static int Percent(uint min, uint max, uint v) => max <= min ? 100 : (int)Math.Round(100.0 * (Math.Clamp(v, min, max) - min) / (max - min));

        async Task<object?> Connect(bool start, bool stopMakers)
        {
            RgbConnect result;
            if (start) Volatile.Write(ref starting, 1);
            try
            {
                result = await session.ConnectAsync(start, stopMakers, scene, CancellationToken.None).ConfigureAwait(true);
                if (result == RgbConnect.Connected && start)
                {
                    if (session.ZonesDefaulted) Save();
                    await session.ApplyAsync(scene, CancellationToken.None).ConfigureAwait(true);   // what the user set before, back on
                }
            }
            finally { Volatile.Write(ref starting, 0); }   // before the state is read: it must not say "connecting" about its own call
            if (start && result != RgbConnect.Connected) _log.LogWarning("The lights were not connected: {Result}", result);
            if (result == RgbConnect.NotFound) return State(Loc.Get("Rgb_NotFound"));
            if (result == RgbConnect.NoServer) return State(Loc.Get("Rgb_NoServer"));
            return State();
        }

        async Task<object?> Guard(Func<Task<object?>> run, bool shareBusy = false)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return shareBusy ? State() : throw new InvalidOperationException(Loc.Get("Tweaks_Busy"));
            try { return await run().ConfigureAwait(true); }
            catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or FormatException or OperationCanceledException)
            {
                _log.LogWarning(e, "OpenRGB call failed"); client.Disconnect(); return State(Loc.Format("Rgb_Failed", e.Message));
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        // Once per run of the app: a scene the user left switched on is put back without the page being opened. The tray does the same at sign-in; the two
        // find each other's server by its port.
        if (scene.Enabled && !s_rgbAutoStarted)
        {
            s_rgbAutoStarted = true;
            _ = Task.Run(async () =>
            {
                if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
                try { await Connect(true, scene.StopMakers).ConfigureAwait(false); }
                catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or FormatException or OperationCanceledException) { _log.LogWarning(e, "The lights' scene was not put back"); client.Disconnect(); }
                finally { Volatile.Write(ref busy, 0); }
            });
        }
        // The lights forget their colours when the PC sleeps: the scene is put back when it wakes (a while after, once the devices are up again).
        PowerModeChangedEventHandler wake = (_, e) =>
        {
            if (e.Mode != PowerModes.Resume || !scene.Enabled) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(8000).ConfigureAwait(false);
                if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
                try { if (client.Connected) await session.ApplyAsync(scene, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or InvalidDataException or OperationCanceledException) { client.Disconnect(); }
                finally { Volatile.Write(ref busy, 0); }
            });
        };
        SystemEvents.PowerModeChanged += wake; _cleanup.Add(() => SystemEvents.PowerModeChanged -= wake);

        MethodAsync("rgb.state", _ => Guard(() => Connect(false, false), shareBusy: true));
        MethodAsync("rgb.start", p => Guard(() =>
        {
            bool keep = p.TryGetProperty("keepMakers", out var k) && k.ValueKind == JsonValueKind.True;
            scene.Enabled = true; scene.StopMakers = !keep; Save();
            return Connect(true, !keep);
        }));
        Method("rgb.release", _ => { scene.Enabled = false; Save(); session.Release(); return State(); });
        MethodAsync("rgb.set", p => Guard(async () =>
        {
            if (!client.Connected) return State(Loc.Get("Rgb_NotConnected"));
            int device = p.TryGetProperty("device", out var dv) && dv.TryGetInt32(out var di) ? di : -1;
            int? mode = p.TryGetProperty("mode", out var mv) && mv.TryGetInt32(out var mi) ? mi : null;
            string modeName = Str(p, "modeName");
            int? speed = p.TryGetProperty("speed", out var sv) && sv.TryGetInt32(out var si) ? si : null, bright = p.TryGetProperty("brightness", out var bv) && bv.TryGetInt32(out var bi) ? bi : null;
            string hex = Str(p, "color"); var color = RgbColor.Parse(hex);
            if (hex.Length > 0 && color is null) throw new ArgumentException("color");
            var targets = device < 0 ? client.Devices.ToList() : client.Devices.Where(d => d.Index == device).ToList(); var failed = new List<string>(); int skipped = 0;
            foreach (var d in targets)
            {
                try
                {
                    // For everything at once the effect is named (its number differs by device); a device without it is left as it is.
                    string name = modeName.Length > 0 ? modeName : mode is { } index && index >= 0 && index < d.Modes.Count ? d.Modes[index].Name : "";
                    var look = new RgbLook(hex.Length > 0 ? hex : null, name.Length > 0 ? name : null, speed, bright, Off: hex.Length == 0 && name.Length == 0);
                    if (!await session.ApplyLookAsync(d.Index, look, CancellationToken.None).ConfigureAwait(true)) { skipped++; continue; }
                    if (device >= 0) scene.Devices[RgbScene.DeviceKey(d.Vendor, d.Name, d.Location)] = look;
                }
                catch (NotSupportedException e) { failed.Add(e.Message); }
            }
            if (device < 0 && skipped < targets.Count) { scene.All = new RgbLook(hex.Length > 0 ? hex : null, modeName.Length > 0 ? modeName : null, speed, bright, Off: hex.Length == 0 && modeName.Length == 0); scene.Devices.Clear(); }
            scene.Dark = false; Save();
            _log.LogInformation("RGB set: device {Device}, mode {Mode}{Name}, colour {Color}; refused: {Refused}, without the mode: {Skipped}", device, mode, modeName, hex.Length > 0 ? hex : "off", failed.Count, skipped);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            return State(failed.Count > 0 ? string.Join(" ", failed) : null);
        }));
        MethodAsync("rgb.dark", p => Guard(async () =>
        {
            if (!client.Connected) return State(Loc.Get("Rgb_NotConnected"));
            scene.Dark = p.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.True; Save();
            await session.ApplyAsync(scene, CancellationToken.None).ConfigureAwait(true);
            return State();
        }));
        MethodAsync("rgb.zone", p => Guard(async () =>
        {
            if (!client.Connected) return State(Loc.Get("Rgb_NotConnected"));
            int device = p.TryGetProperty("device", out var dv) && dv.TryGetInt32(out var di) ? di : -1, zone = p.TryGetProperty("zone", out var zv) && zv.TryGetInt32(out var zi) ? zi : -1;
            int leds = p.TryGetProperty("leds", out var lv) && lv.TryGetInt32(out var li) ? li : -1;
            var d = client.Devices.FirstOrDefault(x => x.Index == device); var z = d?.Zones.FirstOrDefault(x => x.Index == zone);
            if (d is null || z is null) throw new ArgumentException("zone");
            try { await client.ResizeZoneAsync(device, zone, leds, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception e) when (e is NotSupportedException or ArgumentOutOfRangeException) { return State(e.Message); }
            scene.Zones[RgbScene.ZoneKey(d.Vendor, d.Name, d.Location, z.Name)] = leds; Save(); _log.LogInformation("RGB zone {Device}/{Zone} set to {Leds} LEDs", d.Name, z.Name, leds);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            return State();
        }));
    }
}
