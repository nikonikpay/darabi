using System.IO.Compression; using System.Text;
using Mazesta.Persistence.Updates; using Xunit;
namespace Mazesta.Persistence.Tests;

public class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-update-tests-" + Guid.NewGuid().ToString("N"));
    public UpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private static readonly string Hash = new('a', 64);
    private static UpdateManifest Manifest(string version = "0.7.0", string file = "MazestaUpdate-0.7.0.zip")
        => new(UpdateManifest.CurrentFormat, DateTimeOffset.UnixEpoch, new AppRelease(version, file, 10, Hash, DateTimeOffset.UnixEpoch), [new("benchdb/bench.cpu.multi-v1.json", 5, Hash)]);

    [Fact] public void A_signed_manifest_verifies_and_any_change_breaks_it()
    {
        var (priv, pub) = UpdateSigning.NewKey();
        var bytes = Manifest().ToBytes(); string sig = UpdateSigning.Sign(bytes, priv);
        Assert.True(UpdateSigning.Verify(bytes, sig, pub));
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("0.7.0", "0.9.0"));
        Assert.False(UpdateSigning.Verify(changed, sig, pub));
        Assert.False(UpdateSigning.Verify(bytes, sig, UpdateSigning.NewKey().PublicBase64));
        Assert.False(UpdateSigning.Verify(bytes, "not base64!", pub));
    }
    [Fact] public void A_manifest_round_trips() => Assert.Equal("0.7.0", UpdateManifest.Parse(Manifest().ToBytes()).App!.Version);
    [Theory]
    [InlineData("../MazestaUpdate.zip")]
    [InlineData("a/b/c.zip")]
    [InlineData("C:\\x.zip")]
    [InlineData("https://evil.example/x.zip")]
    public void File_names_outside_the_update_folder_are_refused(string file) => Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(Manifest(file: file).ToBytes()));
    [Fact] public void A_version_is_compared_as_a_version()
    {
        Assert.True(Manifest("0.10.0").IsNewer("0.9.0"));
        Assert.False(Manifest("0.6.0").IsNewer("0.6.0"));
        Assert.False(Manifest("0.5.9").IsNewer("0.6.0"));
    }

    [Fact] public void Installing_replaces_the_app_files_and_leaves_Data_alone()
    {
        string app = Path.Combine(_dir, "app"), staging = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(Path.Combine(app, "Data")); Directory.CreateDirectory(Path.Combine(app, "wwwroot"));
        File.WriteAllText(Path.Combine(app, "Data", "appconfig.json"), "mine");
        File.WriteAllText(Path.Combine(app, "Mazesta.exe"), "old"); File.WriteAllText(Path.Combine(app, "gone.dll"), "old");
        File.WriteAllText(Path.Combine(app, "wwwroot", "index.html"), "old");
        Directory.CreateDirectory(Path.Combine(staging, "wwwroot"));
        File.WriteAllText(Path.Combine(staging, "Mazesta.exe"), "new"); File.WriteAllText(Path.Combine(staging, "wwwroot", "index.html"), "new");

        Assert.True(UpdateInstaller.Apply(staging, app, Path.Combine(app, "Data", "cache", "update", "previous"), _ => { }));
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "Mazesta.exe")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "wwwroot", "index.html")));
        Assert.False(File.Exists(Path.Combine(app, "gone.dll")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(app, "Data", "appconfig.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "Data", "cache", "update", "previous", "Mazesta.exe")));
    }
    [Fact] public void Installing_keeps_the_setups_marker_so_an_installed_copy_stays_installed()
    {
        string app = Path.Combine(_dir, "app2"), staging = Path.Combine(_dir, "staging2");
        Directory.CreateDirectory(app); Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(app, AppPaths.InstalledMarker), "x"); File.WriteAllText(Path.Combine(app, "Mazesta.exe"), "old"); File.WriteAllText(Path.Combine(staging, "Mazesta.exe"), "new");
        Assert.True(UpdateInstaller.Apply(staging, app, Path.Combine(_dir, "previous2"), _ => { }));
        Assert.True(File.Exists(Path.Combine(app, AppPaths.InstalledMarker))); Assert.Equal("new", File.ReadAllText(Path.Combine(app, "Mazesta.exe")));
    }
    [Fact] public void A_package_with_a_Data_folder_or_without_the_exe_is_refused()
    {
        string zip = Path.Combine(_dir, "p.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { z.CreateEntry("Mazesta.exe"); z.CreateEntry("Data/appconfig.json"); }
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(zip, Path.Combine(_dir, "s1")));
        File.Delete(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(zip, Path.Combine(_dir, "s2")));
    }
}
