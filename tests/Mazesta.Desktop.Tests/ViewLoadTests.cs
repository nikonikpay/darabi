using System.IO; using Xunit;
using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views;
namespace Mazesta.Desktop.Tests;

/// <summary>The one native window left (the web page draws everything else): its picture is drawn without a window or a screen.</summary>
public class ViewLoadTests
{
    /// <summary>The overlay drawn with live-looking values in each layout; with MAZESTA_RENDER_DIR set, saved there as pictures for a look by eye.</summary>
    [Theory, InlineData("list"), InlineData("columns"), InlineData("line")]
    public void OverlayWindow_draws_the_frame_rate_box_and_a_box_per_part(string layout)
    {
        foreach (bool rtl in new[] { false, true })
        {
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch); var p = new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider();
            var gid = new Mazesta.Core.Hardware.HardwareId("gpu/nvidia-0");
            Mazesta.Core.Hardware.SensorDefinition S(string path, Mazesta.Core.Hardware.SensorRole role, Mazesta.Core.Hardware.Unit unit, int i)
                => new(Mazesta.Core.Hardware.SensorId.Create(gid, path), gid, path, Mazesta.Core.Hardware.SensorKind.Temperature, unit, role, i);
            p.Nodes.Add(new(gid, Mazesta.Core.Hardware.HardwareKind.Gpu, Mazesta.Core.Hardware.HardwareVendor.Nvidia, "NVIDIA GeForce RTX 5070 Ti", null, true,
                [S("t", Mazesta.Core.Hardware.SensorRole.GpuCoreTemp, Mazesta.Core.Hardware.Unit.Celsius, 0), S("l", Mazesta.Core.Hardware.SensorRole.GpuLoad3D, Mazesta.Core.Hardware.Unit.Percent, 1)]));
            var cid = new Mazesta.Core.Hardware.HardwareId("cpu/intel-0");
            p.Nodes.Add(new(cid, Mazesta.Core.Hardware.HardwareKind.Cpu, Mazesta.Core.Hardware.HardwareVendor.Intel, "Intel Core Ultra 7 265K", null, true,
                [new(Mazesta.Core.Hardware.SensorId.Create(cid, "t"), cid, "t", Mazesta.Core.Hardware.SensorKind.Temperature, Mazesta.Core.Hardware.Unit.Celsius, Mazesta.Core.Hardware.SensorRole.CpuPackageTemp, 0),
                 new(Mazesta.Core.Hardware.SensorId.Create(cid, "l"), cid, "l", Mazesta.Core.Hardware.SensorKind.Load, Mazesta.Core.Hardware.Unit.Percent, Mazesta.Core.Hardware.SensorRole.CpuTotalLoad, 1)]));
            var e = new Mazesta.Monitoring.PollingEngine(p, c, new Mazesta.Monitoring.MonitoringOptions(), new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
            var frames = new Frames();
            var vm = new OverlayViewModel(e, a => { a(); return null!; }, [new("fps", false), new("low1", false), new("fps.avg", false), new("fps.min", false), new("fps.max", false), new("frametime", false), new("gpu.temp", false), new("gpu.load", false), new("cpu.temp", false), new("cpu.load", true)], frames, layout: layout);
            vm.SetActive(true); for (int i = 0; i < 30; i++) { frames.Fps = 120 + 20 * Math.Sin(i / 3.0); e.TickOnce(); }
            using var bmp = OverlayRenderer.Render(vm, rtl, 2);
            // Twice the size: the stacked layouts are their panel's width plus the plate; the strip is as long as what it shows.
            Assert.True(bmp.Width / 2 > (layout == "list" ? 244 : 300), $"{layout} {bmp.Width}");
            Assert.True(bmp.Height / 2 > (layout == "line" ? 30 : 200), $"{layout} {bmp.Height}");
            if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir) bmp.Save(Path.Combine(dir, $"overlay-{layout}{(rtl ? "-rtl" : "")}.png"));
        }
    }
    private sealed class Frames : Mazesta.Monitoring.IFrameRateSource
    {
        public double Fps = 120;
        public bool Start() => true; public void Stop() { } public string? Problem => null;
        public Mazesta.Core.Overlay.FrameRateReading? Read() => new(Fps, Fps * 0.8, 1000 / Fps, 42, "Cyberpunk2077.exe");
    }
}
