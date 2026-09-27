using Xunit; using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring.Tests;

public class HardwareDiagnosticsTests
{
    private static HardwareNode Node(HardwareKind kind, string id, params (string Name, SensorRole Role)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, id, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, $"s/{i}"), hid, s.Name, SensorKind.Temperature, Unit.Celsius, s.Role, i))]);
    }

    [Fact] public void A_cpu_without_its_key_readings_is_reported_by_what_it_lacks()
    {
        var cpu = Node(HardwareKind.Cpu, "cpu", ("Package", SensorRole.CpuPackageTemp), ("Total", SensorRole.CpuTotalLoad));
        var problems = HardwareDiagnosticsReport.Problems([cpu], new Dictionary<SensorId, SensorTally>());
        Assert.Contains(problems, p => p.Contains("no clock sensor")); Assert.Contains(problems, p => p.Contains("no package power sensor"));
        Assert.DoesNotContain(problems, p => p.Contains("no temperature sensor"));
    }
    [Fact] public void Unmapped_sensors_and_sensors_that_never_read_are_findings()
    {
        var board = Node(HardwareKind.Motherboard, "board", ("Temp #3", SensorRole.None), ("CPU Socket", SensorRole.BoardTemp));
        var tallies = new Dictionary<SensorId, SensorTally> { [board.Sensors[1].Id] = new(0, 15, DataQuality.Invalid) };
        var problems = HardwareDiagnosticsReport.Problems([board], tallies);
        Assert.Contains(problems, p => p.Contains("1 sensor(s) without a role")); Assert.Contains(problems, p => p.Contains("'CPU Socket'") && p.Contains("never read a good value"));
    }
    [Fact] public void A_fully_covered_machine_has_no_findings_and_the_report_says_so()
    {
        var ram = Node(HardwareKind.Memory, "ram", ("Used", SensorRole.RamUsed));
        Assert.Empty(HardwareDiagnosticsReport.Problems([ram], new Dictionary<SensorId, SensorTally>()));
        var text = HardwareDiagnosticsReport.Build(DateTimeOffset.UnixEpoch, "test", "PC", new ProviderStatus(ProviderState.Ready, 1, null, null), [ram], new Dictionary<SensorId, SensorTally>());
        Assert.Contains("Findings (0)", text); Assert.Contains("none", text); Assert.Contains("[Memory] ram", text); Assert.Contains("not polled", text);
    }
}
