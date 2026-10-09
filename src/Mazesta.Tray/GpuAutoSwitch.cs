using System.Diagnostics; using System.Runtime.InteropServices; using Mazesta.Core.Tuning; using Mazesta.Persistence;
namespace Mazesta.Tray;

/// <summary>
/// Puts the card in the saved profile that goes with what is being used: the one the technician chose for a game (a full-screen game, as Windows itself judges it)
/// or for a listed graphics program (Lumion, Chaos Vantage, D5 Render, 3ds Max...), and back in its start-up profile when neither is. The rules are the tuning page's
/// (<see cref="GpuRulesFile"/>). Nothing runs while there are no rules: a file watcher starts a timer when some are saved, and the timer stops when they are
/// removed. While the app is searching (its journal) or running a benchmark the card is left alone.
/// </summary>
internal sealed class GpuAutoSwitch : IDisposable
{
    private readonly string _file; private readonly GpuProfilesMenu _gpu; private readonly Func<bool> _busy;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 }, _debounce = new() { Interval = 400 }; private readonly FileSystemWatcher? _watch; private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private GpuRules _rules = GpuRules.None; private string? _applied; private bool _known;

    public GpuAutoSwitch(AppPaths paths, GpuProfilesMenu gpu, Func<bool> busy)
    {
        _file = GpuRulesFile.FileIn(paths); _gpu = gpu; _busy = busy;
        _timer.Tick += (_, _) => Check();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        try
        {
            Directory.CreateDirectory(paths.ConfigDir);
            _watch = new(paths.ConfigDir, Path.GetFileName(_file)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            // The watcher calls from a pool thread; the timers belong to the tray's own. A save is one or two events: the reload waits for them to stop.
            void Changed() => _ui?.Post(_ => { _debounce.Stop(); _debounce.Start(); }, null);
            _watch.Changed += (_, _) => Changed(); _watch.Created += (_, _) => Changed(); _watch.Renamed += (_, _) => Changed();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        Reload();
    }

    private void Reload()
    {
        _rules = GpuRulesFile.Read(_file); _known = false;
        if (_rules.IsEmpty) { _timer.Stop(); if (_applied is not null) { _applied = null; _gpu.ApplyRule(null); } }
        else { if (!_timer.Enabled) _timer.Start(); }
    }

    private void Check()
    {
        if (_busy() || _rules.IsEmpty) return;
        var running = new HashSet<string>(); string? front = null;
        foreach (var p in Process.GetProcesses()) { try { running.Add(p.ProcessName.ToLowerInvariant() + ".exe"); } catch (InvalidOperationException) { } finally { p.Dispose(); } }
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (pid != 0) using (var p = Process.GetProcessById((int)pid)) front = p.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        bool game = SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 && front is not null && front != "explorer.exe";   // busy (a full-screen program) or a Direct3D full-screen one
        string? want = GpuAutoRules.Decide(_rules, running, front, game);
        if (_known && want == _applied) return;
        _known = true; _applied = want;
        _gpu.ApplyRule(want);
    }

    public void Dispose() { _timer.Dispose(); _debounce.Dispose(); _watch?.Dispose(); }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
