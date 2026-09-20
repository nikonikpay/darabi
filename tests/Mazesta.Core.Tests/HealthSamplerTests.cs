using Mazesta.Core.Hardware; using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public class HealthSamplerTests
{
    private static readonly DateTimeOffset T = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static (SensorDefinition, SensorReading) S(string id, SensorRole role, double? v, DataQuality q = DataQuality.Ok)
    { var sid = new SensorId(id); return (new(sid, new HardwareId("cpu/0"), id, SensorKind.Temperature, Unit.Celsius, role, 0), new(sid, v, T, q, "t")); }
    private static HealthSample Sample(params (SensorDefinition d, SensorReading r)[] s)
        => HealthSampler.From([new HardwareNode(new HardwareId("cpu/0"), HardwareKind.Cpu, HardwareVendor.Unknown, "CPU", null, true, s.Select(x => x.d).ToList())], s.Select(x => x.r).ToList());

    [Fact] public void The_hottest_cpu_temperature_role_wins() => Assert.Equal(83, Sample(S("a", SensorRole.CpuPackageTemp, 70), S("b", SensorRole.CpuCoreTemp, 83)).CpuTempC);
    [Fact] public void Untrustworthy_readings_are_ignored() => Assert.Null(Sample(S("a", SensorRole.CpuPackageTemp, 200, DataQuality.Invalid)).CpuTempC);
    [Fact] public void Effective_clock_is_preferred_over_the_reported_one() => Assert.Equal(3000, Sample(S("a", SensorRole.CpuCoreClockAverage, 4500), S("b", SensorRole.CpuEffectiveClockAverage, 3000)).CpuClockMhz);
    [Fact] public void No_sensor_means_null_not_zero() { var s = Sample(); Assert.Null(s.CpuTempC); Assert.Null(s.GpuTempC); Assert.Null(s.CpuLoadPercent); Assert.Null(s.CpuClockMhz); }
}
