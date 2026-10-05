using Mazesta.Core.Rgb; using Xunit;
namespace Mazesta.Persistence.Tests;

public class RgbSceneStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-rgbscene-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths() { var p = AppPaths.Create(_dir); p.EnsureDirectories(); return p; }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact] public void A_scene_comes_back_as_it_was_kept()
    {
        var paths = Paths(); var scene = new RgbScene { Enabled = true, Dark = true, All = new RgbLook("#ff0000", "Breathing", 40, 90) };
        scene.Devices[RgbScene.DeviceKey("ASUS", "PRIME", "HID")] = new RgbLook(Off: true); scene.Zones[RgbScene.ZoneKey("ASUS", "PRIME", "HID", "Header")] = 30;
        RgbSceneStore.Write(paths, scene);
        var read = RgbSceneStore.Read(paths);
        Assert.True(read.Enabled); Assert.True(read.Dark); Assert.Equal(new RgbLook("#ff0000", "Breathing", 40, 90), read.All);
        Assert.True(read.Devices["ASUS|PRIME|HID"].Off); Assert.Equal(30, read.Zones["ASUS|PRIME|HID|Header"]);
    }

    [Fact] public void No_file_or_a_damaged_one_is_the_empty_scene_that_applies_nothing()
    {
        var paths = Paths(); Assert.False(RgbSceneStore.Read(paths).Enabled);
        File.WriteAllText(RgbSceneStore.FileIn(paths), "{not json"); var s = RgbSceneStore.Read(paths);
        Assert.False(s.Enabled); Assert.Null(s.All); Assert.Empty(s.Zones);
    }

    [Fact] public void The_led_counts_an_earlier_version_kept_are_taken_over()
    {
        var paths = Paths(); File.WriteAllText(Path.Combine(paths.ConfigDir, "rgb-zones.json"), "{\"a|b|c|d\":14}");
        Assert.Equal(14, RgbSceneStore.Read(paths).Zones["a|b|c|d"]);
    }
}
