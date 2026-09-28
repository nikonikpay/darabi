using Mazesta.Core.Hardware; using Mazesta.Core.Overlay; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Xunit;
namespace Mazesta.Desktop.Tests;

public class OverlayViewModelTests
{
    private static HardwareNode Node(HardwareKind kind, string id, string name, params (string Path, SensorRole Role, Unit Unit)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, name, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s.Path), hid, s.Path, SensorKind.Temperature, s.Unit, s.Role, i))]);
    }
    private static readonly OverlayChoice[] Items = [new("gpu.temp", false), new("gpu.load", true), new("gpu.voltage", false), new("gpu.vram", false), new("gpu.hotspot", false),
        new("cpu.temp", true), new("cpu.load", false), new("net.down", false)];
    private sealed class FakeFrames(FrameRateReading? reading) : IFrameRateSource
    {
        public int Starts, Stops;
        public bool Start() { Starts++; return true; }
        public void Stop() => Stops++;
        public FrameRateReading? Read() => reading;
        public string? Problem => null;
    }
    private static (OverlayViewModel Vm, PollingEngine Engine, FakeSensorProvider P, FakeClock C) Build(IReadOnlyList<OverlayChoice>? items = null, IFrameRateSource? frames = null)
    {
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var p = new FakeSensorProvider();
        p.Nodes.Add(Node(HardwareKind.Gpu, "gpu/0", "NVIDIA GeForce RTX 3090", ("t", SensorRole.GpuCoreTemp, Unit.Celsius), ("l", SensorRole.GpuLoad3D, Unit.Percent), ("v", SensorRole.GpuVoltage, Unit.Volt), ("m", SensorRole.GpuVramUsed, Unit.Megabyte)));
        p.Nodes.Add(Node(HardwareKind.Cpu, "cpu/0", "AMD Ryzen 9 3950X", ("t", SensorRole.CpuPackageTemp, Unit.Celsius), ("l", SensorRole.CpuTotalLoad, Unit.Percent)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/0", "Ethernet", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/1", "Wi-Fi", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/2", "vEthernet (Default Switch)", ("d", SensorRole.NetDownload, Unit.BytesPerSecond)));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
        return (new OverlayViewModel(e, a => { a(); return null!; }, items ?? Items, frames), e, p, c);
    }
    private static OverlayRow Row(OverlayViewModel vm, string section, string key) => vm.Sections.Single(s => s.Title == section).Rows.Single(r => r.Label == Loc.Get(key));

    [Fact] public void Only_sensors_the_machine_has_become_rows()
    {
        var (vm, _, _, _) = Build();
        Assert.Equal(["GPU", "CPU", "NET"], vm.Sections.Select(s => s.Title));
        Assert.Equal([Loc.Get("Overlay_Temp"), Loc.Get("Overlay_Load"), Loc.Get("Overlay_Voltage"), Loc.Get("Overlay_Vram")], vm.Sections[0].Rows.Select(r => r.Label));   // no hot spot sensor: no row
    }

    [Fact] public void Values_are_shown_only_while_on_screen_and_network_is_the_sum_of_real_adapters()
    {
        var (vm, e, _, _) = Build();
        e.TickOnce(); Assert.Equal(OverlayViewModel.Missing, Row(vm, "GPU", "Overlay_Temp").Value);   // hidden: snapshots are ignored
        vm.SetActive(true); e.TickOnce();
        Assert.Equal("42 °C", Row(vm, "GPU", "Overlay_Temp").Value); Assert.Equal("42.000 V", Row(vm, "GPU", "Overlay_Voltage").Value);
        Assert.Equal("84 B/s", Row(vm, "NET", "Overlay_Down").Value);   // two adapters, the virtual switch left out
        Assert.Equal([42.0], Row(vm, "GPU", "Overlay_Load").Trend); Assert.Empty(Row(vm, "GPU", "Overlay_Temp").Trend);   // only charted rows keep a trend
    }

    [Fact] public void A_reading_that_is_not_good_shows_a_dash_and_a_gap_in_the_trend()
    {
        var (vm, e, p, _) = Build(); vm.SetActive(true);
        p.OnPoll = r => new([.. p.Nodes.SelectMany(n => n.Sensors).Select(s => new SensorReading(s.Id, null, r.Now, DataQuality.Missing, "fake"))], p.Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
        e.TickOnce();
        Assert.Equal(OverlayViewModel.Missing, Row(vm, "CPU", "Overlay_Temp").Value); Assert.True(double.IsNaN(Row(vm, "CPU", "Overlay_Temp").Trend[^1]));
    }

    [Fact] public void Short_forms_keep_voltage_precise_and_turn_large_megabytes_into_gigabytes()
    {
        Assert.Equal("0.744 V", OverlayViewModel.Format(0.7442, Unit.Volt)); Assert.Equal("2.5 GB", OverlayViewModel.Format(2560, Unit.Megabyte)); Assert.Equal("512 MB", OverlayViewModel.Format(512, Unit.Megabyte));
        Assert.Equal("1695 MHz", OverlayViewModel.Format(1695.4, Unit.MegaHertz)); Assert.Equal("30.4 MB/s", OverlayViewModel.Format(30_400_000, Unit.BytesPerSecond));
    }

    [Fact] public void Frame_items_read_the_frame_source_only_while_shown_and_a_missing_low_is_a_dash()
    {
        var frames = new FakeFrames(new FrameRateReading(143.6, null, 6.96, 42, "game"));
        var (vm, e, _, _) = Build([new("fps", true), new("low1", false), new("frametime", false), new("gpu.temp", false)], frames);
        Assert.Equal(["GAME", "GPU"], vm.Sections.Select(s => s.Title));
        vm.SetActive(true); e.TickOnce();
        Assert.Equal(1, frames.Starts);
        Assert.Equal("144 FPS", Row(vm, "GAME", "Overlay_Fps").Value); Assert.Equal(OverlayViewModel.Missing, Row(vm, "GAME", "Overlay_Low1").Value);
        Assert.Equal("7.0 ms", Row(vm, "GAME", "Overlay_FrameTime").Value); Assert.Equal("game", vm.Sections[0].Subtitle);
        Assert.True(double.IsNaN(vm.HeroLowValue));   // no level is drawn for a low that was not measured
        vm.SetActive(false); Assert.Equal(1, frames.Stops);
    }

    [Fact] public void The_frame_rate_card_takes_the_game_items_and_each_row_splits_its_number_unit_and_bar()
    {
        var frames = new FakeFrames(new FrameRateReading(143.6, 118.2, 6.96, 42, "game"));
        var (vm, e, _, _) = Build([new("fps", false), new("low1", false), new("frametime", false), new("gpu.temp", false), new("gpu.load", true)], frames);
        Assert.True(vm.HasHero); Assert.Equal(["GPU"], vm.Blocks.Select(s => s.Title));
        vm.SetActive(true); e.TickOnce(); e.TickOnce();
        Assert.Equal(("144", "FPS"), (vm.HeroFps!.Number, vm.HeroFps.UnitText)); Assert.Equal(("7.0", "ms"), (vm.HeroFrameTime!.Number, vm.HeroFrameTime.UnitText));
        Assert.Equal(2, vm.HeroTrend.Length);   // the block's trace runs with the frame rate's chart off
        Assert.Equal(118.2, vm.HeroLowValue);   // the 1 % low, drawn as a level across the trace
        var temp = Row(vm, "GPU", "Overlay_Temp");
        Assert.Equal(("42", "°C", 0.42), (temp.Number, temp.UnitText, temp.Fraction)); Assert.True(temp.HasBar);
        Assert.False(Row(vm, "GPU", "Overlay_Load").HasBar);   // charted: its chart, not a bar
    }

    [Fact] public void Without_game_items_there_is_no_frame_rate_card() => Assert.False(Build().Vm.HasHero);

    [Fact] public void Without_frame_items_the_frame_source_is_never_started()
    {
        var frames = new FakeFrames(null);
        var (vm, _, _, _) = Build(frames: frames);
        vm.SetActive(true); vm.SetActive(false);
        Assert.Equal(0, frames.Starts);
    }
}

public class OverlayOrderTests
{
    private static HardwareNode Node(HardwareKind kind, string id, string name, params (string Path, SensorRole Role, Unit Unit)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, name, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s.Path), hid, s.Path, SensorKind.Temperature, s.Unit, s.Role, i))]);
    }
    private static readonly HardwareNode[] Hw =
    [
        Node(HardwareKind.Cpu, "cpu/0", "CPU", ("t", SensorRole.CpuPackageTemp, Unit.Celsius), ("l", SensorRole.CpuTotalLoad, Unit.Percent)),
        Node(HardwareKind.Gpu, "gpu/0", "GPU", ("t", SensorRole.GpuCoreTemp, Unit.Celsius)),
        Node(HardwareKind.Storage, "storage/a", "Samsung SSD 980 PRO", ("r", SensorRole.StorageReadRate, Unit.BytesPerSecond), ("w", SensorRole.StorageWriteRate, Unit.BytesPerSecond)),
        Node(HardwareKind.Storage, "storage/b", "WDC WD20", ("r", SensorRole.StorageReadRate, Unit.BytesPerSecond)),
    ];

    [Fact] public void Blocks_follow_the_order_their_first_item_was_put_and_items_their_own_order()
    {
        var sections = OverlayViewModel.Build(Hw, [new("cpu.load", false), new("gpu.temp", false), new("cpu.temp", true)]);
        Assert.Equal(["CPU", "GPU"], sections.Select(s => s.Title));
        Assert.Equal(["cpu.load", "cpu.temp"], sections[0].Rows.Select(r => r.Id));
    }
    [Fact] public void A_drive_item_gets_that_drive_block_named_after_it()
    {
        var sections = OverlayViewModel.Build(Hw, [new("storage.write@storage/a", false), new("storage.read", false), new("storage.read@storage/a", true), new("storage.read@storage/b", false)]);
        Assert.Equal(3, sections.Count);
        Assert.Equal("Samsung SSD 980 PRO", sections[0].Subtitle); Assert.Equal(["storage.write@storage/a", "storage.read@storage/a"], sections[0].Rows.Select(r => r.Id));
        Assert.Null(sections[1].Device); Assert.Equal("WDC WD20", sections[2].Subtitle);
    }
    [Fact] public void A_drive_item_for_a_drive_that_is_gone_is_left_out() => Assert.Empty(OverlayViewModel.Build(Hw, [new("storage.read@storage/zz", false)]));
}
