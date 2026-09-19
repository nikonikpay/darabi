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
    // Note: ILoggingBuilder.AddProvider(instance) registers the provider as an already-constructed
    // DI instance, and neither the mini ServiceProvider built inside LoggerFactory.Create nor
    // ILoggerFactory.Dispose() end up disposing an instance registered that way (the well-known
    // ".NET DI never disposes instances it didn't create" rule) - confirmed empirically: after a
    // full graceful OnExit, the log file was still 0 bytes because the provider's buffered
    // StreamWriter was never flushed/closed. The provider is therefore constructed directly here
    // so it can be disposed explicitly in OnExit, which is exactly how Mazesta.Persistence.Tests
    // exercises it too (via `using var p = new RollingFileLoggerProvider(...)`).
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
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, startupLog);
        var load = store.Load(); var config = load.Value;
        Loc.SetLanguage(config.Language);
        if (config.RenderMode == "software") System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;   // before the first window exists
        Services = Composition.Bootstrapper.Build(paths, config, store, lf);
        var shell = Services.GetRequiredService<ViewModels.ShellViewModel>();
        if (load.Outcome == LoadOutcome.Corrupt) shell.ShowBanner(Loc.Get("Config_Corrupt"));
        var window = new MainWindow { DataContext = shell };
        Rtl.Apply(window.RootGrid);                       // see Localization/Rtl for why it is the content, not the Window
        Composition.WindowPlacementRestore.Apply(window, config.MainWindow);
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
                LogStartup($"Provider {status.State}");
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
        // Services can still be null here: the exception handlers wired at the top of OnStartup
        // run before Services is assigned, so a throw from pre-DI startup code (config load,
        // logger setup, etc.) can reach Shutdown(1) with Services never having been built. Guard
        // the whole block so that path doesn't turn into a NullReferenceException on the way out.
        if (IsFirstInstance && Services is not null)
        {
            // PollingEngine.Dispose() is idempotent, so it's safe even if something else already
            // disposed it (e.g. a future shutdown path change).
            Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>().Dispose();
            Services.Dispose();
        }
        // Dispose the concrete provider directly (see the comment on _logProvider) so the
        // rolling file logger's buffered StreamWriter is actually flushed and closed - do this
        // last so it also captures whatever the disposals above happened to log. Independent of
        // the Services guard above since it's constructed before Services and may exist even when
        // Services doesn't.
        _logProvider?.Dispose();
        base.OnExit(e);
    }
}
