using System.IO; using System.Windows;
using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

/// <summary>
/// The web edition's start-up. It composes exactly the services the WPF edition does (Desktop's Bootstrapper) and only swaps the window: one
/// WebView2 showing the local web interface. Only one edition runs at a time (same single-instance mutex): both drive the same hardware. A second
/// start (a double-click, or the tray's "open") brings the running window forward instead of complaining. Closing the window ends the process:
/// the overlay and any other window the app made never keep it alive in the background, where it would block the next start.
/// </summary>
public partial class App : Application
{
    private static readonly Mutex SingleInstance;
    private static readonly bool IsFirstInstance;
    /// <summary>Set by a second start; the running instance waits on it and shows its window.</summary>
    internal const string ActivateEventName = @"Local\Mazesta.Web.Activate";
    static App() { SingleInstance = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out bool created); IsFirstInstance = created; }
    private EventWaitHandle? _activate; private RegisteredWaitHandle? _activateWait;
    private RollingFileLoggerProvider? _logProvider;
    /// <summary>The first polls' hardware report (Data/logs/hardware-report.txt) and the log it goes to, for the diagnostics export.</summary>
    internal static HardwareDiagnosticsRecorder? Recorder { get; private set; }
    internal static RollingFileLoggerProvider? LogProvider { get; private set; }
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsFirstInstance)
        {
            // The web edition is running: ask it to come forward. Otherwise it is the WPF edition, found by its title.
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var running)) using (running) running.Set();
            else Desktop.Composition.SingleInstance.ActivateExisting(Loc.Get("App_Title"));
            Shutdown(); return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var paths = AppPaths.Detect(); paths.EnsureDirectories();
        _logProvider = new RollingFileLoggerProvider(paths.LogsDir, "mazesta-web"); LogProvider = _logProvider;
        var lf = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Information); b.AddProvider(_logProvider); });
        var log = lf.CreateLogger("Web");
        DispatcherUnhandledException += (_, a) => { log.LogError(a.Exception, "Dispatcher exception"); a.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, a) => { log.LogError(a.Exception, "Unobserved task exception"); a.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { log.LogCritical(a.ExceptionObject as Exception, "Unhandled exception"); _logProvider?.Flush(); };
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, log);
        var load = store.Load(); var config = load.Value;
        Loc.SetLanguage(config.Language);
        // The window's own loading panel is WPF: on a machine whose WPF hardware drawing is broken it would be white without this.
        if (config.RenderMode == "software") System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _services = Desktop.Composition.Bootstrapper.Build(paths, config, store, lf);
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        log.LogInformation("Mazesta Web {Version} on {Os}, {Machine}", version, Environment.OSVersion.VersionString, Environment.MachineName);
        Recorder = new HardwareDiagnosticsRecorder(_services.GetRequiredService<PollingEngine>(), paths.LogsDir, $"Mazesta Web {version}", lf.CreateLogger("Hardware"));
        var window = new MainWindow(_services, paths, config, store, load.Outcome == LoadOutcome.Corrupt, log);
        Desktop.Composition.WindowPlacementRestore.Apply(window, config.MainWindow);
        MainWindow = window; window.Show();
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(window.BringForward), null, Timeout.Infinite, false);
        _services.GetRequiredService<PollingEngine>().Start();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null); _activate?.Dispose();
        Recorder?.Dispose();
        if (_services is not null) { _services.GetRequiredService<PollingEngine>().Dispose(); _services.Dispose(); }
        _logProvider?.Dispose();
        if (IsFirstInstance) SingleInstance.ReleaseMutex();
        base.OnExit(e);
    }
}
