using Mazesta.Core.Fans; using Xunit;
namespace Mazesta.Persistence.Tests;

public class FanProfilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-fanprofiles-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact] public void Profiles_labels_and_the_active_one_come_back()
    {
        string file = Path.Combine(_dir, "fan-profiles.json");
        var p = new FanProfiles { Active = "Night" };
        p.Custom["Night"] = new() { ["c1"] = new FanSetting { Mode = "curve", Source = "max", Points = [new(30, 20), new(70, 100)] } };
        p.Labels["c6"] = new FanLabel { Name = "AIO", Kind = "pump" };
        p.Write(file);
        var read = FanProfiles.Read(file);
        Assert.Equal("Night", read.Active); Assert.Equal(2, read.Custom["Night"]["c1"].Points.Count); Assert.Equal(new FanPoint(70, 100), read.Custom["Night"]["c1"].Points[1]);
        Assert.Equal("pump", read.Labels["c6"].Kind);
    }

    [Fact] public void A_missing_or_damaged_file_is_automatic()
    {
        string file = Path.Combine(_dir, "fan-profiles.json"); Assert.Equal("auto", FanProfiles.Read(file).Active);
        Directory.CreateDirectory(_dir); File.WriteAllText(file, "{broken"); Assert.Equal("auto", FanProfiles.Read(file).Active);
    }

    [Fact] public void The_trays_request_is_a_file_with_the_profile_name()
    {
        var paths = AppPaths.Create(_dir); FanProfiles.Request(paths, "silent");
        Assert.Contains("silent", File.ReadAllText(Path.Combine(paths.ConfigDir, FanProfiles.RequestFile)));
        Assert.Contains("auto", FanProfiles.Builtin); Assert.Equal(5, FanProfiles.Builtin.Length);
    }
}
