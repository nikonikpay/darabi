using Xunit; using Mazesta.Core.Hardware;
namespace Mazesta.Core.Tests;

public class PowerTotalTests
{
    private static (SensorDefinition, double) W(string name, SensorRole role, double v) =>
        (new SensorDefinition(new SensorId(name), new HardwareId("hw"), name, SensorKind.Power, Unit.Watt, role, 0), v);
    private static (string, HardwareKind, IReadOnlyList<(SensorDefinition, double)>) D(string name, HardwareKind kind, params (SensorDefinition, double)[] r) => (name, kind, r);

    [Fact]
    public void Cpu_counts_by_its_package_not_its_cores_too()
    {
        var t = PowerTotals.Sum([D("CPU", HardwareKind.Cpu, W("CPU Package", SensorRole.CpuPackagePower, 120), W("CPU Core #1", SensorRole.CpuPerCorePower, 20), W("CPU Core #2", SensorRole.CpuPerCorePower, 20)),
            D("RTX", HardwareKind.Gpu, W("GPU Package", SensorRole.GpuPower, 300), W("GPU Core", SensorRole.None, 200))]);
        Assert.Equal(420, t.Measured);
        Assert.Contains(HardwareKind.Motherboard, t.Unmeasured);
    }

    [Fact] public void Ram_with_its_own_reading_is_added() => Assert.Equal(106, PowerTotals.Sum([D("CPU", HardwareKind.Cpu, W("CPU Package", SensorRole.CpuPackagePower, 100)), D("DIMM", HardwareKind.Memory, W("PMIC", SensorRole.None, 6))]).Measured);
    [Fact] public void A_power_supply_reading_is_the_whole_system() => Assert.True(PowerTotals.Sum([D("CPU", HardwareKind.Cpu, W("CPU Package", SensorRole.CpuPackagePower, 100)), D("PSU", HardwareKind.Psu, W("Total watts", SensorRole.None, 380))]) is { Measured: 380, FromPsu: true });
    [Fact] public void Nothing_measured_is_null_not_zero() => Assert.Null(PowerTotals.Sum([D("Board", HardwareKind.Motherboard)]).Measured);
}
