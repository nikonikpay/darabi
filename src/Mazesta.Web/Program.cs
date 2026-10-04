using System.Windows.Forms;
using Mazesta.Core.Tray; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Monitoring; using Mazesta.Persistence;
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
internal sealed class Program : ApplicationContext
{
    private static Mutex? s_single; private static bool s_first, s_released;
    /// <summary>Set by a second start; the running instance waits on it and shows its window.</summary>
    internal const string ActivateEventName = @"Local\Mazesta.Web.Activate";
    /// <summary>The first polls' hardware report (Data/logs/hardware-report.txt) and the log it goes to, for the diagnostics export.</summary>
    internal static HardwareDiagnosticsRecorder? Recorder { get; private set; }
    internal static RollingFileLoggerProvider? LogProvider { get; private set; }
    /// <summary>Started by the update that has just put this release in place (the page says so once).</summary>
    private static bool JustUpdated { get; set; }
    internal static bool TakeJustUpdated() { bool b = JustUpdated; JustUpdated = false; return b; }
    /// <summary>Ends the app the way closing it from its window does (the update hands over to the new release this way).</summary>
    internal static void Shutdown() => Application.Exit();

    private EventWaitHandle? _activate, _toggle, _shown; private RegisteredWaitHandle? _activateWait, _toggleWait;
    private ServiceProvider? _services;
    private AppPaths? _paths; private AppConfig? _config; private JsonStore<AppConfig>? _store; private bool _configCorrupt; private ILogger? _log;
    private MainWindow? _main; private OverlayService? _overlay; private UiDispatcher? _ui;

