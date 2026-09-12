using Xunit;
using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Persistence.Tests;
public class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tests-" + Guid.NewGuid().ToString("N"));
    public JsonStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private JsonStore<AppConfig> Store() => new(Path.Combine(_dir, "appconfig.json"), new SchemaMigrator([new Migration0To1()]), AppConfig.CurrentSchemaVersion, NullLogger.Instance);
    [Fact] public void Missing_file_returns_defaults()
    { var r = Store().Load(); Assert.Equal(LoadOutcome.Defaulted, r.Outcome); Assert.Equal(2, r.Value.FastIntervalSeconds); Assert.Equal("en", r.Value.Language); }
    [Fact] public void Save_then_load_round_trips_and_leaves_no_temp_file()
    {
        var s = Store(); s.Save(new AppConfig { Language = "fa", ShopName = "فروشگاه", ExpandedGroups = ["cpu/intelcpu-0"] });
        var r = s.Load(); Assert.Equal((LoadOutcome.Loaded, "fa", "فروشگاه"), (r.Outcome, r.Value.Language, r.Value.ShopName)); Assert.Equal(["cpu/intelcpu-0"], r.Value.ExpandedGroups);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
    [Fact] public void Corrupt_file_is_renamed_aside_and_defaults_used()
    {
        File.WriteAllText(Path.Combine(_dir, "appconfig.json"), "{ not json");
        var r = Store().Load(); Assert.Equal(LoadOutcome.Corrupt, r.Outcome); Assert.Single(Directory.GetFiles(_dir, "appconfig.json.corrupt-*")); Assert.Equal(2, r.Value.FastIntervalSeconds);
    }
    [Fact] public void Newer_schema_than_supported_is_treated_as_corrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "appconfig.json"), """{"schemaVersion": 99}"""); Assert.Equal(LoadOutcome.Corrupt, Store().Load().Outcome);
    }
    [Fact] public void V0_file_on_disk_loads_as_migrated_and_is_resaved()
    {
        string path = Path.Combine(_dir, "appconfig.json");
        File.WriteAllText(path, """{"pollSeconds": 5, "language": "fa"}""");
        var r = Store().Load();
        Assert.Equal(LoadOutcome.Migrated, r.Outcome); Assert.Equal(5, r.Value.FastIntervalSeconds); Assert.Equal("fa", r.Value.Language);
        string onDisk = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", onDisk); Assert.DoesNotContain("pollSeconds", onDisk);
    }
    [Fact] public void Locked_file_is_treated_as_corrupt_not_a_crash()
    {
        string path = Path.Combine(_dir, "appconfig.json");
        Store().Save(new AppConfig());
        using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        LoadResult<AppConfig> result = default;
        var ex = Record.Exception(() => result = Store().Load());
        Assert.Null(ex); Assert.Equal(LoadOutcome.Corrupt, result.Outcome);
    }
}
