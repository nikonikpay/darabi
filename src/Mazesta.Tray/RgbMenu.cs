using Mazesta.Core.Rgb; using Mazesta.Hardware.Rgb; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Microsoft.Win32;
namespace Mazesta.Tray;

/// <summary>
/// The lights while only the tray runs. What the user set on the app's lighting page (<see cref="RgbScene"/>, kept in <c>rgb-scene.json</c>) is put back a little after
/// sign-in and again when the PC wakes, through the same <see cref="RgbSession"/> the app uses: OpenRGB is started hidden, and its window never shows. The menu switches
/// the scene on and off (the colours stay saved). Nothing is done when the scene was never switched on, and nothing runs between those moments.
/// </summary>
internal sealed class RgbMenu : IDisposable
{
    private readonly AppPaths _paths; private readonly Action<string, ToolTipIcon> _notify; private readonly RgbSession _session;
    private readonly ToolStripMenuItem _dark; private bool _busy;
    public ToolStripMenuItem Menu { get; }

    public RgbMenu(AppPaths paths, Action<string, ToolTipIcon> notify)
    {
        _paths = paths; _notify = notify;
        _session = new RgbSession(AppContext.BaseDirectory, Path.Combine(paths.DataRoot, "openrgb"), NullLogger.Instance);
        _dark = new ToolStripMenuItem(TrayText.LightsOff, null, async (_, _) => await ToggleDarkAsync());
        Menu = new ToolStripMenuItem(TrayText.Lights);
        Menu.DropDownItems.AddRange([_dark, new ToolStripMenuItem(TrayText.LightsApply, null, async (_, _) => await ApplyAsync(userAsked: true))]);
        Menu.DropDownOpening += (_, _) => { var s = RgbSceneStore.Read(_paths); _dark.Checked = s.Dark; Menu.Enabled = _session.Exe is not null; };
        SystemEvents.PowerModeChanged += OnPower;
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var t = new System.Windows.Forms.Timer { Interval = 8000 };
        t.Tick += async (_, _) => { t.Stop(); t.Dispose(); await ApplyAsync(userAsked: false); };
        t.Start();
    }

    /// <summary>At sign-in: the scene back on, when the user left it switched on. Silent when it works.</summary>
    public Task ApplyAtStartAsync() => ApplyAsync(userAsked: false);

    private async Task ApplyAsync(bool userAsked)
    {
        var scene = RgbSceneStore.Read(_paths);
        if (_busy || (!userAsked && !scene.Enabled)) return;
        _busy = true;
        try
        {
            if (userAsked && !scene.Enabled) { scene.Enabled = true; RgbSceneStore.Write(_paths, scene); }
            var result = await _session.ConnectAsync(true, scene.StopMakers, scene, CancellationToken.None);
            if (result == RgbConnect.Connected)
            {
                if (_session.ZonesDefaulted) RgbSceneStore.Write(_paths, scene);
                await _session.ApplyAsync(scene, CancellationToken.None);
                if (userAsked) _notify(TrayText.LightsOn, ToolTipIcon.Info);
            }
            else if (userAsked) _notify(result == RgbConnect.NotFound ? TrayText.LightsNoProgram : TrayText.LightsNoServer, ToolTipIcon.Warning);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
        { _session.Client.Disconnect(); if (userAsked) _notify(e.Message, ToolTipIcon.Warning); }
        finally { _busy = false; }
    }

    private async Task ToggleDarkAsync()
    {
        var scene = RgbSceneStore.Read(_paths); scene.Dark = !scene.Dark; scene.Enabled = true; RgbSceneStore.Write(_paths, scene);
        await ApplyAsync(userAsked: false);
    }

    /// <summary>The tray ends: the server it started is ended too, unless the app is running and may be using it.</summary>
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPower;
        var app = System.Diagnostics.Process.GetProcessesByName("MazestaWeb"); bool appRuns = app.Length > 0; foreach (var p in app) p.Dispose();
        _session.Release(leaveRunning: appRuns); _session.Client.Dispose();
    }
}
