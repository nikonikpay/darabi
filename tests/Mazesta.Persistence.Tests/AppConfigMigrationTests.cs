using Xunit;
using System.Text.Json.Nodes; using Mazesta.Persistence;
namespace Mazesta.Persistence.Tests;
public class AppConfigMigrationTests
{
    [Fact] public void V0_document_migrates_pollSeconds_to_fastIntervalSeconds()
    {
        var doc = JsonNode.Parse("""{"pollSeconds": 5, "language": "fa", "unknownThing": 1}""")!.AsObject();
        var migrated = new SchemaMigrator([new Migration0To1()]).Migrate(doc, 1, out var steps);
        Assert.Equal(1, steps); Assert.Equal(5, (int)migrated["fastIntervalSeconds"]!); Assert.Equal(1, (int)migrated["schemaVersion"]!); Assert.Null(migrated["pollSeconds"]); Assert.Equal("fa", (string)migrated["language"]!);
    }
    [Fact] public void Migrator_throws_when_a_step_is_missing()
        => Assert.Throws<InvalidOperationException>(() => new SchemaMigrator([]).Migrate(JsonNode.Parse("""{"schemaVersion":0}""")!.AsObject(), 1, out _));

    [Fact] public void V1_document_with_the_old_english_default_is_reset_to_persian_and_keeps_everything_else()
    {
        var doc = JsonNode.Parse("""{"schemaVersion":1,"language":"en","fastIntervalSeconds":5,"shopName":"x"}""")!.AsObject();
        var migrated = new SchemaMigrator(AppConfig.Migrations).Migrate(doc, 2, out var steps);
        Assert.Equal(1, steps); Assert.Equal("fa", (string)migrated["language"]!); Assert.Equal(5, (int)migrated["fastIntervalSeconds"]!); Assert.Equal("x", (string)migrated["shopName"]!);
    }
}
