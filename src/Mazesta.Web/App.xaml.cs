using System.IO; using System.Windows;
using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

/// <summary>
/// The web edition's start-up. It composes exactly the services the WPF edition does (Desktop's Bootstrapper) and only swaps the window: one
/// WebView2 showing the local web interface. Only one edition runs at a time (same single-instance mutex): both drive the same hardware.
/// </summary>
public partial class App : Application
{
    private static readonly Mutex SingleInstance;
    private static readonly bool IsFirstInstance;
    static App() { SingleInstance = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out bool created); IsFirstInstance = created; }
    private RollingFileLoggerProvider? _logProvider;
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsFirstInstance)
        {
            MessageBox.Show(Loc.Get("Web_AlreadyRunning"), "Mazesta", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        var paths = AppPaths.Detect(); paths.EnsureDirectories();
        _logProvider = new RollingFileLoggerProvider(paths.LogsDir);
        var lf = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Information); b.AddProvider(_logProvider); });
        var log = lf.CreateLogger("Web");
        DispatcherUnhandledException += (_, a) => { log.LogError(a.Exception, "Dispatcher exception"); a.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, a) => { log.LogError(a.Exception, "Unobserved task exception"); a.SetObserved(); };
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, log);
        var load = store.Load(); var config = load.Value;
        Loc.SetLanguage(config.Language);
        _services = Desktop.Composition.Bootstrapper.Build(paths, config, store, lf);
        var window = new MainWindow(_services, paths, config, store, load.Outcome == LoadOutcome.Corrupt, log);
        Desktop.Composition.WindowPlacementRestore.Apply(window, config.MainWindow);
        MainWindow = window; window.Show();
        _services.GetRequiredService<PollingEngine>().Start();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null) { _services.GetRequiredService<PollingEngine>().Dispose(); _services.Dispose(); }
        _logProvider?.Dispose();
        if (IsFirstInstance) SingleInstance.ReleaseMutex();
        base.OnExit(e);
    }
}
