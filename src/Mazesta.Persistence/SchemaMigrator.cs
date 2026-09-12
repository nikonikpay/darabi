using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public interface IMigration { int From { get; } JsonObject Apply(JsonObject document); }
public sealed class SchemaMigrator(IReadOnlyList<IMigration> migrations)
{
    public JsonObject Migrate(JsonObject doc, int target, out int appliedSteps)
    {
        appliedSteps = 0; int version = doc["schemaVersion"]?.GetValue<int>() ?? 0;
        while (version < target)
        {
            var step = migrations.FirstOrDefault(m => m.From == version) ?? throw new InvalidOperationException($"No migration from schema version {version}.");
            doc = step.Apply(doc); doc["schemaVersion"] = version + 1; version++; appliedSteps++;
        }
        return doc;
    }
}
