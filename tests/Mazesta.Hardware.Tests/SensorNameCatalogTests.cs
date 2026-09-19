using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class SensorNameCatalogTests
{
    private static FakeHardware SuperIo(string board, string chip = "Nuvoton NCT6798D")
    {
        var mb = new FakeHardware(HardwareType.Motherboard, "/motherboard", board);
        var io = new FakeHardware(HardwareType.SuperIO, "/lpc/nct6798d/0", chip, mb);
        for (int i = 0; i < 7; i++) { io.Add($"Fan #{i + 1}", SensorType.Fan, i, 0); io.Add($"Fan #{i + 1}", SensorType.Control, i, 50); }
        mb.SubHardware = [io];
        return mb;
    }
    private static IReadOnlyList<SensorDefinition> Sensors(FakeHardware board) => new LhmHardwareMapper(_ => null).Map([board])[1].Sensors.Select(s => s.Definition).ToList();

    [Fact] public void Verified_board_names_the_cpu_and_cpu_opt_fan_headers_and_gives_them_the_right_roles()
    {
        var s = Sensors(SuperIo("ASUS PRIME B550M-A"));
        Assert.Equal(SensorRole.CpuFan, s.Single(x => x.Name == "CPU Fan").Role);
        Assert.Equal(SensorKind.Fan, s.Single(x => x.Name == "CPU_OPT Fan").Kind);
        Assert.Contains(s, x => x.Name == "CPU Fan Control" && x.Kind == SensorKind.Control);
    }
    [Fact] public void Unverified_channels_keep_lhm_name_and_only_the_duty_cycle_sensor_is_disambiguated()
    {
        var s = Sensors(SuperIo("ASUS PRIME B550M-A"));
        Assert.Contains(s, x => x.Name == "Fan #1" && x.Kind == SensorKind.Fan);      // no evidence for SYSFAN's header: not guessed
        Assert.Contains(s, x => x.Name == "Fan #1 Control" && x.Kind == SensorKind.Control);
    }
    [Fact] public void An_unknown_board_gets_no_header_labels_at_all()
    {
        var s = Sensors(SuperIo("Some Other Board"));
        Assert.DoesNotContain(s, x => x.Name.StartsWith("CPU", StringComparison.Ordinal));
        Assert.Equal(7, s.Count(x => x.Kind == SensorKind.Fan));
    }
    [Fact] public void Virtual_network_bindings_are_not_exposed_but_real_adapters_are()
    {
        FakeHardware Nic(string name) { var n = new FakeHardware(HardwareType.Network, $"/nic/{name.GetHashCode()}", name); n.Add("Data Downloaded", SensorType.Data, 0, 1); return n; }
        var nodes = new LhmHardwareMapper(_ => null).Map([Nic("Ethernet"), Nic("Wi-Fi-QoS Packet Scheduler-0000"), Nic("Ethernet 5-WFP Native MAC Layer LightWeight Filter-0000"), Nic("Local Area Connection* 10"), Nic("Tailscale")]);
        Assert.Equal(["Ethernet", "Tailscale"], nodes.Select(n => n.Node.Name));
    }
    [Fact] public void Sensors_that_lhm_only_activates_on_first_update_are_present_after_start()
    {
        var mb = new FakeHardware(HardwareType.Motherboard, "/motherboard", "Any board");
        var io = new FakeHardware(HardwareType.SuperIO, "/lpc/nct6798d/0", "Nuvoton NCT6798D", mb); io.Add("Fan #2", SensorType.Control, 1, 50);
        io.OnUpdate = () => { if (io.Sensors.All(x => x.SensorType != SensorType.Fan)) io.Add("Fan #2", SensorType.Fan, 1, 900); };
        mb.SubHardware = [io];
        var computer = new FakeLhmComputer(); computer.Roots.Add(mb);
        var p = new LibreHardwareMonitorProvider(computer, () => true, () => true, _ => null, new SystemClock(), NullLogger<LibreHardwareMonitorProvider>.Instance);
        p.Start();
        Assert.Contains(p.Hardware.SelectMany(n => n.Sensors), s => s.Kind == SensorKind.Fan);
    }
}
