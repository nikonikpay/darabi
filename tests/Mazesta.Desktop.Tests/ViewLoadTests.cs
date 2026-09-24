using System.IO; using Microsoft.Extensions.DependencyInjection; using System.Windows; using System.Windows.Controls; using Xunit;
using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views;
namespace Mazesta.Desktop.Tests;

public class ViewLoadTests
{
    private static void OnSta(Func<FrameworkElement> make)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MazestaTest;component/Themes/Dark.xaml") });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MazestaTest;component/Themes/SensorIcons.xaml") });
                var el = make(); var host = new Window { Content = el, Width = 800, Height = 600, ShowInTaskbar = false };
                host.Show(); el.UpdateLayout(); host.Close();
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (error is not null) throw new Exception(error.ToString());
    }

    [Fact] public void SystemInfoView_loads_and_renders_sections()
        => OnSta(() =>
        {
            var vm = new SystemInfoViewModel(new Mazesta.Desktop.Composition.InventoryCache(new Inv(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance), a => { a(); return null!; });
            vm.Loaded.Wait();
            return new SystemInfoView { DataContext = vm };
        });

    [Fact] public void SettingsView_loads_with_the_tray_section()
        => OnSta(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "mazesta-view-" + Guid.NewGuid().ToString("N"));
            var cfg = new Mazesta.Persistence.AppConfig(); var store = new Mazesta.Persistence.JsonStore<Mazesta.Persistence.AppConfig>(Path.Combine(dir, "a.json"), new Mazesta.Persistence.SchemaMigrator(Mazesta.Persistence.AppConfig.Migrations), Mazesta.Persistence.AppConfig.CurrentSchemaVersion, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch); var opts = new Mazesta.Monitoring.MonitoringOptions();
            var e = new Mazesta.Monitoring.PollingEngine(new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(), c, opts, new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var shell = new ShellViewModel(e, new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
            return new SettingsView { DataContext = new SettingsViewModel(cfg, store, Mazesta.Persistence.AppPaths.Create(dir, dir, true), e, opts, shell, _ => { }) };
        });

    private sealed class Inv : Mazesta.Core.Providers.IInventoryProvider
    {
        public Task<Mazesta.Core.Inventory.HardwareInventory> ReadAsync(CancellationToken ct) => Task.FromResult(Mazesta.Core.Inventory.HardwareInventory.Empty);
    }
}
