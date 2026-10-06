using Forms = System.Windows.Forms;
using Mazesta.Core.Health; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Monitoring;
using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// Problems told as Windows notifications, so they are seen while the app is behind a game or minimised: a part running too hot or a CPU
/// throttling under load (the tray's own rules, <see cref="HealthAlerts"/>, on the monitor's snapshots), a test that failed, and the sensor
/// reader failing. While the window is in front the page shows the same words as a toast instead. The notification icon exists only for
/// the moments a notification is up (Windows needs one to show it), so the app does not add a second permanent icon next to the tray monitor's.
/// Each problem is announced once until it clears (the rules' own hysteresis and repeat interval; a failed test once per run).
/// </summary>
public sealed class Notifier : IDisposable
{
    private readonly MainWindow _window; private readonly PollingEngine _engine; private readonly TestEngine _tests; private readonly IReadOnlyList<ITestExecutor> _executors;
    private readonly Action<string, string> _toast; private readonly ILogger _log;
    private HealthAlerts _alerts = new(); private (int Cpu, int Gpu) _limits = (95, 95);
    /// <summary>The user's thresholds (the tray's settings, also set by the assistant); read at each snapshot, so a change applies at once.</summary>
    public Func<(int Cpu, int Gpu)>? Limits { get; set; }
    private Forms.NotifyIcon? _icon; private Forms.Timer? _hide;
    private ProviderState _lastProvider = ProviderState.Ready;

    public Notifier(MainWindow window, PollingEngine engine, TestEngine tests, IEnumerable<ITestExecutor> executors, Action<string, string> toast, ILogger log)
    {
        _window = window; _engine = engine; _tests = tests; _executors = [.. executors]; _toast = toast; _log = log;
        engine.SnapshotPublished += OnSnapshot; engine.Provider.StatusChanged += OnProvider; tests.TestCompleted += OnTest;
    }

    private void OnSnapshot(SensorSnapshot s)
    {
        if (Limits?.Invoke() is { } l && l != _limits) { _limits = l; _alerts = new(3, l.Cpu, l.Gpu); }
        foreach (var a in _alerts.Evaluate(HealthSampler.From(_engine.Hardware, s.Readings), s.Timestamp))
            Tell(Loc.Get("Notify_Health_Title"), a.Kind switch
            {
                HealthAlertKind.CpuOverheat => Loc.Format("Notify_CpuOverheat", Math.Round(a.Value)),
                HealthAlertKind.GpuOverheat => Loc.Format("Notify_GpuOverheat", Math.Round(a.Value)),
                _ => Loc.Format("Notify_CpuThrottle", Math.Round(a.Value)),
            }, error: true);
    }

    private void OnProvider(ProviderStatus s)
    {
        if (s.State == _lastProvider) return;
        _lastProvider = s.State;
        if (s.State == ProviderState.Failed) Tell(Loc.Get("Notify_Sensors_Title"), Loc.Get("Notify_ProviderFailed"), error: true);
    }

    private void OnTest(TestId id, TestRunResult r)
    {
        if (r.Outcome != TestOutcome.Failed) return;
        string name = _executors.FirstOrDefault(e => e.Definition.Id == id)?.Definition.NameKey is { } key ? Loc.Get(key) : id.Value;
        Tell(Loc.Get("Notify_Test_Title"), Loc.Format("Notify_TestFailed", name), error: true);
    }

    /// <summary>A notification when the window is not in front, a page toast when it is. Always on the UI thread.</summary>
    private void Tell(string title, string text, bool error)
    {
        _log.LogWarning("Notified: {Title}: {Text}", title, text);
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_window.InFront) { _toast(text, error ? "fail" : ""); return; }
            try
            {
                _icon ??= new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Text = "Mazesta" };
                _icon.BalloonTipClicked -= OnClicked; _icon.BalloonTipClicked += OnClicked;
                _icon.Visible = true;
                _icon.ShowBalloonTip(10_000, title, text, error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
                _hide ??= new Forms.Timer { Interval = 20_000 };
                _hide.Tick -= OnHide; _hide.Tick += OnHide; _hide.Stop(); _hide.Start();
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) { _log.LogWarning(e, "Notification not shown"); }
        });
    }

    private void OnClicked(object? sender, EventArgs e) => _window.BringForward();
    private void OnHide(object? sender, EventArgs e) { _hide?.Stop(); if (_icon is not null) _icon.Visible = false; }   // Windows keeps the notification in its centre

    public void Dispose()
    {
        _engine.SnapshotPublished -= OnSnapshot; _engine.Provider.StatusChanged -= OnProvider; _tests.TestCompleted -= OnTest;
        _hide?.Stop();
        _hide?.Dispose();
        if (_icon is not null) { _icon.Visible = false; _icon.Dispose(); }
    }
}
