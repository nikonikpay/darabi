using Xunit; using Mazesta.Core.Hardware; using Mazesta.Core.Overlay;
namespace Mazesta.Core.Tests;

public class OverlayTests
{
    private static HardwareNode Node(string id, HardwareKind kind, params (string Name, SensorRole Role)[] sensors)
        => new(new HardwareId(id), kind, HardwareVendor.Unknown, id, null, true,
            [.. sensors.Select((s, i) => new SensorDefinition(new SensorId($"{id}/{i}"), new HardwareId(id), s.Name, SensorKind.Temperature, Unit.Celsius, s.Role, i))]);

    [Fact] public void Every_preset_names_only_catalog_items_once()
        => Assert.All(OverlayCatalog.Presets.Values, p => { Assert.All(p, c => Assert.NotNull(OverlayCatalog.Find(c.Id))); Assert.Equal(p.Count, p.Select(c => c.Id).Distinct().Count()); });
    [Fact] public void The_game_preset_charts_the_frame_rate() => Assert.Contains(new OverlayChoice("fps", true), OverlayCatalog.Presets["game"]);
    [Fact] public void Catalog_ids_are_unique() => Assert.Equal(OverlayCatalog.All.Count, OverlayCatalog.All.Select(i => i.Id).Distinct().Count());

    [Fact] public void First_prefers_the_gpu_with_a_core_temperature_and_the_first_role_found()
    {
        var igpu = Node("igpu", HardwareKind.Gpu, ("Load", SensorRole.GpuLoadD3D3D));
        var dgpu = Node("dgpu", HardwareKind.Gpu, ("Core", SensorRole.GpuCoreTemp), ("3D", SensorRole.GpuLoad3D));
        var load = OverlayCatalog.Resolve(OverlayCatalog.Find("gpu.load")!, [igpu, dgpu]);
        Assert.Equal("dgpu/1", Assert.Single(load).Id.Value);
    }
    [Fact] public void Max_and_sum_read_the_role_on_every_device_and_a_missing_role_is_unavailable()
    {
        var a = Node("nvme", HardwareKind.Storage, ("T", SensorRole.StorageTemp)); var b = Node("hdd", HardwareKind.Storage, ("T", SensorRole.StorageTemp));
        Assert.Equal(2, OverlayCatalog.Resolve(OverlayCatalog.Find("storage.temp")!, [a, b]).Count);
        Assert.Empty(OverlayCatalog.Resolve(OverlayCatalog.Find("cpu.voltage")!, [a, b]));
        Assert.Empty(OverlayCatalog.Resolve(OverlayCatalog.Find("fps")!, [a, b]));
        Assert.Single(OverlayCatalog.Resolve(OverlayCatalog.Find("storage.temp")!, [a, b], n => n.Name != "hdd"));
    }
    [Fact] public void Combine_never_invents_a_value()
    {
        Assert.Null(OverlayCatalog.Combine(OverlayAggregate.Sum, [null, null]));
        Assert.Equal(5, OverlayCatalog.Combine(OverlayAggregate.Sum, [2, null, 3]));
        Assert.Equal(71, OverlayCatalog.Combine(OverlayAggregate.Max, [64, 71, null]));
        Assert.Equal(64, OverlayCatalog.Combine(OverlayAggregate.First, [null, 64, 71]));
    }

    private static double[] Frames(double fps, double seconds, double start = 0) => [.. Enumerable.Range(0, (int)(fps * seconds) + 1).Select(i => start + i / fps)];

    [Fact] public void A_steady_sixty_fps_reads_sixty_with_a_sixty_low()
    {
        var f = Frames(60, 12);
        var r = FrameTimeStats.Compute(f, f[^1] + 0.5, 1, "game")!;
        Assert.Equal(60, r.Fps, 1); Assert.Equal(16.67, r.FrameTimeMs, 1); Assert.Equal(60, r.Low1Fps!.Value, 1);
    }
    [Fact] public void Stutters_pull_the_one_percent_low_down_but_not_the_average()
    {
        var f = new List<double> { 0 };
        for (int i = 1; i < 1200; i++) f.Add(f[^1] + (i % 50 == 0 ? 0.05 : 0.01));   // 100 fps with a 50 ms hitch every 50 frames (2%)
        var r = FrameTimeStats.Compute(f, f[^1], 1, null)!;
        Assert.Equal(20, r.Low1Fps!.Value, 1); Assert.True(r.Fps > 80, $"fps {r.Fps}");
    }
    [Fact] public void Too_few_frames_give_no_low_and_old_frames_give_nothing()
    {
        var f = Frames(30, 1);
        Assert.Null(FrameTimeStats.Compute(f, f[^1], 1, null)!.Low1Fps);
        Assert.Null(FrameTimeStats.Compute(f, f[^1] + 3, 1, null));
        Assert.Null(FrameTimeStats.Compute([1.0], 1.0, 1, null));
    }
}
