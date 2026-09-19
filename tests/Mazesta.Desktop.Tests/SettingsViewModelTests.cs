using System.IO; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;
public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-settings-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private (SettingsViewModel vm, AppConfig cfg, JsonStore<AppConfig> store, PollingEngine e) Build()
    {
        var cfg = new AppConfig(); var store = new JsonStore<AppConfig>(Path.Combine(_dir, "appconfig.json"), new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, NullLogger.Instance);
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var opts = new MonitoringOptions(); var e = new PollingEngine(new FakeSensorProvider(), c, opts, new BoundedEventLog(c, NullLogger.Instance));
        var shell = new ShellViewModel(e, new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
        return (new SettingsViewModel(cfg, store, AppPaths.Create(_dir, _dir, true), e, opts, shell, _ => { }), cfg, store, e);
    }
    [Fact] public void Persian_digits_are_accepted_for_intervals()
    { var (vm, cfg, _, e) = Build(); vm.FastIntervalText = "۵"; vm.StorageIntervalText = "۱۲۰"; vm.SaveCommand.Execute(null); Assert.Equal((5, 120), (cfg.FastIntervalSeconds, cfg.StorageIntervalSeconds)); Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval); }
    [Fact] public void Storage_interval_below_60_is_rejected()
    { var (vm, cfg, _, _) = Build(); vm.StorageIntervalText = "5"; vm.SaveCommand.Execute(null); Assert.Equal(900, cfg.StorageIntervalSeconds); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Settings_Invalid_Interval"), vm.Message); }
    [Fact] public void Fast_interval_must_be_an_allowed_value()
    { var (vm, cfg, _, _) = Build(); vm.FastIntervalText = "3"; vm.SaveCommand.Execute(null); Assert.Equal(2, cfg.FastIntervalSeconds); }
    [Fact] public void Save_writes_file_and_language_change_shows_restart_note()
    { var (vm, _, store, _) = Build(); vm.Language = "en"; vm.ShopName = "فروشگاه"; vm.SaveCommand.Execute(null); var r = store.Load(); Assert.Equal(("en", "فروشگاه"), (r.Value.Language, r.Value.ShopName)); Assert.Contains(Mazesta.Desktop.Localization.Loc.Get("Settings_RestartNote"), vm.Message); }
}
