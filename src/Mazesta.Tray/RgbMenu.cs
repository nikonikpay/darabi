using Mazesta.Core.Rgb; using Mazesta.Hardware.Rgb; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Microsoft.Win32;
namespace Mazesta.Tray;

/// <summary>
/// The lights while only the tray runs. What the user set on the app's lighting page (<see cref="RgbScene"/>, kept in <c>rgb-scene.json</c>) is put back a little after
/// sign-in and again when the PC wakes, through the same <see cref="RgbSession"/> the app uses: OpenRGB is started hidden, and its window never shows. The menu switches
/// the scene on and off (the colours stay saved). Nothing is done when the scene was never switched on and the lights are not to be touched at sleep, and nothing runs between those moments.
/// Putting the lights out at sleep or shutdown writes down how they were first (<c>rgb-before-off.json</c>): at wake or at the next start they are put back from the scene, or, with
/// no scene, from that note, however the PC came back. What is done is written to <c>logs/tray-lights.log</c>.
/// </summary>
internal sealed class RgbMenu : IDisposable
{
    private readonly AppPaths _paths; private readonly Action<string, ToolTipIcon> _notify; private readonly RgbSession _session;
    private readonly ToolStripMenuItem _dark; private int _busy; private CancellationTokenSource _stale = new();
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 5000 }; private bool _appWas;
    public ToolStripMenuItem Menu { get; }

    public RgbMenu(AppPaths paths, Action<string, ToolTipIcon> notify)
    {
        _paths = paths; _notify = notify;
        _session = new RgbSession(AppContext.BaseDirectory, Path.Combine(paths.DataRoot, "openrgb"), NullLogger.Instance);
        _session.Runner.Yield = AppRuns;   // while the app is open the effects it runs are the ones shown
        _dark = new ToolStripMenuItem(TrayText.LightsOff, null, async (_, _) => await ToggleDarkAsync());
        Menu = new ToolStripMenuItem(TrayText.Lights);
        Menu.DropDownItems.AddRange([_dark, new ToolStripMenuItem(TrayText.LightsApply, null, async (_, _) => await ApplyAsync(userAsked: true))]);
        Menu.DropDownOpening += (_, _) => { var s = RgbSceneStore.Read(_paths); _dark.Checked = s.Dark; Menu.Enabled = _session.Exe is not null; };
        _watch.Tick += async (_, _) => await WatchAsync();
        SystemEvents.PowerModeChanged += OnPower; SystemEvents.SessionEnding += OnEnding;
    }

    private static bool AppRuns()
    {
        var app = System.Diagnostics.Process.GetProcessesByName("Mazesta").Concat(System.Diagnostics.Process.GetProcessesByName("Mazesta-Admin")).ToArray(); bool runs = app.Length > 0; foreach (var p in app) p.Dispose();
        return runs;
    }

    private void Log(string line)
    {
        try
        {
            string file = Path.Combine(_paths.DataRoot, "logs", "tray-lights.log"); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (File.Exists(file) && new FileInfo(file).Length > 200_000) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Going to sleep or shutting down: what the user chose for the lights (put them out, or put the scene on so they stay lit) is done first. Windows waits only a few
    /// seconds for this, so it is a best effort that gives up (and is cancelled, so it cannot land after the wake) when the time is up; "leave" does nothing. Before the lights
    /// are put out, how they are is written down, unless an earlier note is still waiting.</summary>
    private void AtPowerEnd()
    {
        var scene = RgbSceneStore.Read(_paths);
        string mode = RgbScene.NormalPowerEnd(scene.PowerEnd);
        if (mode == RgbScene.Leave) return;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { Log("power end: busy, skipped"); return; }
        _stale.Cancel(); _stale = new CancellationTokenSource(TimeSpan.FromSeconds(4)); var ct = _stale.Token;
        try
        {
            Task.Run(async () =>
            {
                if (_session.Client.Connected) { try { await _session.Client.RefreshAsync(ct); } catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or OperationCanceledException) { _session.Client.Disconnect(); } }   // a server that has gone: a new one is started below
                if (!_session.Client.Connected) { var r = await _session.ConnectAsync(true, scene.StopMakers, scene, ct); if (r != RgbConnect.Connected) { Log($"power end: not connected ({r})"); return; } }
                if (mode == RgbScene.Off)
                {
                    if (RgbSceneStore.ReadSnapshot(_paths) is null) { await _session.Client.RefreshAsync(ct); RgbSceneStore.WriteSnapshot(_paths, _session.Capture()); }
                    scene.Dark = true;
                }
                else scene.Dark = false;
                scene.Enabled = true;   // applied but not saved: the scene itself is untouched
                await _session.ApplyAsync(scene, ct);
                Log($"power end: {mode} done");
            }, CancellationToken.None).Wait(TimeSpan.FromSeconds(4));
        }
        catch (Exception e) when (e is AggregateException or System.Net.Sockets.SocketException or IOException or InvalidOperationException or OperationCanceledException) { Log($"power end: {e.GetType().Name} {e.Message}"); }
        finally { _stale.Cancel(); Volatile.Write(ref _busy, 0); }
    }

    private void OnEnding(object? sender, SessionEndingEventArgs e) => AtPowerEnd();

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) { AtPowerEnd(); return; }
        if (e.Mode != PowerModes.Resume) return;
        Log("resume");
        // The devices are not up at once, and an old connection into them is dead: the lights' server is started afresh a few seconds after the wake, and what is to be shown is
        // put on again a while later in case a device was still coming up the first time.
        After(8000, () => BackAsync(recover: true, final: false)); After(30000, () => BackAsync(recover: false, final: false)); After(75000, () => BackAsync(recover: false, final: true));
    }

    private static void After(int ms, Func<Task> work)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += async (_, _) => { t.Stop(); t.Dispose(); await work(); };
        t.Start();
    }

    /// <summary>At sign-in: the scene back on, when the user left it switched on, and whatever the lights were before they were put out at the last shutdown. Silent when it works.</summary>
    /// <summary>The devices of a cold boot can still be coming up when the tray starts: a try that did not reach them is made again after a while, three times at most.</summary>
    public async Task ApplyAtStartAsync()
    {
        foreach (int wait in new[] { 0, 20000, 60000 })
        {
            if (wait > 0) await Task.Delay(wait);
            if (await BackAsync(recover: false, final: true)) return;
        }
    }

    /// <summary>The lights back after a sleep or at the start: the scene when it is switched on, else the note of how they were. With <paramref name="recover"/> the server is started afresh.</summary>
    private async Task<bool> BackAsync(bool recover, bool final, bool force = false)
    {
        var scene = RgbSceneStore.Read(_paths); var snap = RgbSceneStore.ReadSnapshot(_paths); bool app = AppRuns();
        string end = RgbScene.NormalPowerEnd(scene.PowerEnd);
        if (!scene.Enabled && snap is null && end == RgbScene.Leave) return true;
        if (app && scene.Enabled && !force) { Log($"back: the app brings the scene back (recover {recover})"); return true; }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { Log("back: busy, skipped"); return false; }
        _stale.Cancel(); _stale = new CancellationTokenSource(); var ct = _stale.Token;
        try
        {
            // Not enabled: no maker's program is stopped for it, and the connection is only held so the lights can be put out at the next sleep.
            bool stopMakers = scene.Enabled && scene.StopMakers;
            var r = recover && !app ? await _session.RecoverAsync(scene, ct) : await _session.ConnectAsync(true, stopMakers, scene, ct);
            if (r != RgbConnect.Connected) { Log($"back: not connected ({r})"); return false; }
            if (scene.Enabled) { if (_session.ZonesDefaulted) RgbSceneStore.Write(_paths, scene); await _session.ApplyAsync(scene, ct); Log($"back: scene applied, {_session.Runner.Count} effect(s) run by the app"); }
            else if (snap is not null) { int n = await _session.RestoreAsync(snap, ct); Log($"back: {n} device(s) put back from the note"); if (n == 0) return false; }   // none reached: the note stays for the next try
            if (snap is not null && final) RgbSceneStore.ClearSnapshot(_paths);
            UpdateWatch(); return true;
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        { Log($"back: {e.GetType().Name} {e.Message}"); _session.Client.Disconnect(); return false; }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private async Task ApplyAsync(bool userAsked)   // the menu's "apply now"
    {
        var scene = RgbSceneStore.Read(_paths);
        if (!userAsked) { await BackAsync(recover: false, final: true); return; }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try
        {
            if (!scene.Enabled) { scene.Enabled = true; RgbSceneStore.Write(_paths, scene); }
            var result = await _session.ConnectAsync(true, scene.StopMakers, scene, CancellationToken.None);
            if (result == RgbConnect.Connected)
            {
                if (_session.ZonesDefaulted) RgbSceneStore.Write(_paths, scene);
                await _session.ApplyAsync(scene, CancellationToken.None); UpdateWatch();
                _notify(TrayText.LightsOn, ToolTipIcon.Info);
            }
            else _notify(result == RgbConnect.NotFound ? TrayText.LightsNoProgram : TrayText.LightsNoServer, ToolTipIcon.Warning);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        { _session.Client.Disconnect(); _notify(e.Message, ToolTipIcon.Warning); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>While the app runs effects that devices lack themselves, the tray watches whether the app is open (it then sends nothing) and takes the scene over when it closes.</summary>
    private void UpdateWatch() { if (_session.Runner.Count > 0) { _appWas = AppRuns(); _watch.Start(); } else _watch.Stop(); }

    private async Task WatchAsync()
    {
        if (_session.Runner.Count == 0) { _watch.Stop(); return; }
        bool app = AppRuns();
        if (_appWas && !app) { Log("the app closed: its effects are taken over"); await BackAsync(recover: false, final: true); }
        _appWas = app;
    }

    private async Task ToggleDarkAsync()
    {
        var scene = RgbSceneStore.Read(_paths); scene.Dark = !scene.Dark; scene.Enabled = true; RgbSceneStore.Write(_paths, scene);
        await BackAsync(recover: false, final: true, force: true);
    }

    /// <summary>The tray ends: the server it started is ended too, unless the app is running and may be using it.</summary>
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPower; SystemEvents.SessionEnding -= OnEnding; _watch.Dispose(); _stale.Cancel();
        _session.Release(leaveRunning: AppRuns()); _session.Client.Dispose();
    }
}
