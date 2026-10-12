using System.Diagnostics;
using Mazesta.Core.Health; using Mazesta.Core.Tray; using Mazesta.Core.Providers; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Mazesta.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Tray;

/// <summary>
/// The tray icon and its three schedules. Temperatures are read every <see cref="TrayIntervals.IdleMinutes"/> (hourly by default; every <see cref="TrayIntervals.WatchSeconds"/>
/// while a part is hot at full load, until it has stayed so for the hold or cooled); the drives' health every <see cref="TrayIntervals.HealthMinutes"/> (daily by default);
/// the RAM in use every <see cref="TrayIntervals.RamCheckMinutes"/> (a plain Windows call, no sensor driver).
/// Idle cost is two sleeping timers and an icon: the sensor provider (the heavy part) exists only during a check. Every check is kept in
/// <see cref="TrayCheckLog"/>; a problem is announced as a Windows notification. Double-clicking the icon opens the summary window.
/// The menu also turns the app's on-screen overlay on and off (the app keeps it up after its window closes while the tray runs; see
/// <see cref="OverlaySignals"/>) and switches the GPU profile (<see cref="GpuProfilesMenu"/>), which the tray also puts back at every sign-in.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan Busy = TimeSpan.FromSeconds(20);
    private readonly TrayIntervals _intervals; private readonly string _logFile;
    private readonly NotifyIcon _icon; private readonly System.Windows.Forms.Timer _temps = new(), _health = new(), _ram = new(); private readonly HealthAlerts _rules; private readonly MemoryPressure _memory; private readonly string _ramFile;
    private readonly HashSet<string> _drivesAnnounced = [];
    private readonly ToolStripMenuItem _status = new() { Enabled = false }, _checkNow, _overlay;
    private readonly GpuProfilesMenu _gpu; private readonly GpuAutoSwitch _auto; private OverlayKeys? _keys; private readonly PowerTimerMenu _power; private readonly System.Windows.Forms.Timer _gpuAtStart = new() { Interval = 15_000 }; private readonly RgbMenu _rgb; private readonly FanMenu _fans;
    private SummaryForm? _summary; private bool _checking;

    public IReadOnlyList<TrayCheck> Checks { get; private set; }
    public bool IsChecking => _checking;
    public event Action? Changed;

    public TrayContext(TrayIntervals intervals, string logFile, AppPaths paths)
    {
        _intervals = intervals; _logFile = logFile; _rules = new(TimeSpan.FromMinutes(intervals.HoldMinutes), intervals.CpuAlertC, intervals.GpuAlertC, CpuBaseClock.Mhz, intervals.GpuHotSpotAlertC);
        // The RAM warning comes once a day: the last time it came is kept beside the check log, so a restart of the tray does not make it come again.
        _memory = new(intervals.RamAlertPercent, TimeSpan.FromMinutes(intervals.RamHoldMinutes)); _ramFile = Path.Combine(Path.GetDirectoryName(logFile) ?? "", "ram-alert.txt");
        try { if (DateTimeOffset.TryParse(File.ReadAllText(_ramFile), null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)) _memory.LastFired = last; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Checks = TrayCheckLog.Read(logFile);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        var open = new ToolStripMenuItem(TrayText.OpenApp, null, (_, _) => OpenApp()) { Font = new Font(menu.Font, FontStyle.Bold) };
        _checkNow = new ToolStripMenuItem(TrayText.CheckNow, null, async (_, _) => await CheckAllAsync());
        _overlay = new ToolStripMenuItem(TrayText.Overlay, null, (_, _) => ToggleOverlay());
        _gpu = new GpuProfilesMenu(paths, (text, kind) => _icon!.ShowBalloonTip(kind == ToolTipIcon.Info ? 5000 : 15000, TrayText.Title, text, kind));
        _power = new PowerTimerMenu((text, kind) => _icon!.ShowBalloonTip(kind == ToolTipIcon.Info ? 5000 : 15000, TrayText.Title, text, kind));
        _auto = new GpuAutoSwitch(paths, _gpu, Benchmarking);
        _rgb = new RgbMenu(paths, (text, kind) => _icon!.ShowBalloonTip(kind == ToolTipIcon.Info ? 5000 : 15000, TrayText.Title, text, kind));
        _fans = new FanMenu(paths, StartApp, (text, kind) => _icon!.ShowBalloonTip(kind == ToolTipIcon.Info ? 5000 : 15000, TrayText.Title, text, kind));
        menu.Items.AddRange([open, new ToolStripMenuItem(TrayText.Summary, null, (_, _) => ShowSummary()), _checkNow, new ToolStripSeparator(), _overlay, _gpu.Menu, _fans.Menu, _rgb.Menu, _power.Menu, new ToolStripSeparator(),
            _status, new ToolStripSeparator(), new ToolStripMenuItem(TrayText.Exit, null, (_, _) => ExitThread())]);
        menu.Opening += (_, _) => _overlay.Checked = OverlayShown();
        _icon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield, Text = TrayText.Title, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowSummary();
        _icon.BalloonTipClicked += (_, _) => ShowSummary();
        Refresh();
        _temps.Tick += async (_, _) => { if (Benchmarking()) Schedule(_temps, TimeSpan.FromMinutes(1)); else await CheckTempsAsync(); };
        _health.Tick += async (_, _) => { if (Benchmarking()) Schedule(_health, TimeSpan.FromMinutes(1)); else await CheckHealthAsync(); };
        _ram.Tick += (_, _) => CheckMemory();
        Schedule(_ram, TimeSpan.FromSeconds(intervals.FirstCheckSeconds + 20));
        // The first checks come shortly after sign-in, not during it, and not both at once.
        Schedule(_temps, TimeSpan.FromSeconds(intervals.FirstCheckSeconds)); Schedule(_health, TimeSpan.FromSeconds(intervals.FirstCheckSeconds + 40));
        // The GPU profile a little after sign-in, once the driver has settled.
        _gpuAtStart.Tick += async (_, _) => { _gpuAtStart.Stop(); _gpu.ApplyAtStart(); _fans.ApplyAtStart(); await _rgb.ApplyAtStartAsync(); Memory.Release(); };
        _gpuAtStart.Start();
        _keys = new OverlayKeys(OverlayKeysFile.Read(paths.ConfigFile), ToggleOverlay, MoveOverlay, () => SignalApp(OverlaySignals.Layout), () => SignalApp(OverlaySignals.Preset));
    }

    /// <summary>The app is running a benchmark: a scheduled check waits a minute and asks again (one asked for from the menu still runs).</summary>
    private static bool Benchmarking()
    {
        if (!EventWaitHandle.TryOpenExisting(OverlaySignals.BenchmarkBusy, out var busy)) return false;
        using (busy) return busy.WaitOne(0);
    }

    /// <summary>Whether the app shows the overlay now: the app keeps the named event set while it does; no app running means no overlay.</summary>
    private static bool OverlayShown()
    {
        if (!EventWaitHandle.TryOpenExisting(OverlaySignals.Shown, out var shown)) return false;
        using (shown) return shown.WaitOne(0);
    }

    /// <summary>The running app shows or hides it; with no app running, the app is started for the overlay alone.</summary>
    private void ToggleOverlay()
    {
        if (EventWaitHandle.TryOpenExisting(OverlaySignals.Toggle, out var toggle)) { using (toggle) toggle.Set(); return; }
        StartApp(OverlaySignals.Argument);
    }

    /// <summary>A shortcut that only changes how the overlay looks: told to the running app; with no app running nothing is started for it (there is no overlay to change).</summary>
    private static void SignalApp(string name) { if (EventWaitHandle.TryOpenExisting(name, out var e)) using (e) e.Set(); }

    /// <summary>The move shortcut (Alt+M by default): the running app puts the overlay in the mode where the mouse moves and resizes it; with no app running it is started for the overlay alone.</summary>
    private void MoveOverlay()
    {
        if (EventWaitHandle.TryOpenExisting(OverlaySignals.Move, out var move)) { using (move) move.Set(); return; }
        StartApp(OverlaySignals.Argument);
    }

    private static void Schedule(System.Windows.Forms.Timer timer, TimeSpan after) { timer.Stop(); timer.Interval = (int)Math.Clamp(after.TotalMilliseconds, 1000, int.MaxValue); timer.Start(); }

    /// <summary>A plain read of the RAM in use; a warning is a notification only (it is not a check of the log). A benchmark's own load is not a problem, so it waits.</summary>
    private void CheckMemory()
    {
        double? used = Benchmarking() ? null : MemoryLoad.UsedPercent();
        if (_memory.Evaluate(used, DateTimeOffset.UtcNow) && used is { } p)
        {
            Notify(TrayText.MemoryFull(p));
            try { File.WriteAllText(_ramFile, _memory.LastFired!.Value.ToString("O")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        Schedule(_ram, TimeSpan.FromMinutes(_memory.IsWatching ? 1 : _intervals.RamCheckMinutes));
    }

    public async Task CheckAllAsync() { await CheckTempsAsync(); await CheckHealthAsync(); }

    private async Task CheckTempsAsync()
    {
        if (_checking) { Schedule(_temps, Busy); return; }
        Begin(); _temps.Stop();
        TrayCheck check;
        try
        {
            var sample = await Within(Task.Run(TakeSample), TimeSpan.FromMinutes(3));
            var now = DateTimeOffset.UtcNow;
            var alerts = _rules.Evaluate(sample, now).Select(TrayText.Alert).ToList();
            check = new(now, "temps", sample.CpuTempC, sample.GpuTempC, [], alerts, null, sample.GpuHotSpotC);
            foreach (var a in alerts) Notify(a);
        }
        catch (Exception e) { check = new(DateTimeOffset.UtcNow, "temps", null, null, [], [], e.Message); }
        End(check);
        Schedule(_temps, _rules.IsWatching ? TimeSpan.FromSeconds(_intervals.WatchSeconds) : TimeSpan.FromMinutes(_intervals.IdleMinutes));
    }

    private async Task CheckHealthAsync()
    {
        if (_checking) { Schedule(_health, Busy); return; }
        Begin(); _health.Stop();
        TrayCheck check;
        try
        {
            var drives = (await Within(Task.Run(() => new WmiDriveHealthProvider(new WmiQuery()).Read()), TimeSpan.FromMinutes(2)))
                .Select(d => new TrayDrive(d.Name, DriveAttention.Verdict(d.Status, d.WearPercent), d.WearPercent, d.TemperatureC, DriveAttention.Needs(d) || DriveAttention.Verdict(d.Status, d.WearPercent) == "Unhealthy", d.Letters)).ToList();
            var problems = drives.Where(d => d.NeedsAttention || d.Status == "Warning").Select(TrayText.DriveProblem).ToList();
            // A drive's problem is announced once; it is announced again only after it has cleared and come back.
            foreach (var d in drives)
                if (!d.NeedsAttention && d.Status != "Warning") _drivesAnnounced.Remove(d.Name);
                else if (_drivesAnnounced.Add(d.Name)) Notify(TrayText.DriveProblem(d));
            check = new(DateTimeOffset.UtcNow, "health", null, null, drives, problems, null);
        }
        catch (Exception e) { check = new(DateTimeOffset.UtcNow, "health", null, null, [], [], e.Message); }
        End(check);
        Schedule(_health, TimeSpan.FromMinutes(_intervals.HealthMinutes));
    }

    /// <summary>The work, or a timeout once <paramref name="limit"/> has passed: a sensor open or a WMI query that never returns is reported as a failed
    /// check and the next one runs on time, instead of the tray waiting on it for good with every later check held back behind it. The stuck work is left
    /// to finish (or not) on its own thread.</summary>
    private static async Task<T> Within<T>(Task<T> work, TimeSpan limit)
        => await Task.WhenAny(work, Task.Delay(limit)) == work ? await work : throw new TimeoutException($"no answer in {limit.TotalMinutes:0} min");

    private void Begin() { _checking = true; _checkNow.Enabled = false; Changed?.Invoke(); }

    private void End(TrayCheck check)
    {
        try { Checks = TrayCheckLog.Append(_logFile, check); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Checks = [.. Checks.TakeLast(TrayCheckLog.Keep - 1), check]; }
        _checking = false; _checkNow.Enabled = true;
        Refresh(); Changed?.Invoke();
        Memory.Release();
    }

    private void Refresh()
    {
        var temps = Checks.LastOrDefault(c => c.Kind == "temps" && c.Error is null);
        var last = Checks.LastOrDefault();
        _status.Text = TrayText.Status(last?.Time, last?.Error is { } e ? $"{TrayText.CheckFailed}: {e}" : null);
        _icon.Text = TrayText.Tooltip(temps);
    }

    private void Notify(string text) => _icon.ShowBalloonTip(15000, TrayText.Title, text, ToolTipIcon.Warning);

    private void ShowSummary()
    {
        if (_summary is { IsDisposed: false }) { _summary.WindowState = FormWindowState.Normal; _summary.Activate(); return; }
        _summary = new SummaryForm(this);
        _summary.Show(); _summary.Activate();
        // Opened after a while, the summary reads fresh temperatures rather than showing an old check as if it were now.
        var temps = Checks.LastOrDefault(c => c.Kind == "temps");
        if (!_checking && (temps is null || DateTimeOffset.UtcNow - temps.Time > TimeSpan.FromMinutes(2))) _ = CheckTempsAsync();
    }

    /// <summary>Starts the app next to the tray. A running app brings its window forward instead of starting twice.</summary>
    public void OpenApp() => StartApp("");

    private void StartApp(string arguments)
    {
        string? exe = new[] { "Mazesta.exe", "Mazesta-Admin.exe" }.Select(n => Path.Combine(AppContext.BaseDirectory, n)).FirstOrDefault(File.Exists);
        if (exe is null) { _icon.ShowBalloonTip(8000, TrayText.Title, TrayText.NoApp, ToolTipIcon.Error); return; }
        try { Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { _icon.ShowBalloonTip(8000, TrayText.Title, e.Message, ToolTipIcon.Error); }
    }

    /// <summary>Opens the provider, polls twice (load and clocks are deltas, so the first poll reads zero), and disposes it.</summary>
    private static HealthSample TakeSample()
    {
        using var provider = LibreHardwareMonitorProvider.CreateDefault(new SystemClock(), NullLoggerFactory.Instance);
        provider.Start();
        var all = provider.Hardware.Select(n => n.Id).ToHashSet();
        provider.Poll(new PollRequest(DateTimeOffset.UtcNow, all)); Thread.Sleep(1000);
        var result = provider.Poll(new PollRequest(DateTimeOffset.UtcNow, all));
        return HealthSampler.From(provider.Hardware, result.Readings);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _power.Dispose(); _keys?.Dispose(); _auto.Dispose(); _rgb.Dispose(); _temps.Dispose(); _health.Dispose(); _ram.Dispose(); _gpuAtStart.Dispose(); _summary?.Dispose(); _icon.Visible = false; _icon.Dispose(); }
        base.Dispose(disposing);
    }
}
