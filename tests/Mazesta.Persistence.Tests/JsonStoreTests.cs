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
}
