using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Tray;

/// <summary>
/// The tray icon and its schedule. Idle cost is a sleeping timer and an icon: the sensor provider (the heavy part)
/// exists only during a check. Checks run every <see cref="Idle"/>, or every <see cref="Watch"/> while a rule is
/// building towards an alert, so a real problem is confirmed in minutes without polling all day.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(10), Watch = TimeSpan.FromSeconds(30);
    private readonly NotifyIcon _icon; private readonly System.Windows.Forms.Timer _timer = new(); private readonly HealthAlerts _rules = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private DateTimeOffset? _last; private string? _problem; private bool _checking;

    public TrayContext()
    {
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        menu.Items.Add(_status); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TrayText.CheckNow, null, (_, _) => Check()); menu.Items.Add(TrayText.Exit, null, (_, _) => ExitThread());
        _icon = new NotifyIcon { Icon = SystemIcons.Shield, Text = TrayText.Title, ContextMenuStrip = menu, Visible = true };
        _status.Text = TrayText.Status(null, null);
        _timer.Tick += (_, _) => Check(); Schedule(TimeSpan.FromSeconds(20));   // first check shortly after sign-in, not during it
    }

    private void Schedule(TimeSpan after) { _timer.Interval = (int)after.TotalMilliseconds; _timer.Start(); }

    private async void Check()
    {
        if (_checking) return;
        _checking = true; _timer.Stop();
        try
        {
            var sample = await Task.Run(TakeSample);
            _last = DateTimeOffset.UtcNow; _problem = null;
            foreach (var alert in _rules.Evaluate(sample, _last.Value)) _icon.ShowBalloonTip(15000, TrayText.Title, TrayText.Alert(alert), ToolTipIcon.Warning);
        }
        catch (Exception e) { _problem = e.Message; }
        finally { _status.Text = TrayText.Status(_last, _problem); _checking = false; Schedule(_rules.IsWatching ? Watch : Idle); Memory.Release(); }
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

    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); _icon.Visible = false; _icon.Dispose(); } base.Dispose(disposing); }
}
