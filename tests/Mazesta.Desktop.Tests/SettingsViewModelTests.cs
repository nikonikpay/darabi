using System.IO; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;
public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-settings-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private sealed class FakeTray : Mazesta.Desktop.Services.ITrayController
    {
        public Mazesta.Desktop.Services.TrayState State = new(false, false); public int Restarts;
        public Mazesta.Desktop.Services.TrayState Query() => State;
        public string? Enable() { State = new(true, true); return null; }
        public string? Disable() { State = new(false, false); return null; }
        public string? Restart() { Restarts++; return null; }
    }
    private (SettingsViewModel vm, AppConfig cfg, JsonStore<AppConfig> store, PollingEngine e, FakeTray tray) Build()
    {
        var tray = new FakeTray(); var cfg = new AppConfig(); var store = new JsonStore<AppConfig>(Path.Combine(_dir, "appconfig.json"), new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, NullLogger.Instance);
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var opts = new MonitoringOptions(); var e = new PollingEngine(new FakeSensorProvider(), c, opts, new BoundedEventLog(c, NullLogger.Instance));
        return (new SettingsViewModel(cfg, store, AppPaths.Create(_dir), e, opts, _ => { }, tray), cfg, store, e, tray);
    }
    [Fact] public void Persian_digits_are_accepted_for_intervals()
    { var (vm, cfg, _, e, _) = Build(); vm.FastIntervalText = "۵"; vm.StorageIntervalText = "۱۲۰"; vm.SaveCommand.Execute(null); Assert.Equal((5, 120), (cfg.FastIntervalSeconds, cfg.StorageIntervalSeconds)); Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval); }
    [Fact] public void Storage_interval_below_60_is_rejected()
    { var (vm, cfg, _, _, _) = Build(); vm.StorageIntervalText = "5"; vm.SaveCommand.Execute(null); Assert.Equal(900, cfg.StorageIntervalSeconds); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Settings_Invalid_Interval"), vm.Message); }
    [Fact] public void Fast_interval_must_be_an_allowed_value()
    { var (vm, cfg, _, _, _) = Build(); vm.FastIntervalText = "3"; vm.SaveCommand.Execute(null); Assert.Equal(2, cfg.FastIntervalSeconds); }
    [Fact] public void Save_writes_file_and_language_change_shows_restart_note()
    { var (vm, _, store, _, _) = Build(); vm.Language = "en"; vm.ShopName = "فروشگاه"; vm.SaveCommand.Execute(null); var r = store.Load(); Assert.Equal(("en", "فروشگاه"), (r.Value.Language, r.Value.ShopName)); Assert.Contains(Mazesta.Desktop.Localization.Loc.Get("Settings_RestartNote"), vm.Message); }
    [Fact] public async Task Enable_and_disable_follow_what_the_system_reports_not_a_stored_flag()
    {
        var (vm, _, _, _, tray) = Build(); await vm.TrayLoaded;
        Assert.True(vm.CanEnableTray); Assert.False(vm.CanDisableTray);
        await vm.EnableTrayCommand.ExecuteAsync(null); Assert.False(vm.CanEnableTray); Assert.True(vm.CanDisableTray);
        await vm.DisableTrayCommand.ExecuteAsync(null); Assert.True(vm.CanEnableTray); Assert.False(tray.State.Running);
    }
    [Fact] public async Task Changed_tray_intervals_restart_a_running_tray_and_unchanged_ones_do_not()
    {
        var (vm, _, _, _, tray) = Build(); tray.State = new(true, true); await vm.EnableTrayCommand.ExecuteAsync(null);
        vm.SaveCommand.Execute(null); Assert.Equal(0, tray.Restarts);
        vm.TrayIdleText = "15"; vm.SaveCommand.Execute(null); Assert.Equal(1, tray.Restarts);
    }
    [Fact] public void Tray_intervals_out_of_range_are_rejected()
    { var (vm, cfg, _, _, _) = Build(); vm.TrayWatchText = "1"; vm.SaveCommand.Execute(null); Assert.Equal(30, cfg.TrayWatchIntervalSeconds); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Settings_Invalid_Interval"), vm.Message); }
}
