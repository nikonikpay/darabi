using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Xunit;
namespace Mazesta.Desktop.Tests;

public class OverlayViewModelTests
{
    private static HardwareNode Node(HardwareKind kind, string id, string name, params (string Path, SensorRole Role, Unit Unit)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, name, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s.Path), hid, s.Path, SensorKind.Temperature, s.Unit, s.Role, i))]);
    }
    private static (OverlayViewModel Vm, PollingEngine Engine, FakeSensorProvider P, FakeClock C) Build()
    {
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var p = new FakeSensorProvider();
        p.Nodes.Add(Node(HardwareKind.Gpu, "gpu/0", "NVIDIA GeForce RTX 3090", ("t", SensorRole.GpuCoreTemp, Unit.Celsius), ("l", SensorRole.GpuLoad3D, Unit.Percent), ("v", SensorRole.GpuVoltage, Unit.Volt), ("m", SensorRole.GpuVramUsed, Unit.Megabyte)));
        p.Nodes.Add(Node(HardwareKind.Cpu, "cpu/0", "AMD Ryzen 9 3950X", ("t", SensorRole.CpuPackageTemp, Unit.Celsius), ("l", SensorRole.CpuTotalLoad, Unit.Percent)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/0", "Ethernet", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/1", "Wi-Fi", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/2", "vEthernet (Default Switch)", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
        return (new OverlayViewModel(e, a => { a(); return null!; }), e, p, c);
    }
    private static OverlayRow Row(OverlayViewModel vm, string section, string key) => vm.Sections.Single(s => s.Title == section).Rows.Single(r => r.Label == Loc.Get(key));

    [Fact] public void Only_sensors_the_machine_has_become_rows()
    {
        var (vm, _, _, _) = Build();
        Assert.Equal(["GPU", "CPU", "NET"], vm.Sections.Select(s => s.Title));
        Assert.Equal([Loc.Get("Overlay_Temp"), Loc.Get("Overlay_Load"), Loc.Get("Overlay_Voltage"), Loc.Get("Overlay_Vram")], vm.Sections[0].Rows.Select(r => r.Label));
    }

    [Fact] public void Values_are_shown_only_while_on_screen_and_network_is_the_sum_of_real_adapters()
    {
        var (vm, e, _, _) = Build();
        e.TickOnce(); Assert.Equal(OverlayViewModel.Missing, Row(vm, "GPU", "Overlay_Temp").Value);   // hidden: snapshots are ignored
        vm.SetActive(true); e.TickOnce();
        Assert.Equal("42 °C", Row(vm, "GPU", "Overlay_Temp").Value); Assert.Equal("42.000 V", Row(vm, "GPU", "Overlay_Voltage").Value);
        Assert.Equal("84 B/s", Row(vm, "NET", "Overlay_Down").Value);   // two adapters, the virtual switch left out
        Assert.Equal([42.0], vm.Sections[0].LoadTrend);
    }

    [Fact] public void A_reading_that_is_not_good_shows_a_dash_and_a_gap_in_the_trend()
    {
        var (vm, e, p, _) = Build(); vm.SetActive(true);
        p.OnPoll = r => new([.. p.Nodes.SelectMany(n => n.Sensors).Select(s => new SensorReading(s.Id, null, r.Now, DataQuality.Missing, "fake"))], p.Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
        e.TickOnce();
        Assert.Equal(OverlayViewModel.Missing, Row(vm, "CPU", "Overlay_Temp").Value); Assert.True(double.IsNaN(vm.Sections[1].TempTrend[^1]));
    }

    [Fact] public void Short_forms_keep_voltage_precise_and_turn_large_megabytes_into_gigabytes()
    {
        Assert.Equal("0.744 V", OverlayViewModel.Format(0.7442, Unit.Volt)); Assert.Equal("2.5 GB", OverlayViewModel.Format(2560, Unit.Megabyte)); Assert.Equal("512 MB", OverlayViewModel.Format(512, Unit.Megabyte));
        Assert.Equal("1695 MHz", OverlayViewModel.Format(1695.4, Unit.MegaHertz)); Assert.Equal("30.4 MB/s", OverlayViewModel.Format(30_400_000, Unit.BytesPerSecond));
    }
}
