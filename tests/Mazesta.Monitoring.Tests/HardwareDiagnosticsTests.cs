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
    [Fact] public void Virtual_memory_is_not_expected_to_have_ram_readings()
        => Assert.Empty(HardwareDiagnosticsReport.Problems([Node(HardwareKind.Memory, "Virtual Memory")], new Dictionary<SensorId, SensorTally>()));
}

public class HardwareDiagnosticsNotesTests
{
    private static HardwareNode Node(HardwareKind kind, string id, params (string Name, SensorRole Role)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, id, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, $"s/{i}"), hid, s.Name, SensorKind.Temperature, Unit.Celsius, s.Role, i))]);
    }

    [Fact] public void A_dimm_is_a_module_not_the_memory_pool_and_its_unprogrammed_limits_are_not_faults()
    {
        var dimm = Node(HardwareKind.Memory, "DIMM #2", ("DIMM #2", SensorRole.DimmTemp), ("Thermal Sensor High Limit", SensorRole.DimmSpec));
        var tallies = new Dictionary<SensorId, SensorTally> { [dimm.Sensors[1].Id] = new(0, 15, DataQuality.Invalid, 0) };
        Assert.Empty(HardwareDiagnosticsReport.Problems([dimm], tallies)); Assert.Empty(HardwareDiagnosticsReport.Notes([dimm], tallies));
    }
    [Fact] public void A_sensor_stuck_at_zero_is_a_note_about_the_device_not_a_finding()
    {
        var usb = Node(HardwareKind.Storage, "T7", ("Composite Temperature", SensorRole.StorageTemp), ("Used Space", SensorRole.StorageUsedSpace));
        var tallies = new Dictionary<SensorId, SensorTally> { [usb.Sensors[0].Id] = new(0, 15, DataQuality.Invalid, 0) };
        Assert.Empty(HardwareDiagnosticsReport.Problems([usb], tallies));
        Assert.Contains("reads 0 all the time", Assert.Single(HardwareDiagnosticsReport.Notes([usb], tallies)));
    }
    [Fact] public void Utilization_of_an_adapter_without_traffic_is_a_note_with_traffic_a_finding()
    {
        var nic = Node(HardwareKind.Network, "Ethernet", ("Upload Speed", SensorRole.NetUpload), ("Download Speed", SensorRole.NetDownload), ("Network Utilization", SensorRole.NetUtilization));
        var idle = new Dictionary<SensorId, SensorTally> { [nic.Sensors[0].Id] = new(15, 0, DataQuality.Ok, 0), [nic.Sensors[1].Id] = new(15, 0, DataQuality.Ok, 0), [nic.Sensors[2].Id] = new(0, 15, DataQuality.Invalid) };
        Assert.Empty(HardwareDiagnosticsReport.Problems([nic], idle)); Assert.Single(HardwareDiagnosticsReport.Notes([nic], idle));
        var busy = new Dictionary<SensorId, SensorTally>(idle) { [nic.Sensors[1].Id] = new(15, 0, DataQuality.Ok, 1e6) };
        Assert.Contains(HardwareDiagnosticsReport.Problems([nic], busy), p => p.Contains("never read a good value")); Assert.Empty(HardwareDiagnosticsReport.Notes([nic], busy));
    }
    [Fact] public void A_failing_device_is_always_a_finding()
    {
        var usb = Node(HardwareKind.Storage, "T7", ("Composite Temperature", SensorRole.StorageTemp), ("Used Space", SensorRole.StorageUsedSpace));
        var tallies = new Dictionary<SensorId, SensorTally> { [usb.Sensors[0].Id] = new(0, 15, DataQuality.Stale, 0) };
        Assert.Single(HardwareDiagnosticsReport.Problems([usb], tallies)); Assert.Empty(HardwareDiagnosticsReport.Notes([usb], tallies));
    }
    [Fact] public void The_report_lists_the_notes_apart_and_unmapped_sensors_by_name()
    {
        var board = Node(HardwareKind.Motherboard, "board", ("Temp #3", SensorRole.None), ("Zero", SensorRole.BoardTemp));
        var tallies = new Dictionary<SensorId, SensorTally> { [board.Sensors[1].Id] = new(0, 15, DataQuality.Invalid, 0) };
        var text = HardwareDiagnosticsReport.Build(DateTimeOffset.UnixEpoch, "test", "PC", new ProviderStatus(ProviderState.Ready, 2, null, null), [board], tallies);
        Assert.Contains("'Temp #3'", text); Assert.Contains("Not measured by the hardware (1)", text);
    }
}
