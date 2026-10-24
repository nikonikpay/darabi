using Mazesta.Core.Fans; using Xunit;
namespace Mazesta.Persistence.Tests;

public class GpuFanProfilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-gpufan-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact] public void The_curve_in_force_and_the_saved_profiles_come_back()
    {
        string file = Path.Combine(_dir, "gpu-fan.json");
        var p = new GpuFanProfiles { Active = "Night", Current = new() { Mode = "curve", Source = "hot", Points = [new(40, 30), new(80, 100)] } };
        p.Custom["Night"] = new() { Mode = "curve", Source = "core", Points = [new(30, 30), new(70, 90)] };
        p.Write(file);
        var read = GpuFanProfiles.Read(file);
        Assert.Equal("Night", read.Active); Assert.Equal("curve", read.Current.Mode); Assert.Equal("hot", read.Current.Source); Assert.Equal(new FanPoint(80, 100), read.Current.Points[1]);
        Assert.Equal(new FanPoint(70, 90), read.Custom["Night"].Points[1]);
    }

    [Fact] public void A_missing_or_damaged_file_is_automatic()
    {
        string file = Path.Combine(_dir, "gpu-fan.json"); Assert.Equal("auto", GpuFanProfiles.Read(file).Current.Mode);
        Directory.CreateDirectory(_dir); File.WriteAllText(file, "{broken"); Assert.Equal("auto", GpuFanProfiles.Read(file).Current.Mode);
    }

    [Fact] public void A_curve_with_too_few_points_is_no_curve_and_the_fans_stay_with_the_card()
    {
        string file = Path.Combine(_dir, "gpu-fan.json");
        new GpuFanProfiles { Current = new() { Mode = "curve", Points = [new(50, 50)] } }.Write(file);
        Assert.Equal("auto", GpuFanProfiles.Read(file).Current.Mode);
    }
}
