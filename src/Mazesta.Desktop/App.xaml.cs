using System.Windows;
using Mazesta.Desktop.Localization;
using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mazesta.Desktop;

public partial class App : Application
{
    // Note: a single field-initializer expression cannot share an `out var` local with a
    // sibling field's initializer (each initializer is its own scope), so the mutex creation
    // and the resulting flag are grouped into a static constructor instead of two field
    // initializers as sketched in the brief. Behavior is identical.
    private static readonly Mutex SingleInstance;
    public static bool IsFirstInstance;
    public static ServiceProvider Services { get; private set; } = null!;
    public static System.Diagnostics.Stopwatch StartupClock { get; } = System.Diagnostics.Stopwatch.StartNew();
    // Note: LoggingSetup.CreateFactory(...) wraps a freshly-constructed RollingFileLoggerProvider
    // inside an ILoggerFactory via ILoggingBuilder.AddProvider(instance). That registers the
    // provider as an already-constructed DI instance, and neither the mini ServiceProvider built
    // inside LoggerFactory.Create nor ILoggerFactory.Dispose() end up disposing an instance
    // registered that way (the well-known ".NET DI never disposes instances it didn't create"
    // rule) - confirmed empirically: after a full graceful OnExit, the log file was still 0 bytes
    // because the provider's buffered StreamWriter was never flushed/closed. The provider itself
    // is constructed directly here instead so it can be disposed explicitly in OnExit, which is
    // exactly how Mazesta.Persistence.Tests exercises it too (via `using var p = new
    // RollingFileLoggerProvider(...)`).
    private static RollingFileLoggerProvider? _logProvider;

    static App()
    {
        SingleInstance = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out var createdNew);
        IsFirstInstance = createdNew;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // App_Title is identical in both languages, so the English default culture (before any
        // config is read) already yields the right window title to search for.
        if (!IsFirstInstance) { Composition.SingleInstance.ActivateExisting(Loc.Get("App_Title")); Shutdown(); return; }
        DispatcherUnhandledException += (_, e) =>
        {
            Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogError(e.Exception, "Dispatcher exception");
            var r = MessageBox.Show(Loc.Get("Crash_Body"), Loc.Get("Crash_Title"), MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes, Loc.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
            e.Handled = r == MessageBoxResult.Yes; if (!e.Handled) Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogCritical(e.ExceptionObject as Exception, "AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, e) => { Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogError(e.Exception, "Unobserved task exception"); e.SetObserved(); };
        var paths = AppPaths.Detect(); paths.EnsureDirectories();
        _logProvider = new RollingFileLoggerProvider(paths.LogsDir);
        var lf = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Information); b.AddProvider(_logProvider); });
        var startupLog = lf.CreateLogger("Startup");
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator([new Migration0To1()]), AppConfig.CurrentSchemaVersion, startupLog);
        var load = store.Load(); var config = load.Value;
        Loc.SetLanguage(config.Language);
        Services = Composition.Bootstrapper.Build(paths, config, store, lf);
        var shell = Services.GetRequiredService<ViewModels.ShellViewModel>();
        if (load.Outcome == LoadOutcome.Corrupt) shell.ShowBanner(Loc.Get("Config_Corrupt"));
        // Note: FlowDirection is applied to the window's root content (RootGrid), not the Window
        // itself. Setting FlowDirection on the Window element flips the underlying HWND
        // (WS_EX_LAYOUTRTL), which mirrors the native title bar and, on this rendering path, the
        // glyphs themselves (letters render as literal mirror images). Applying it to the content
        // instead keeps RTL mirroring entirely inside WPF's own visual tree, which renders text
        // correctly while still flipping sidebar/content layout for RTL languages.
        var window = new MainWindow { DataContext = shell };
        window.RootGrid.FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        MainWindow = window; window.Show();
        startupLog.LogInformation("Window shown at {Ms} ms", StartupClock.ElapsedMilliseconds);
        var engine = Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>();
        var charts = (Mazesta.Desktop.Services.ChartWindowService)Services.GetRequiredService<ViewModels.IChartWindowService>();
        // Note: PollingEngine.Start() begins the hardware scan on its own background thread, so
        // engine.Hardware is still empty right after this call returns. Pages such as Dashboard
        // build their card layout once, synchronously, from engine.Hardware at construction time
        // (matching how their tests pre-populate hardware before constructing them), so the
        // initial nav selection is deferred until the provider reports a settled status here
        // instead of happening immediately after Show(). Otherwise the very first page would be
        // built against an empty hardware list and would never pick up the real sensors.
        void OnProviderStatus(Mazesta.Core.Hardware.ProviderStatus status)
        {
            if (status.State is Mazesta.Core.Hardware.ProviderState.Ready or Mazesta.Core.Hardware.ProviderState.Degraded or Mazesta.Core.Hardware.ProviderState.Failed)
            {
                engine.Provider.StatusChanged -= OnProviderStatus;
                LogStartup("Provider ready");
                Dispatcher.BeginInvoke(() => { shell.Selected ??= shell.Items[0]; charts.RestoreFromConfig(); });
            }
        }
        engine.Provider.StatusChanged += OnProviderStatus;
        engine.Start();
        base.OnStartup(e);
    }

    public static void LogStartup(string what)
    {
        if (Services is null) return;
        Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation("{What} at {Ms} ms", what, StartupClock.ElapsedMilliseconds);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (IsFirstInstance)
        {
            Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>().Dispose();
            Services.Dispose();
            // Dispose the concrete provider directly (see the comment on _logProvider) so the
            // rolling file logger's buffered StreamWriter is actually flushed and closed - do this
            // last so it also captures whatever the disposals above happened to log.
            _logProvider?.Dispose();
        }
        base.OnExit(e);
    }
}
