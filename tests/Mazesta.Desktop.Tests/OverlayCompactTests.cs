using Mazesta.Core.Hardware; using Mazesta.Core.Overlay; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Xunit;
namespace Mazesta.Desktop.Tests;

public class OverlayCompactTests
{
    private sealed class Frames(FrameRateReading reading) : IFrameRateSource { public bool Start() => true; public void Stop() { } public FrameRateReading? Read() => reading; public string? Problem => null; }

    private static HardwareNode Node(HardwareKind kind, string id, string name, params (string Path, SensorRole Role, Unit Unit)[] sensors)
    {
        var hid = new HardwareId(id);
        return new(hid, kind, HardwareVendor.Unknown, name, null, true, [.. sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s.Path), hid, s.Path, SensorKind.Temperature, s.Unit, s.Role, i))]);
    }

    private static OverlayViewModel Build(string layout, bool english)
    {
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var p = new FakeSensorProvider();
        p.Nodes.Add(Node(HardwareKind.Gpu, "gpu/0", "NVIDIA GeForce RTX 3090", ("t", SensorRole.GpuCoreTemp, Unit.Celsius), ("l", SensorRole.GpuLoad3D, Unit.Percent), ("c", SensorRole.GpuCoreClock, Unit.MegaHertz),
            ("p", SensorRole.GpuPower, Unit.Watt), ("v", SensorRole.GpuVoltage, Unit.Volt), ("f", SensorRole.GpuFanPercent, Unit.Percent), ("r", SensorRole.GpuFanRpm, Unit.Rpm),
            ("m", SensorRole.GpuVramUsed, Unit.Gigabyte), ("mc", SensorRole.GpuMemoryClock, Unit.MegaHertz), ("vt", SensorRole.GpuVramTemp, Unit.Celsius)));
        p.Nodes.Add(Node(HardwareKind.Cpu, "cpu/0", "AMD Ryzen 9 3950X", ("t", SensorRole.CpuPackageTemp, Unit.Celsius), ("l", SensorRole.CpuTotalLoad, Unit.Percent), ("p", SensorRole.CpuPackagePower, Unit.Watt),
            ("c", SensorRole.CpuEffectiveClockAverage, Unit.MegaHertz)));
        p.Nodes.Add(Node(HardwareKind.Memory, "ram/0", "Memory", ("u", SensorRole.RamUsed, Unit.Gigabyte)));
        p.Nodes.Add(Node(HardwareKind.Network, "nic/0", "Ethernet", ("d", SensorRole.NetDownload, Unit.BytesPerSecond), ("u", SensorRole.NetUpload, Unit.BytesPerSecond)));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
        string[] ids = ["fps", "low1", "frametime", "gpu.temp", "gpu.load", "gpu.clock", "gpu.power", "gpu.voltage", "gpu.fan", "gpu.fanrpm", "gpu.vram", "gpu.memclock", "gpu.vramtemp",
            "cpu.temp", "cpu.load", "cpu.power", "cpu.clock", "ram.used", "net.down", "net.up"];
        var vm = new OverlayViewModel(e, a => { a(); return null!; }, [.. ids.Select(i => new OverlayChoice(i, false))], new Frames(new FrameRateReading(142, 120.4, 7.04, 1, "game")), 0.9, 1, layout, null, english);
        vm.SetActive(true); for (int i = 0; i < 6; i++) e.TickOnce();
        return vm;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void The_compact_layout_draws_the_frame_rate_then_a_line_for_each_part(bool english)
    {
        var vm = Build("compact", english);
        Assert.True(vm.IsCompact); Assert.False(vm.IsStacked); Assert.Equal(english, vm.English);
        using var bmp = OverlayRenderer.Render(vm, rtl: !english, 2f);
        Assert.InRange(bmp.Width, 200, 2400); Assert.InRange(bmp.Height, 120, 1600);
        // Not drawn when no picture is wanted; with MAZESTA_OVERLAY_PNG set the pictures are kept for a look.
        if (Environment.GetEnvironmentVariable("MAZESTA_OVERLAY_PNG") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); bmp.Save(Path.Combine(dir, $"compact-{(english ? "en" : "fa")}.png")); }
    }

    [Fact] public void The_english_overlay_gives_english_labels_whatever_the_app_language_is()
    {
        var vm = Build("list", english: true);
        Assert.Contains(vm.Sections.SelectMany(s => s.Rows), r => r.Label == "Temperature" || r.Label == "Temp");
    }
}
