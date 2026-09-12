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
}
