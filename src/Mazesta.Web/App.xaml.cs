using System.IO; using System.Windows;
using Mazesta.Core.Tray; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Monitoring; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

/// <summary>
/// The web edition's start-up. It composes the services from the app layer (Mazesta.Desktop's Bootstrapper) and shows one window:
/// a WebView2 showing the local web interface. Only one copy runs at a time (a single-instance mutex): two would drive the same hardware. A second
/// start (a double-click, or the tray's "open") brings the running window forward - or opens it again - instead of complaining.
/// Closing the window ends the process, with one exception: while the tray runs and the overlay is on screen, the process stays for the overlay
/// alone (no window, no web page), until the overlay is hidden - from its shortcut or the tray's menu - or the window is opened again. The tray
/// can also start the app for the overlay alone (<see cref="OverlaySignals.Argument"/>).
/// </summary>
public partial class App : Application
{
    private static readonly Mutex SingleInstance;
    private static readonly bool IsFirstInstance;
    /// <summary>Set by a second start; the running instance waits on it and shows its window.</summary>
    internal const string ActivateEventName = @"Local\Mazesta.Web.Activate";
    static App() { SingleInstance = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out bool created); IsFirstInstance = created; }
    private EventWaitHandle? _activate, _toggle, _shown; private RegisteredWaitHandle? _activateWait, _toggleWait;
    private RollingFileLoggerProvider? _logProvider;
    /// <summary>The first polls' hardware report (Data/logs/hardware-report.txt) and the log it goes to, for the diagnostics export.</summary>
    internal static HardwareDiagnosticsRecorder? Recorder { get; private set; }
    internal static RollingFileLoggerProvider? LogProvider { get; private set; }
    private ServiceProvider? _services;
    private AppPaths? _paths; private AppConfig? _config; private JsonStore<AppConfig>? _store; private bool _configCorrupt; private ILogger? _log;
    private MainWindow? _main; private OverlayService? _overlay;

    /// <summary>Started by the update that has just put this release in place (the page says so once).</summary>
    private static bool JustUpdated { get; set; }
    internal static bool TakeJustUpdated() { bool b = JustUpdated; JustUpdated = false; return b; }
    private static bool s_released;

    protected override void OnStartup(StartupEventArgs e)
    {
        // A downloaded release, started by the running app to put itself in place: no window, no services; see AppUpdater.Apply.
        if (e.Args.Length == 3 && e.Args[0] == AppUpdater.ApplyArgument && int.TryParse(e.Args[2], out int pid))
        {
            AppUpdater.Apply(e.Args[1], pid, () => { if (IsFirstInstance) SingleInstance.ReleaseMutex(); SingleInstance.Dispose(); s_released = true; });
            Shutdown(); return;
        }
        JustUpdated = e.Args.Contains(AppUpdater.UpdatedArgument);
        bool overlayOnly = e.Args.Contains(OverlaySignals.Argument);
        if (!IsFirstInstance)
        {
            // The app is running: ask it to come forward (or, from the tray, to show the overlay). Otherwise it is an old copy of the retired WPF
            // edition (same mutex), found by its title.
            if (overlayOnly) { if (EventWaitHandle.TryOpenExisting(OverlaySignals.Toggle, out var toggle)) using (toggle) toggle.Set(); }
            else if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var running)) using (running) running.Set();
            else Desktop.Composition.SingleInstance.ActivateExisting(Loc.Get("App_Title"));
            Shutdown(); return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var paths = AppPaths.Detect(); paths.EnsureDirectories(); _paths = paths;
        _logProvider = new RollingFileLoggerProvider(paths.LogsDir, "mazesta-web"); LogProvider = _logProvider;
        var lf = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Information); b.AddProvider(_logProvider); });
        var log = lf.CreateLogger("Web"); _log = log;
        DispatcherUnhandledException += (_, a) => { log.LogError(a.Exception, "Dispatcher exception"); a.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, a) => { log.LogError(a.Exception, "Unobserved task exception"); a.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { log.LogCritical(a.ExceptionObject as Exception, "Unhandled exception"); _logProvider?.Flush(); };
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, log); _store = store;
        var load = store.Load(); var config = load.Value; _config = config; _configCorrupt = load.Outcome == LoadOutcome.Corrupt;
        Loc.SetLanguage(config.Language);
        // The window's own loading panel is WPF: on a machine whose WPF hardware drawing is broken it would be white without this.
        if (config.RenderMode == "software") System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _services = Desktop.Composition.Bootstrapper.Build(paths, config, store, lf);
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        log.LogInformation("Mazesta Web {Version} on {Os}, {Machine}{Mode}", version, Environment.OSVersion.VersionString, Environment.MachineName, overlayOnly ? " (overlay only, started by the tray)" : "");
        var engine = _services.GetRequiredService<PollingEngine>();
        Recorder = new HardwareDiagnosticsRecorder(engine, paths.LogsDir, $"Mazesta Web {version}", lf.CreateLogger("Hardware"));

        _overlay = _services.GetRequiredService<OverlayService>();
        if (!_overlay.RegisterHotkey()) log.LogWarning("The overlay shortcut {Hotkey} is held by another program", OverlayService.HotkeyText);
        _shown = new EventWaitHandle(false, EventResetMode.ManualReset, OverlaySignals.Shown);
        _overlay.VisibilityChanged += OnOverlayVisibility;
        _toggle = new EventWaitHandle(false, EventResetMode.AutoReset, OverlaySignals.Toggle);
        _toggleWait = ThreadPool.RegisterWaitForSingleObject(_toggle, (_, _) => Dispatcher.BeginInvoke(ToggleFromTray), null, Timeout.Infinite, false);

        if (overlayOnly) ShowOverlayWhenReady(); else ShowMain();
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, false);
        engine.Start();
        base.OnStartup(e);
    }

    /// <summary>The window, brought forward if it is open, or made again (it was closed while the overlay kept the app alive).</summary>
    private void ShowMain()
    {
        if (_main is not null) { _main.BringForward(); return; }
        var window = new MainWindow(_services!, _paths!, _config!, _store!, _configCorrupt, _log!);
        _configCorrupt = false;   // said once
        Desktop.Composition.WindowPlacementRestore.Apply(window, _config!.MainWindow);
        window.Closed += (_, _) => OnMainClosed(window);
        _main = window; MainWindow = window; window.Show();
    }

    private void OnMainClosed(MainWindow window)
    {
        if (_main != window) return;
        _main = null; MainWindow = null;
        foreach (var w in Windows.OfType<Window>().Where(w => w is not Desktop.Views.OverlayWindow).ToList()) w.Close();   // pop-out charts go with it
        if (_overlay?.IsVisible == true && TrayRunning()) { _log?.LogInformation("Main window closed; the overlay stays while the tray runs"); return; }
        Shutdown();
    }

    private void OnOverlayVisibility(bool visible)
    {
        if (visible) _shown?.Set(); else _shown?.Reset();
        if (!visible && _main is null) Dispatcher.BeginInvoke(() => Shutdown());   // it lived on only for the overlay
    }

    private void ToggleFromTray()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.SetVisible(false); else ShowOverlayWhenReady();
    }

    /// <summary>The overlay needs the hardware list; right after start-up the first scan takes a few seconds, so it is shown when that is in.</summary>
    private void ShowOverlayWhenReady()
    {
        var engine = _services!.GetRequiredService<PollingEngine>();
        if (engine.Hardware.Count > 0) { _overlay!.SetVisible(true); return; }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int waited = 0;
        timer.Tick += (_, _) =>
        {
            if (engine.Hardware.Count > 0) { timer.Stop(); _overlay!.SetVisible(true); }
            else if (++waited > 120) { timer.Stop(); _log?.LogWarning("The overlay was asked for, but no hardware was read in two minutes"); if (_main is null) Shutdown(); }
        };
        timer.Start();
    }

    private static bool TrayRunning()
    {
        var found = System.Diagnostics.Process.GetProcessesByName(OverlaySignals.TrayProcess);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null); _activate?.Dispose();
        _toggleWait?.Unregister(null); _toggle?.Dispose(); _shown?.Dispose();
        if (_config is not null) _store?.Save(_config);   // the overlay's last state, when the app ended from the tray with no window to save it
        Recorder?.Dispose();
        if (_services is not null) { _services.GetRequiredService<PollingEngine>().Dispose(); _services.Dispose(); }
        _logProvider?.Dispose();
        if (IsFirstInstance && !s_released) SingleInstance.ReleaseMutex();
        base.OnExit(e);
    }
}
