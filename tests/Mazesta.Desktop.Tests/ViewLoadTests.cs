using System.IO; using System.Windows; using System.Windows.Controls; using Xunit;
using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views;
namespace Mazesta.Desktop.Tests;

/// <summary>The one native window left (the web page draws everything else): it loads and lays out on an STA thread with no app resources.</summary>
public class ViewLoadTests
{
    private static void OnSta(Func<FrameworkElement> make)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var el = make(); var host = new Window { Content = el, Width = 800, Height = 600, ShowInTaskbar = false };
                host.Show(); el.UpdateLayout(); host.Close();
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (error is not null) throw new Exception(error.ToString());
    }
    /// <summary>The overlay drawn with live-looking values in each layout; with MAZESTA_RENDER_DIR set, saved there as pictures for a look by eye.</summary>
    [Theory, InlineData("list"), InlineData("columns"), InlineData("line")]
    public void OverlayWindow_draws_the_frame_rate_box_and_a_box_per_part(string layout)
        => OnSta(() =>
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
            var window = new OverlayWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content; window.Content = null; root.DataContext = vm; Mazesta.Desktop.Localization.Rtl.Apply(root);
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); root.Arrange(new Rect(root.DesiredSize)); root.UpdateLayout();
            Assert.True(root.DesiredSize.Width > (layout == "list" ? 200 : 300));
            if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir)
            {
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(root);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(dir, $"overlay-{layout}.png")); png.Save(f);
            }
            return new Border();
        });
    private sealed class Frames : Mazesta.Monitoring.IFrameRateSource
    {
        public double Fps = 120;
        public bool Start() => true; public void Stop() { } public string? Problem => null;
        public Mazesta.Core.Overlay.FrameRateReading? Read() => new(Fps, Fps * 0.8, 1000 / Fps, 42, "Cyberpunk2077.exe");
    }
}
