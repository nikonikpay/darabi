using System.IO; using Microsoft.Extensions.DependencyInjection; using System.Windows; using System.Windows.Controls; using Xunit;
using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views;
namespace Mazesta.Desktop.Tests;

public class ViewLoadTests
{
    private static void OnSta(Func<FrameworkElement> make)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MazestaTest;component/Themes/Dark.xaml") });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MazestaTest;component/Themes/SensorIcons.xaml") });
                var el = make(); var host = new Window { Content = el, Width = 800, Height = 600, ShowInTaskbar = false };
                host.Show(); el.UpdateLayout(); host.Close();
            }
            catch (Exception e) { error = e; }
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (error is not null) throw new Exception(error.ToString());
    }

    [Fact] public void SystemInfoView_loads_and_renders_sections()
        => OnSta(() =>
        {
            var vm = new SystemInfoViewModel(new Mazesta.Desktop.Composition.InventoryCache(new Inv(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance), a => { a(); return null!; });
            vm.Loaded.Wait();
            return new SystemInfoView { DataContext = vm };
        });

    [Fact] public void SettingsView_loads_with_the_tray_section()
        => OnSta(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "mazesta-view-" + Guid.NewGuid().ToString("N"));
            var cfg = new Mazesta.Persistence.AppConfig(); var store = new Mazesta.Persistence.JsonStore<Mazesta.Persistence.AppConfig>(Path.Combine(dir, "a.json"), new Mazesta.Persistence.SchemaMigrator(Mazesta.Persistence.AppConfig.Migrations), Mazesta.Persistence.AppConfig.CurrentSchemaVersion, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch); var opts = new Mazesta.Monitoring.MonitoringOptions();
            var e = new Mazesta.Monitoring.PollingEngine(new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(), c, opts, new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var shell = new ShellViewModel(e, new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
            return new SettingsView { DataContext = new SettingsViewModel(cfg, store, Mazesta.Persistence.AppPaths.Create(dir), e, opts, shell, _ => { }, new StubTray()) };
        });

    [Fact] public void TuningView_loads_with_a_card_and_its_profiles()
        => OnSta(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "mazesta-view-" + Guid.NewGuid().ToString("N"));
            var store = new Mazesta.Persistence.JsonStore<Mazesta.Persistence.GpuProfileDocument>(Path.Combine(dir, "g.json"), new Mazesta.Persistence.SchemaMigrator([]), 1, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var vm = new TuningViewModel(new TuningViewModelTests.Provider(new TuningViewModelTests.Card()), store, new Mazesta.Desktop.Composition.InventoryCache(new TuningViewModelTests.Inv(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance),
                _ => false, () => { }, a => { a(); return null!; }, _ => new TuningViewModelTests.NoLoad(), null, withTimer: false);
            vm.SaveProfileCommand.Execute(null);
            return new TuningView { DataContext = vm };
        });

    [Fact] public void BenchmarksView_loads_with_its_rows()
        => OnSta(() =>
        {
            var probe = new FixedProbe();
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch);
            var e = new Mazesta.Monitoring.PollingEngine(new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(), c, new Mazesta.Monitoring.MonitoringOptions(), new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            IEnumerable<Mazesta.Diagnostics.Benchmarks.IBenchmark> all = [new Mazesta.Diagnostics.Benchmarks.CpuBenchmark(allThreads: false), new Mazesta.Diagnostics.Benchmarks.CpuBenchmark(allThreads: true), new Mazesta.Diagnostics.Benchmarks.MemoryBenchmark(probe), new Mazesta.Diagnostics.Benchmarks.StorageBenchmark()];
            return new BenchmarksView { DataContext = new BenchmarksViewModel(new Mazesta.Diagnostics.Benchmarks.BenchmarkRunner(all, c, e), a => { a(); return null!; }) };
        });

    [Fact] public void ComponentView_loads_with_inventory_sensors_and_benchmarks()
        => OnSta(() =>
        {
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch); var p = new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider();
            p.Nodes.Add(Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider.Node(Mazesta.Core.Hardware.HardwareKind.Cpu, "cpu/intelcpu-0", "temperature/0", "clock/0"));
            var e = new Mazesta.Monitoring.PollingEngine(p, c, new Mazesta.Monitoring.MonitoringOptions(), new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks();
            Func<Action, object> now = a => { a(); return null!; }; var kind = Mazesta.Core.Hardware.HardwareKind.Cpu;
            var sensors = new MonitoringViewModel(e, new Mazesta.Monitoring.MonitoringFocus(), new Mazesta.Persistence.AppConfig(), new NoCharts(), c, now, new HashSet<Mazesta.Core.Hardware.HardwareKind> { kind });
            var runner = new Mazesta.Diagnostics.Benchmarks.BenchmarkRunner([new Mazesta.Diagnostics.Benchmarks.CpuBenchmark(allThreads: false)], c, e);
            var vm = new ComponentViewModel(kind, new Mazesta.Desktop.Composition.InventoryCache(new Inv(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Mazesta.Desktop.Composition.InventoryCache>.Instance), sensors, new BenchmarksViewModel(runner, now, kind), now);
            vm.Loaded.Wait(); Assert.True(vm.HasSensors); Assert.True(vm.HasBenchmarks); Assert.NotEmpty(vm.Inventory);
            return new ComponentView { DataContext = vm };
        });

    [Fact] public void MainWindow_loads_with_the_sidebar_logo_service_number_and_a_notice()
        => OnSta(() =>
        {
            var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(DateTimeOffset.UnixEpoch);
            var e = new Mazesta.Monitoring.PollingEngine(new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(), c, new Mazesta.Monitoring.MonitoringOptions(), new Mazesta.Monitoring.BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection(); services.AddSingleton(new Mazesta.Persistence.AppConfig { ServiceNumber = "S-1" });
            var shell = new ShellViewModel(e, services.BuildServiceProvider()); shell.Notify(new("done", ToastKind.Success));
            var window = new MainWindow { DataContext = shell };
            return (FrameworkElement)window.Content;   // the content only: the window itself is the host here
        });

    /// <summary>The overlay drawn with live-looking values; with MAZESTA_RENDER_DIR set, saved there as a picture for a look by eye.</summary>
    [Fact] public void OverlayWindow_draws_the_frame_rate_card_and_part_cards()
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
            var vm = new OverlayViewModel(e, a => { a(); return null!; }, [new("fps", false), new("low1", false), new("frametime", false), new("gpu.temp", false), new("gpu.load", false), new("cpu.temp", false), new("cpu.load", true)], frames, twoColumns: true);
            vm.SetActive(true); for (int i = 0; i < 30; i++) { frames.Fps = 120 + 20 * Math.Sin(i / 3.0); e.TickOnce(); }
            var window = new OverlayWindow { DataContext = vm };
            var root = (FrameworkElement)window.Content; window.Content = null; root.DataContext = vm;
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); root.Arrange(new Rect(root.DesiredSize)); root.UpdateLayout();
            Assert.True(root.DesiredSize.Width > 300);
            if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir)
            {
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(root);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(dir, "overlay.png")); png.Save(f);
            }
            return new Border();
        });
    private sealed class Frames : Mazesta.Monitoring.IFrameRateSource
    {
        public double Fps = 120;
        public bool Start() => true; public void Stop() { } public string? Problem => null;
        public Mazesta.Core.Overlay.FrameRateReading? Read() => new(Fps, Fps * 0.8, 1000 / Fps, 42, "Cyberpunk2077.exe");
    }

    [Fact] public void WindowsTools_and_Gaming_views_load()
    {
        OnSta(() => new WindowsToolsView { DataContext = new WindowsToolsViewModel(new NoRunner(), new NoWmi(), _ => { }, a => { a(); return null!; }) });
        OnSta(() => new GamingView { DataContext = new GamingViewModel(new NoRunner(), _ => { }, new Mazesta.Diagnostics.Windows.GamingStatus(null, true)) });
    }
    private sealed class NoRunner : Mazesta.Diagnostics.Windows.ICommandRunner
    {
        public Task<Mazesta.Diagnostics.Windows.CommandResult> RunAsync(string file, string arguments, System.Text.Encoding encoding, Action<string>? line, CancellationToken ct) => Task.FromResult(new Mazesta.Diagnostics.Windows.CommandResult(0, ["Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *"]));
    }
    private sealed class NoWmi : Mazesta.Hardware.Wmi.IWmiQuery { public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql) => []; }

    private sealed class NoCharts : IChartWindowService { public void Open(Mazesta.Core.Hardware.SensorDefinition s, Mazesta.Core.Hardware.HardwareNode n) { } }

    private sealed class FixedProbe : Mazesta.Diagnostics.Memory.IMemoryProbe { public Mazesta.Diagnostics.Memory.MemoryStatus Read() => new(16L << 30, 8L << 30); }

    private sealed class StubTray : Mazesta.Desktop.Services.ITrayController
    {
        public Mazesta.Desktop.Services.TrayState Query() => new(false, false); public string? Enable() => null; public string? Disable() => null; public string? Restart() => null;
    }

    private sealed class Inv : Mazesta.Core.Providers.IInventoryProvider
    {
        public Task<Mazesta.Core.Inventory.HardwareInventory> ReadAsync(CancellationToken ct) => Task.FromResult(Mazesta.Core.Inventory.HardwareInventory.Empty);
    }
}
