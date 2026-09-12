using Mazesta.Monitoring; using Xunit;
namespace Mazesta.Desktop.Tests;
public class ChartWindowViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    [Fact] public void Uses_minute_tier_when_window_exceeds_raw_coverage()
    {
        var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(T0); var p = new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(); p.Nodes.Add(Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider.Node(Mazesta.Core.Hardware.HardwareKind.Cpu, "cpu/x", "temperature/0"));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks(); e.TickOnce();
        var vm = new Mazesta.Desktop.ViewModels.ChartWindowViewModel(e, p.Nodes[0].Sensors[0], p.Nodes[0], a => { a(); return null!; });
        vm.WindowMinutes = 10; vm.Refresh(); Assert.False(vm.UseMinutes);       // 10 min × 2 s = 300 points ≤ 900 raw
        vm.WindowMinutes = 360; vm.Refresh(); Assert.True(vm.UseMinutes);       // 6 h > raw coverage
        Assert.Equal("42.0", vm.CurrentText);
    }
}
