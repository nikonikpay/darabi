using Mazesta.Core.Hardware; using Mazesta.Desktop.ViewModels; using Mazesta.Core.Providers; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;
public class MonitoringViewModelTests
{
    private sealed class NoCharts : IChartWindowService { public List<SensorDefinition> Opened = []; public void Open(SensorDefinition s, HardwareNode n) => Opened.Add(s); }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static (MonitoringViewModel vm, PollingEngine e, FakeSensorProvider p, AppConfig cfg, NoCharts charts, MonitoringFocus focus) Build()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/intelcpu-0", "temperature/0", "clock/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Gpu, "gpu/nvidiagpu-0", "temperature/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        var cfg = new AppConfig { ExpandedGroups = ["gpu/nvidiagpu-0"] }; var charts = new NoCharts(); var focus = new MonitoringFocus();
        return (new MonitoringViewModel(e, focus, cfg, charts, a => { a(); return null!; }), e, p, cfg, charts, focus);
    }
    [Fact] public void Groups_follow_hardware_and_expansion_comes_from_config()
    { var (vm, _, _, _, _, _) = Build(); Assert.Equal(3, vm.Groups.Count); Assert.False(vm.Groups[0].IsExpanded); Assert.True(vm.Groups[1].IsExpanded); }
    [Fact] public void Snapshot_updates_rows_without_changing_expansion()
    {
        var (vm, e, _, _, _, _) = Build(); vm.Groups[0].IsExpanded = true; var s = e.TickOnce()!; vm.ApplySnapshot(s);
        Assert.Equal("42.0", vm.Groups[0].Rows[0].Current); Assert.True(vm.Groups[0].IsExpanded); Assert.True(vm.Groups[1].IsExpanded);
    }
    [Fact] public void Missing_reading_shows_not_available_text_not_zero()
    {
        var (vm, e, p, _, _, _) = Build(); var id = p.Nodes[0].Sensors[0].Id;
        p.OnPoll = r => new PollResult([new SensorReading(id, null, r.Now, DataQuality.Missing, "f")], new Dictionary<HardwareId, NodeStatus> { [p.Nodes[0].Id] = NodeStatus.Healthy(r.Now) });
        vm.ApplySnapshot(e.TickOnce()!); var row = vm.Groups[0].Rows[0];
        Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable"), row.Current); Assert.Equal(DataQuality.Missing, row.Quality); Assert.NotEqual("0", row.Current);
    }
    [Fact] public void Filter_hides_non_matching_rows_and_groups()
    { var (vm, _, _, _, _, _) = Build(); vm.FilterText = "clock"; Assert.True(vm.Groups[0].IsVisible); Assert.False(vm.Groups[0].Rows[0].IsVisible); Assert.True(vm.Groups[0].Rows[1].IsVisible); Assert.False(vm.Groups[1].IsVisible); vm.FilterText = "nvidia"; Assert.True(vm.Groups[1].IsVisible); }
    [Fact] public void Interval_change_goes_to_engine_and_config()
    { var (vm, e, _, cfg, _, _) = Build(); vm.SelectedIntervalSeconds = 5; Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval); Assert.Equal(5, cfg.FastIntervalSeconds); }
    [Fact] public void Expansion_change_is_written_to_config()
    { var (vm, _, _, cfg, _, _) = Build(); vm.Groups[0].IsExpanded = true; Assert.Contains("cpu/intelcpu-0", cfg.ExpandedGroups); vm.Groups[1].IsExpanded = false; Assert.DoesNotContain("gpu/nvidiagpu-0", cfg.ExpandedGroups); }
    [Fact] public void Focus_request_expands_only_requested_kinds_once()
    {
        var (vm, e, _, _, _, focus) = Build(); vm.Groups[0].IsExpanded = true;
        focus.RequestFocus(new HashSet<HardwareKind> { HardwareKind.Gpu, HardwareKind.Storage }, "test");
        Assert.Equal([false, true, true], vm.Groups.Select(g => g.IsExpanded));
        vm.Groups[0].IsExpanded = true; vm.ApplySnapshot(e.TickOnce()!); Assert.True(vm.Groups[0].IsExpanded);   // ticks never reset user choice
    }
    [Fact] public void Open_chart_command_uses_chart_service()
    { var (vm, _, _, _, charts, _) = Build(); vm.OpenChartCommand.Execute(vm.Groups[0].Rows[1]); Assert.Single(charts.Opened); Assert.Equal("clock/0", charts.Opened[0].Name); }
    [Fact] public void Reset_clears_statistics()
    { var (vm, e, p, _, _, _) = Build(); vm.ApplySnapshot(e.TickOnce()!); vm.ResetStatsCommand.Execute(null); Assert.Equal(0, e.Statistics.Get(p.Nodes[0].Sensors[0].Id).Count); }
}
