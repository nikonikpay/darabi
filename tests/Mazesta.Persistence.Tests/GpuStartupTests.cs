using Mazesta.Core.Tuning; using Xunit;
namespace Mazesta.Persistence.Tests;

public class GpuStartupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-gpustartup-" + Guid.NewGuid().ToString("N"));
    private string File1 => Path.Combine(_dir, "gpu-startup.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact] public void Each_card_keeps_its_own_start_up_profile_and_stock_clears_it()
    {
        Assert.Empty(GpuStartup.Read(File1));   // no file: every card at stock
        GpuStartup.Set(File1, "GPU-a", "Night"); GpuStartup.Set(File1, "GPU-b", "Render");
        GpuStartup.Set(File1, "GPU-a", "Undervolt");
        Assert.Equal("Undervolt", GpuStartup.For(File1, "GPU-a")); Assert.Equal("Render", GpuStartup.For(File1, "GPU-b"));
        GpuStartup.Set(File1, "GPU-a", null);
        Assert.Null(GpuStartup.For(File1, "GPU-a")); Assert.Single(GpuStartup.Read(File1));
    }

    [Fact] public void A_damaged_file_reads_as_stock()
    {
        Directory.CreateDirectory(_dir); File.WriteAllText(File1, "{not json");
        Assert.Empty(GpuStartup.Read(File1));
    }

    [Fact] public void The_profiles_the_app_saved_are_read_as_the_app_wrote_them()
    {
        var file = Path.Combine(_dir, "gpu-profiles.json");
        var store = new JsonStore<GpuProfileDocument>(file, new SchemaMigrator([]), GpuProfileDocument.CurrentSchemaVersion, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        Directory.CreateDirectory(_dir);
        store.Save(new GpuProfileDocument { Profiles = [new("Night", GpuProfileKind.Undervolt, "GPU-a", "RTX", new GpuTuningSettings(120, 0, 1905, null, null), DateTimeOffset.UnixEpoch)] });
        var doc = GpuStartup.ReadProfiles(file)!;
        Assert.Equal(1905, Assert.Single(doc.Profiles).Settings.MaxClockMHz); Assert.Null(doc.Journal);
    }
}
