using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Desktop.Tests;

public class CustomerSummaryTemperatureTests
{
    private static HardwareNode Node(HardwareKind kind, string id, params (string Path, SensorRole Role)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, id, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s.Path), hid, s.Path, SensorKind.Temperature, Unit.Celsius, s.Role, i))]);
    }

    [Fact] public void Cpu_and_gpu_temperatures_come_with_the_newest_reading_and_the_highest_since_the_monitor_started()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch); var p = new FakeSensorProvider(); double temp = 70;
        p.Nodes.Add(Node(HardwareKind.Cpu, "cpu/0", ("temperature/package", SensorRole.CpuPackageTemp)));
        p.Nodes.Add(Node(HardwareKind.Gpu, "gpu/0", ("temperature/core", SensorRole.GpuCoreTemp)));   // no hot spot sensor: that part is left out
        p.OnPoll = r => new([.. p.Nodes.SelectMany(n => n.Sensors).Select(s => new SensorReading(s.Id, temp, r.Now, DataQuality.Ok, "fake"))], p.Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
        var e = new PollingEngine(p, clock, new MonitoringOptions(), new BoundedEventLog(clock, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
        e.TickOnce(); clock.Advance(TimeSpan.FromSeconds(2)); temp = 45; e.TickOnce();
        var t = CustomerSummaryBuilder.Temperatures(e, clock.UtcNow);
        Assert.Equal(["CPU", "GPU"], t.Select(x => x.Part)); Assert.All(t, x => { Assert.Equal(45, x.CurrentC); Assert.Equal(70, x.MaxC); });
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.All(CustomerSummaryBuilder.Temperatures(e, clock.UtcNow), x => Assert.Null(x.CurrentC));   // an old reading is not "now"
    }
}
