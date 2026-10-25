using Mazesta.Core.Rgb; using Mazesta.Hardware.Rgb; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.App;

/// <summary>
/// <c>--selftest=lights</c>: what the lights do around sleep and for the effects, on the real devices, with nobody at the screen. It writes down how the lights are, puts them out and
/// puts them back from that note, ends the lights' server and starts it again (what a wake does) and checks the devices are found again, then runs the universal effects and a colour
/// spectrum on every device and checks that those the app drives really move. The user's own scene is read, never written, and the lights are put back as they were at the end.
/// </summary>
internal static class SelfTestLights
{
    public static async Task RunAsync(AppPaths paths, Action<string> say)
    {
        var scene = RgbSceneStore.Read(paths); using var session = new RgbSession(AppContext.BaseDirectory, Path.Combine(paths.DataRoot, "openrgb"), NullLogger.Instance);
        if (session.Exe is null) { say("OpenRGB was not found beside the app."); return; }
        var r = await session.ConnectAsync(true, stopMakers: false, scene, CancellationToken.None);
        say($"connect: {r}, {session.Client.Devices.Count} device(s)"); if (r != RgbConnect.Connected) return;
        foreach (var d in session.Client.Devices) say($"  [{d.Index}] {d.Kind} {d.Name}: {d.LedCount} LEDs, active '{(d.ActiveMode >= 0 && d.ActiveMode < d.Modes.Count ? d.Modes[d.ActiveMode].Name : "?")}', modes: {string.Join(", ", d.Modes.Select(m => m.Name))}");

        // 1. Put out and put back from the note.
        var snap = session.Capture(); say($"note written for {snap.Devices.Count} device(s)");
        await session.ApplyAsync(new RgbScene { Enabled = true, Dark = true }, CancellationToken.None); await Task.Delay(1000);
        await session.Client.RefreshAsync(CancellationToken.None);
        foreach (var d in session.Client.Devices) say($"  out: [{d.Index}] first LED {(d.Colors.Count > 0 ? d.Colors[0].Hex : "-")}");
        int back = await session.RestoreAsync(snap, CancellationToken.None); await Task.Delay(500); await session.Client.RefreshAsync(CancellationToken.None);
        say($"put back from the note: {back} device(s)");
        foreach (var d in session.Client.Devices)
        {
            var was = snap.Devices.GetValueOrDefault(RgbScene.DeviceKey(d.Vendor, d.Name, d.Location)); string mode = d.ActiveMode >= 0 && d.ActiveMode < d.Modes.Count ? d.Modes[d.ActiveMode].Name : "?";
            say($"  back: [{d.Index}] mode '{mode}' (was '{was?.Mode}'), first LED {(d.Colors.Count > 0 ? d.Colors[0].Hex : "-")} (was {(was?.Leds.Count > 0 ? was.Leds[0] : "-")}) => {(was is not null && was.Mode == mode ? "OK" : "DIFFERENT")}");
        }

        // 2. What a wake does: the server ended and started again, the devices found afresh.
        int before = session.Client.Devices.Count; var watch = System.Diagnostics.Stopwatch.StartNew();
        var again = await session.RecoverAsync(scene, CancellationToken.None);
        say($"recover: {again} after {watch.Elapsed.TotalSeconds:F1} s, {session.Client.Devices.Count} device(s) (was {before}) => {(again == RgbConnect.Connected && session.Client.Devices.Count == before ? "OK" : "DIFFERENT")}");
        if (again != RgbConnect.Connected) return;

        // 3. Each universal effect on every device, and whether the ones the app drives move.
        foreach (var fx in new[] { RgbEffects.Rainbow, RgbEffects.Cycle, RgbEffects.Breathing, RgbEffects.Flash })
        {
            var how = new List<string>();
            foreach (var d in session.Client.Devices.ToList()) how.Add($"[{d.Index}]{await session.ApplyLookAsync(d.Index, new RgbLook("#ff3300", fx, 80, 100), CancellationToken.None)}");
            session.Runner.Start(); await Task.Delay(700);
            await session.Client.RefreshAsync(CancellationToken.None); var first = session.Client.Devices.ToDictionary(d => d.Index, d => string.Join("", d.Colors.Take(8).Select(c => c.Hex)));
            await Task.Delay(450); await session.Client.RefreshAsync(CancellationToken.None);
            var moved = session.Client.Devices.Where(d => first[d.Index] != string.Join("", d.Colors.Take(8).Select(c => c.Hex))).Select(d => d.Index).ToList();
            say($"{fx}: {string.Join(" ", how)} | by the app: {session.Runner.Count} | colours changed within 0.45 s on: {(moved.Count == 0 ? "none" : string.Join(",", moved))}");
        }

        // 4. A colour spectrum as a still gradient.
        foreach (var d in session.Client.Devices.ToList()) say($"palette 'sunset' on [{d.Index}]: {await session.ApplyLookAsync(d.Index, new RgbLook(Palette: "sunset"), CancellationToken.None)}");
        await Task.Delay(500); await session.Client.RefreshAsync(CancellationToken.None);
        foreach (var d in session.Client.Devices) if (d.Colors.Count > 1) say($"  [{d.Index}] first {d.Colors[0].Hex} last {d.Colors[^1].Hex}");

        // 4b. A spectrum made from one chosen colour ("custom:"), on all at once, how long it takes; then it moving for 10 s, and what that costs in processor time.
        var watch2 = System.Diagnostics.Stopwatch.StartNew(); const string custom = "custom:#ff0000,#ffd400,#00e5ff";
        foreach (var d in session.Client.Devices.ToList()) await session.ApplyLookAsync(d.Index, new RgbLook("#ff0000", Palette: custom), CancellationToken.None);
        say($"custom spectrum on all {session.Client.Devices.Count}: {watch2.Elapsed.TotalSeconds:F2} s");
        await session.Client.RefreshAsync(CancellationToken.None); foreach (var d in session.Client.Devices) if (d.Colors.Count > 1) say($"  [{d.Index}] first {d.Colors[0].Hex} last {d.Colors[^1].Hex}");
        foreach (var d in session.Client.Devices.ToList()) await session.ApplyLookAsync(d.Index, new RgbLook("#ff0000", RgbEffects.Rainbow, 60, 100, Palette: custom), CancellationToken.None);
        session.Runner.Start(); var me = System.Diagnostics.Process.GetCurrentProcess(); var cpu0 = me.TotalProcessorTime; await Task.Delay(10000); me.Refresh();
        say($"moving spectrum on {session.Runner.Count} device(s): {(me.TotalProcessorTime - cpu0).TotalMilliseconds / 100:F1} % of one core over 10 s (this process)");
        var cpu1 = me.TotalProcessorTime; await Task.Delay(10000); me.Refresh(); say($"the same, the next 10 s (warm): {(me.TotalProcessorTime - cpu1).TotalMilliseconds / 100:F1} % of one core");
        // A server that is behind with the frames answers late: how long a plain question takes while the effects run, and whether OpenRGB itself is busy.
        var ask = System.Diagnostics.Stopwatch.StartNew(); await session.Client.RefreshAsync(CancellationToken.None); say($"a question to the server while the effects run: {ask.ElapsedMilliseconds} ms");
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("OpenRGB")) { var t0 = p.TotalProcessorTime; await Task.Delay(5000); p.Refresh(); say($"OpenRGB itself over 5 s: {(p.TotalProcessorTime - t0).TotalMilliseconds / 50:F1} % of one core"); p.Dispose(); }

        // 5. As it was.
        session.Runner.Stop(); session.Runner.Clear(); int restored = await session.RestoreAsync(snap, CancellationToken.None);
        say($"the lights put back as they were: {restored} device(s)");
    }
}
