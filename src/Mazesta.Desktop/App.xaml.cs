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

    static App()
    {
        SingleInstance = new Mutex(true, @"Global\Mazesta.Test.SingleInstance", out var createdNew);
        IsFirstInstance = createdNew;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsFirstInstance) { Shutdown(); return; }
        var paths = AppPaths.Detect(); paths.EnsureDirectories();
        var lf = LoggingSetup.CreateFactory(paths.LogsDir); var startupLog = lf.CreateLogger("Startup");
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
        if (IsFirstInstance) { Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>().Dispose(); Services.Dispose(); }
        base.OnExit(e);
    }
}
