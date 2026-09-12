using Mazesta.Core.Time;
using Mazesta.Hardware;
using Mazesta.Hardware.Lhm;
using Mazesta.Hardware.Wmi;
using Mazesta.Monitoring;
using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mazesta.Desktop.Composition;

public static class Bootstrapper
{
    public static ServiceProvider Build(AppPaths paths, AppConfig config, JsonStore<AppConfig> store)
    {
        var s = new ServiceCollection();
        var lf = LoggingSetup.CreateFactory(paths.LogsDir);
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
        s.AddSingleton<ViewModels.ShellViewModel>();
        s.AddSingleton<ViewModels.IChartWindowService, Services.ChartWindowService>();
        s.AddTransient<ViewModels.MonitoringViewModel>(sp => new ViewModels.MonitoringViewModel(sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringFocus>(), sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<ViewModels.IChartWindowService>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));
        // Tasks 19-20 add: DashboardViewModel, SettingsViewModel
        return s.BuildServiceProvider();
    }
}
