using System.Diagnostics; using System.IO; using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Hardware.Rgb; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// Light colours of the memory, graphics card, board and what plugs into it, through OpenRGB (a separate program, run as its server; see
    /// <see cref="OpenRgbClient"/>). The page can only ask for a colour (as "#rrggbb", or none for off), a mode by its name or number, a speed and brightness
    /// in percent, and the number of LEDs of a zone; the program's place is found here. The makers' own lighting programs are stopped while the page is in
    /// use and put back when it lets go (the button, or the app closing).
    /// </summary>
    private void RegisterRgb()
    {
        var client = new OpenRgbClient(); var conflicts = new RgbConflicts(); Process? server = null; var rgbLock = new object();
        const int port = OpenRgbClient.DefaultPort; bool busy = false;
        string zonesFile = Path.Combine(_paths.ConfigDir, "rgb-zones.json");
        string? Exe() => OpenRgbHost.Find(AppContext.BaseDirectory);
        static string ZoneKey(RgbDevice d, RgbZone z) => $"{d.Vendor}|{d.Name}|{d.Location}|{z.Name}";

        Dictionary<string, int> LoadZones()
        {
            try { return File.Exists(zonesFile) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(zonesFile)) ?? [] : []; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
        }
        void SaveZone(string key, int leds)
        {
            var all = LoadZones(); all[key] = leds;
            try { Directory.CreateDirectory(_paths.ConfigDir); File.WriteAllText(zonesFile, JsonSerializer.Serialize(all)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not keep the LED counts"); }
        }

        void Release()
        {
            lock (rgbLock)
            {
                client.Disconnect();
                try { if (server is { HasExited: false }) { server.Kill(true); server.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                server?.Dispose(); server = null; conflicts.RestoreAll();
            }
        }
        _cleanup.Add(Release);

        object State(string? error = null) => new
        {
            found = Exe() is not null, connected = client.Connected, error,
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

        async Task ReapplyZones()
        {
            var saved = LoadZones();
            foreach (var d in client.Devices.ToList())
                foreach (var z in d.Zones.Where(z => z.Resizable))
                    if (saved.TryGetValue(ZoneKey(d, z), out int leds) && leds != z.LedsCount && leds >= z.LedsMin && leds <= z.LedsMax)
                        await client.ResizeZoneAsync(d.Index, z.Index, leds, CancellationToken.None).ConfigureAwait(true);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        }

        async Task<object?> Connect(bool start, bool stopMakers)
        {
            if (!client.Connected)
            {
                if (!await OpenRgbHost.ListeningAsync(port, CancellationToken.None).ConfigureAwait(true))
                {
                    if (!start) return State();
                    if (Exe() is not { } exe) return State(Loc.Get("Rgb_NotFound"));
                    if (stopMakers) { var stopped = await Task.Run(conflicts.StopAll).ConfigureAwait(true); if (stopped.Count > 0) _log.LogInformation("Stopped for the lights: {Makers}", string.Join(", ", stopped)); }
                    server = await OpenRgbHost.StartAsync(exe, port, CancellationToken.None).ConfigureAwait(true);
                    if (server is null) { conflicts.RestoreAll(); return State(Loc.Get("Rgb_NoServer")); }
                    await Task.Delay(3000).ConfigureAwait(true);   // the port opens before the first device scan has finished
                }
                await client.ConnectAsync(port, CancellationToken.None).ConfigureAwait(true);
            }
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            if (start) await ReapplyZones().ConfigureAwait(true);
            _log.LogInformation("OpenRGB devices: {Devices}", string.Join(" | ", client.Devices.Select(d => $"{d.Kind} {d.Vendor} {d.Name} [{string.Join(", ", d.Zones.Select(z => $"{z.Name}:{z.LedsCount}/{z.LedsMax}"))}]")));
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

        MethodAsync("rgb.state", _ => Guard(() => Connect(false, false)));
        MethodAsync("rgb.start", p => Guard(() => Connect(true, !p.TryGetProperty("keepMakers", out var k) || k.ValueKind != JsonValueKind.True)));
        Method("rgb.release", _ => { Release(); return State(); });
        MethodAsync("rgb.set", p => Guard(async () =>
        {
            if (!client.Connected) return State(Loc.Get("Rgb_NotConnected"));
            int device = p.TryGetProperty("device", out var dv) && dv.TryGetInt32(out var di) ? di : -1;
            int? mode = p.TryGetProperty("mode", out var mv) && mv.TryGetInt32(out var mi) ? mi : null;
            string modeName = Str(p, "modeName");
            int? speed = p.TryGetProperty("speed", out var sv) && sv.TryGetInt32(out var si) ? si : null, bright = p.TryGetProperty("brightness", out var bv) && bv.TryGetInt32(out var bi) ? bi : null;
            string hex = Str(p, "color"); var color = RgbColor.Parse(hex);
            if (hex.Length > 0 && color is null) throw new ArgumentException("color");
            var targets = device < 0 ? client.Devices.Select(d => d.Index).ToList() : [device]; var failed = new List<string>(); int skipped = 0;
            foreach (var i in targets)
            {
                try
                {
                    // For everything at once the effect is named (its number differs by device); a device without it is left as it is.
                    int? m = modeName.Length > 0 ? client.ModeIndex(i, modeName) is var found and >= 0 ? found : null : mode;
                    if (modeName.Length > 0 && m is null) { skipped++; continue; }
                    if (m is { } index) await client.SetModeAsync(i, index, color, CancellationToken.None, speed, bright).ConfigureAwait(true);
                    else await client.SetColorAsync(i, color, CancellationToken.None).ConfigureAwait(true);
                }
                catch (NotSupportedException e) { failed.Add(e.Message); }
            }
            _log.LogInformation("RGB set: device {Device}, mode {Mode}{Name}, colour {Color}; refused: {Refused}, without the mode: {Skipped}", device, mode, modeName, hex.Length > 0 ? hex : "off", failed.Count, skipped);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            return State(failed.Count > 0 ? string.Join(" ", failed) : null);
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
            SaveZone(ZoneKey(d, z), leds); _log.LogInformation("RGB zone {Device}/{Zone} set to {Leds} LEDs", d.Name, z.Name, leds);
            await client.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            return State();
        }));
    }
}
