using System.Diagnostics;
using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Mazesta.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Tray;

/// <summary>
/// The tray icon and its two schedules. Temperatures are read every <see cref="TrayIntervals.IdleMinutes"/> (every <see cref="TrayIntervals.WatchSeconds"/>
/// while a rule is building towards an alert, so a real problem is confirmed in minutes); the drives' health every <see cref="TrayIntervals.HealthMinutes"/>.
/// Idle cost is two sleeping timers and an icon: the sensor provider (the heavy part) exists only during a check. Every check is kept in
/// <see cref="TrayCheckLog"/>; a problem is announced as a Windows notification. Double-clicking the icon opens the summary window.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan Busy = TimeSpan.FromSeconds(20);
    private readonly TrayIntervals _intervals; private readonly string _logFile;
    private readonly NotifyIcon _icon; private readonly System.Windows.Forms.Timer _temps = new(), _health = new(); private readonly HealthAlerts _rules = new();
    private readonly HashSet<string> _drivesAnnounced = [];
    private readonly ToolStripMenuItem _status = new() { Enabled = false }, _checkNow;
    private SummaryForm? _summary; private bool _checking;

    public IReadOnlyList<TrayCheck> Checks { get; private set; }
    public bool IsChecking => _checking;
    public event Action? Changed;

    public TrayContext(TrayIntervals intervals, string logFile)
    {
        _intervals = intervals; _logFile = logFile;
        Checks = TrayCheckLog.Read(logFile);
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, ShowImageMargin = false };
        var open = new ToolStripMenuItem(TrayText.OpenApp, null, (_, _) => OpenApp()) { Font = new Font(menu.Font, FontStyle.Bold) };
        _checkNow = new ToolStripMenuItem(TrayText.CheckNow, null, async (_, _) => await CheckAllAsync());
        menu.Items.AddRange([open, new ToolStripMenuItem(TrayText.Summary, null, (_, _) => ShowSummary()), _checkNow, new ToolStripSeparator(), _status, new ToolStripSeparator(),
            new ToolStripMenuItem(TrayText.Exit, null, (_, _) => ExitThread())]);
        _icon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield, Text = TrayText.Title, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowSummary();
        _icon.BalloonTipClicked += (_, _) => ShowSummary();
        Refresh();
        _temps.Tick += async (_, _) => await CheckTempsAsync(); _health.Tick += async (_, _) => await CheckHealthAsync();
        // The first checks come shortly after sign-in, not during it, and not both at once.
        Schedule(_temps, TimeSpan.FromSeconds(intervals.FirstCheckSeconds)); Schedule(_health, TimeSpan.FromSeconds(intervals.FirstCheckSeconds + 40));
    }

    private static void Schedule(System.Windows.Forms.Timer timer, TimeSpan after) { timer.Stop(); timer.Interval = (int)Math.Clamp(after.TotalMilliseconds, 1000, int.MaxValue); timer.Start(); }

    public async Task CheckAllAsync() { await CheckTempsAsync(); await CheckHealthAsync(); }

    private async Task CheckTempsAsync()
    {
        if (_checking) { Schedule(_temps, Busy); return; }
        Begin(); _temps.Stop();
        TrayCheck check;
        try
        {
            var sample = await Task.Run(TakeSample);
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
            var drives = (await Task.Run(() => new WmiDriveHealthProvider(new WmiQuery()).Read()))
                .Select(d => new TrayDrive(d.Name, d.Status, d.WearPercent, d.TemperatureC, DriveAttention.Needs(d))).ToList();
            var problems = drives.Where(d => d.NeedsAttention).Select(TrayText.DriveProblem).ToList();
            // A drive's problem is announced once; it is announced again only after it has cleared and come back.
            foreach (var d in drives)
                if (!d.NeedsAttention) _drivesAnnounced.Remove(d.Name);
                else if (_drivesAnnounced.Add(d.Name)) Notify(TrayText.DriveProblem(d));
            check = new(DateTimeOffset.UtcNow, "health", null, null, drives, problems, null);
        }
        catch (Exception e) { check = new(DateTimeOffset.UtcNow, "health", null, null, [], [], e.Message); }
        End(check);
        Schedule(_health, TimeSpan.FromMinutes(_intervals.HealthMinutes));
    }

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

    /// <summary>Starts the app next to the tray, the web edition first. A running app brings its window forward instead of starting twice.</summary>
    public void OpenApp()
    {
        var exe = new[] { "MazestaWeb.exe", "MazestaTest.exe" }.Select(n => Path.Combine(AppContext.BaseDirectory, n)).FirstOrDefault(File.Exists);
        if (exe is null) { _icon.ShowBalloonTip(8000, TrayText.Title, TrayText.NoApp, ToolTipIcon.Error); return; }
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
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
        if (disposing) { _temps.Dispose(); _health.Dispose(); _summary?.Dispose(); _icon.Visible = false; _icon.Dispose(); }
        base.Dispose(disposing);
    }
}