    [STAThread]
    private static int Main(string[] args)
    {
        // A downloaded release, started by the running app to put itself in place: no window, no services; see AppUpdater.Apply.
        s_single = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out s_first);
        if (args.Length == 3 && args[0] == AppUpdater.ApplyArgument && int.TryParse(args[2], out int pid))
        {
            AppUpdater.Apply(args[1], pid, () => { if (s_first) s_single.ReleaseMutex(); s_single.Dispose(); s_released = true; });
            return 0;
        }
        JustUpdated = args.Contains(AppUpdater.UpdatedArgument);
        bool overlayOnly = args.Contains(OverlaySignals.Argument);
        if (!s_first)
        {
            // The app is running: ask it to come forward (or, from the tray, to show the overlay). Otherwise it is an old copy of the retired WPF
            // edition (same mutex), found by its title.
            if (overlayOnly) { if (EventWaitHandle.TryOpenExisting(OverlaySignals.Toggle, out var toggle)) using (toggle) toggle.Set(); }
            else if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var running)) using (running) running.Set();
            else SingleInstance.ActivateExisting(Loc.Get("App_Title"));
            return 0;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        using var app = new Program();
        try { app.Start(overlayOnly); Application.Run(app); }
        finally { app.End(); }
        return 0;
    }

    private void Start(bool overlayOnly)
    {
        // The UI thread's message-loop context, set now so the dispatcher can post to it from the start (before any window exists).
        var context = new WindowsFormsSynchronizationContext(); SynchronizationContext.SetSynchronizationContext(context);
        _ui = new UiDispatcher(context, Environment.CurrentManagedThreadId); UiDispatcher.Current = _ui;
        var paths = AppPaths.Detect(); paths.EnsureDirectories(); _paths = paths;
        var logProvider = new RollingFileLoggerProvider(paths.LogsDir, "mazesta-web"); LogProvider = logProvider;
        var lf = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Information); b.AddProvider(logProvider); });
        var log = lf.CreateLogger("Web"); _log = log;
        Application.ThreadException += (_, a) => log.LogError(a.Exception, "UI thread exception");
        UiDispatcher.Unhandled += e => log.LogError(e, "Dispatcher exception");
        TaskScheduler.UnobservedTaskException += (_, a) => { log.LogError(a.Exception, "Unobserved task exception"); a.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { log.LogCritical(a.ExceptionObject as Exception, "Unhandled exception"); LogProvider?.Flush(); };
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, log); _store = store;
        var load = store.Load(); var config = load.Value; _config = config; _configCorrupt = load.Outcome == LoadOutcome.Corrupt;
        Loc.SetLanguage(config.Language);
        _services = Bootstrapper.Build(paths, config, store, lf);
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        log.LogInformation("Mazesta Web {Version} on {Os}, {Machine}{Mode}", version, Environment.OSVersion.VersionString, Environment.MachineName, overlayOnly ? " (overlay only, started by the tray)" : "");
        var engine = _services.GetRequiredService<PollingEngine>();
        Recorder = new HardwareDiagnosticsRecorder(engine, paths.LogsDir, $"Mazesta Web {version}", lf.CreateLogger("Hardware"));

        _overlay = _services.GetRequiredService<OverlayService>();
        if (!_overlay.RegisterHotkey()) log.LogWarning("The overlay shortcut {Hotkey} is held by another program", OverlayService.HotkeyText);
        _shown = new EventWaitHandle(false, EventResetMode.ManualReset, OverlaySignals.Shown);
        _overlay.VisibilityChanged += OnOverlayVisibility;
        _toggle = new EventWaitHandle(false, EventResetMode.AutoReset, OverlaySignals.Toggle);
        _toggleWait = ThreadPool.RegisterWaitForSingleObject(_toggle, (_, _) => _ui.BeginInvoke(ToggleFromTray), null, Timeout.Infinite, false);

        if (overlayOnly) ShowOverlayWhenReady(); else ShowMain();
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => _ui.BeginInvoke(ShowMain), null, Timeout.Infinite, false);
        engine.Start();
        if (!overlayOnly && config.TrayWithApp) StartTray(log);
    }

    /// <summary>The tray monitor runs whenever the app does (unless the user turned it off); one already running is left alone.</summary>
    private static void StartTray(ILogger log)
    {
        try
        {
            string exe = Path.Combine(AppContext.BaseDirectory, OverlaySignals.TrayProcess + ".exe");
            if (!File.Exists(exe) || TrayRunning()) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { log.LogInformation("The tray was not started: {Message}", e.Message); }
    }

    /// <summary>The window, brought forward if it is open, or made again (it was closed while the overlay kept the app alive).</summary>
    private void ShowMain()
    {
        if (_main is not null) { _main.BringForward(); return; }
        var window = new MainWindow(_services!, _paths!, _config!, _store!, _configCorrupt, _log!, _ui!);
        _configCorrupt = false;   // said once
        WindowPlacementRestore.Apply(window, _config!.MainWindow);
        window.FormClosed += (_, _) => OnMainClosed(window);
        _main = window; UiDispatcher.Owner = window; window.Show();
    }

    private void OnMainClosed(MainWindow window)
    {
        if (_main != window) return;
        _main = null; UiDispatcher.Owner = null;
        foreach (var w in Application.OpenForms.OfType<ChartWindow>().ToList()) w.Close();   // pop-out charts go with it
        window.Dispose();
        if (_overlay?.IsVisible == true && TrayRunning()) { _log?.LogInformation("Main window closed; the overlay stays while the tray runs"); return; }
        ExitThread();
    }

    private void OnOverlayVisibility(bool visible)
    {
        if (visible) _shown?.Set(); else _shown?.Reset();
        if (!visible && _main is null) _ui?.BeginInvoke(ExitThread);   // it lived on only for the overlay
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
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        int waited = 0;
        timer.Tick += (_, _) =>
        {
            if (engine.Hardware.Count > 0) { timer.Dispose(); _overlay!.SetVisible(true); }
            else if (++waited > 120) { timer.Dispose(); _log?.LogWarning("The overlay was asked for, but no hardware was read in two minutes"); if (_main is null) ExitThread(); }
        };
        timer.Start();
    }

    private static bool TrayRunning()
    {
        var found = System.Diagnostics.Process.GetProcessesByName(OverlaySignals.TrayProcess);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    private void End()
    {
        _activateWait?.Unregister(null); _activate?.Dispose();
        _toggleWait?.Unregister(null); _toggle?.Dispose(); _shown?.Dispose();
        if (_config is not null) _store?.Save(_config);   // the overlay's last state, when the app ended from the tray with no window to save it
        Recorder?.Dispose();
        if (_services is not null) { _services.GetRequiredService<PollingEngine>().Dispose(); _services.Dispose(); }
        LogProvider?.Dispose();
        if (s_first && !s_released) s_single?.ReleaseMutex();
    }
}
