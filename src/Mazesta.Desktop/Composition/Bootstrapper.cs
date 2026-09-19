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
        s.AddSingleton<ViewModels.ShellViewModel>();
        s.AddSingleton<ViewModels.IChartWindowService, Services.ChartWindowService>();
        s.AddDiagnostics(paths, lf);
        AddViewModelFactory(s, sp => new ViewModels.MonitoringViewModel(sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringFocus>(), sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<ViewModels.IChartWindowService>(), sp.GetRequiredService<IClock>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.DashboardViewModel(sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<InventoryCache>(), sp.GetRequiredService<AppConfig>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.TestCenterViewModel(sp.GetRequiredService<TestEngine>(), sp.GetRequiredService<IEnumerable<ITestExecutor>>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        AddViewModelFactory(s, sp => new ViewModels.SettingsViewModel(sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<JsonStore<AppConfig>>(), sp.GetRequiredService<AppPaths>(), sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringOptions>(), sp.GetRequiredService<ViewModels.ShellViewModel>(), dir => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true })));
        return s.BuildServiceProvider();
    }

    /// <summary>
    /// Registers a page view model as a <c>Func&lt;T&gt;</c> factory rather than a transient service.
    /// The page view models are IDisposable (they unsubscribe from the polling engine), and a
    /// container tracks every IDisposable transient it creates until the container itself is
    /// disposed - so with AddTransient, every navigation leaked one live view model, still rooted
    /// by the root provider, for the life of the process. Objects the factory news up are never
    /// handed to the container, so nothing but the caller holds them; ShellViewModel disposes the
    /// outgoing page as it navigates.
    /// </summary>
    internal static void AddViewModelFactory<T>(IServiceCollection s, Func<IServiceProvider, T> create) where T : class
        => s.AddSingleton<Func<T>>(sp => () => create(sp));
}
