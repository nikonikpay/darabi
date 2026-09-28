using System.IO;
using Mazesta.Core.Time;
using Mazesta.Core.Providers;
using Mazesta.Diagnostics;
using Mazesta.Hardware.Lhm;
using Mazesta.Hardware.Wmi;
using Mazesta.Monitoring;
using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mazesta.Desktop.Composition;

public static class Bootstrapper
{
    // Note: `lf` is accepted rather than created here so that the app's pre-DI startup logger
    // (used to log config load and the "Window shown"/"Provider ready" timing lines) and the
    // logger factory registered into DI are the SAME RollingFileLoggerProvider instance. Two
    // independent instances writing to the same rolling log file from different threads (the
    // startup thread and the polling engine's background thread) raced for the file handle and
    // crashed the app with an IOException the first time both fired close together.
    public static ServiceProvider Build(AppPaths paths, AppConfig config, JsonStore<AppConfig> store, ILoggerFactory lf)
    {
        var s = new ServiceCollection();
        s.AddSingleton(lf);
        s.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        s.AddSingleton(paths);
        s.AddSingleton(config);
        s.AddSingleton(store);
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<IEventLog>(sp => new BoundedEventLog(sp.GetRequiredService<IClock>(), lf.CreateLogger("Events")));
        s.AddSingleton(new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(config.FastIntervalSeconds), StorageInterval = TimeSpan.FromSeconds(config.StorageIntervalSeconds) });
        s.AddSingleton<ISensorProvider>(sp => LibreHardwareMonitorProvider.CreateDefault(sp.GetRequiredService<IClock>(), lf));
        s.AddSingleton<PollingEngine>();
        s.AddSingleton<MonitoringFocus>();
        s.AddSingleton<IWmiQuery, WmiQuery>();
        s.AddSingleton<IInventoryProvider, WmiInventoryProvider>();
        s.AddSingleton<InventoryCache>();
        s.AddDiagnostics(paths, lf);
        s.AddSingleton<Services.ITrayController, Services.TrayController>();
        s.AddSingleton<Services.ReportService>();
        s.AddSingleton<IFrameRateSource>(_ => new FrameRateMonitor(lf.CreateLogger("FrameRate")));
        s.AddSingleton<Services.OverlayService>();
        AddViewModelFactory(s, sp => new ViewModels.TestCenterViewModel(sp.GetRequiredService<TestEngine>(), sp.GetRequiredService<IEnumerable<ITestExecutor>>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.ReportsViewModel(sp.GetRequiredService<Services.ReportService>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a), path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }), text => System.Windows.MessageBox.Show(text, Localization.Loc.Get("Nav_Reports"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes));
        AddViewModelFactory(s, sp => new ViewModels.BenchmarksViewModel(sp.GetRequiredService<Mazesta.Diagnostics.Benchmarks.BenchmarkRunner>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        Action<string> open = target => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        // One for the session (see WindowsToolsViewModel): a running repair and its output survive leaving the page.
        s.AddSingleton(sp => new ViewModels.WindowsToolsViewModel(sp.GetRequiredService<Mazesta.Diagnostics.Windows.ICommandRunner>(), sp.GetRequiredService<IWmiQuery>(), open, a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.GamingViewModel(sp.GetRequiredService<Mazesta.Diagnostics.Windows.ICommandRunner>(), open, Mazesta.Diagnostics.Windows.GamingStatus.Read()));
        // Opened before the container so a search that never came back (see TuningViewModel.Recover) is undone at start-up, not when the page is first visited.
        var tuningStore = new JsonStore<GpuProfileDocument>(Path.Combine(paths.ConfigDir, "gpu-profiles.json"), new SchemaMigrator([]), GpuProfileDocument.CurrentSchemaVersion, lf.CreateLogger("Tuning"));
        var tuning = new Mazesta.Hardware.Nvidia.NvmlTuningProvider(lf.CreateLogger("Tuning"));
        string? recovered = ViewModels.TuningViewModel.Recover(tuningStore, tuning);
        if (recovered is not null) lf.CreateLogger("Tuning").LogWarning("Interrupted automatic GPU tuning found at start-up: {Message}", recovered);
        s.AddSingleton(new ViewModels.TuningRecovery(recovered));
        s.AddSingleton(sp => new ViewModels.TuningViewModel(tuning, tuningStore, sp.GetRequiredService<InventoryCache>(),
            text => System.Windows.MessageBox.Show(text, Localization.Loc.Get("Nav_Tuning"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes,
            () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /fw /t 0") { UseShellExecute = false, CreateNoWindow = true }),
            a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a), device => new Mazesta.Diagnostics.Gpu.Tuning.ComputeGpuLoad(device.Name), recovered,
            // The core voltage comes from the sensor monitor (NVML has no voltage reading); the reading lives as long as the page's view model, i.e. the session.
            name => LatestReading.Find(sp.GetRequiredService<PollingEngine>(), Mazesta.Core.Hardware.HardwareKind.Gpu, name, Mazesta.Core.Hardware.SensorRole.GpuVoltage) is { } r ? () => r.Value : () => null));
        AddViewModelFactory(s, sp => new ViewModels.SystemInfoViewModel(sp.GetRequiredService<InventoryCache>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.SettingsViewModel(sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<JsonStore<AppConfig>>(), sp.GetRequiredService<AppPaths>(), sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringOptions>(), dir => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }), sp.GetRequiredService<Services.ITrayController>(), sp.GetRequiredService<Services.OverlayService>()));
        var provider = s.BuildServiceProvider();
        provider.GetRequiredService<Services.ReportService>();   // constructed now so it is already listening when the first test run starts
        return provider;
    }

    /// <summary>
    /// Registers a page view model as a <c>Func&lt;T&gt;</c> factory rather than a transient service.
    /// The page view models are IDisposable (they unsubscribe from the polling engine), and a
    /// container tracks every IDisposable transient it creates until the container itself is
    /// disposed - so with AddTransient, every navigation leaked one live view model, still rooted
    /// by the root provider, for the life of the process. Objects the factory news up are never
    /// handed to the container, so nothing but the caller holds them; the page that asked for it disposes the
    /// outgoing page as it navigates.
    /// </summary>
    internal static void AddViewModelFactory<T>(IServiceCollection s, Func<IServiceProvider, T> create) where T : class
        => s.AddSingleton<Func<T>>(sp => () => create(sp));
}
