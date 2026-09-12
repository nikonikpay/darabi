using System.Text.Json; using System.Text.Json.Nodes; using System.Text.Json.Serialization; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence;
public enum LoadOutcome { Loaded, Defaulted, Migrated, Corrupt }
public readonly record struct LoadResult<T>(T Value, LoadOutcome Outcome, string? Detail);
public sealed class JsonStore<T>(string path, SchemaMigrator migrator, int currentVersion, ILogger logger) where T : class, IVersionedDocument, new()
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public LoadResult<T> Load()
    {
        if (!File.Exists(path)) return new(new T { SchemaVersion = currentVersion }, LoadOutcome.Defaulted, null);
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("Root is not an object.");
            int version = node["schemaVersion"]?.GetValue<int>() ?? 0;
            if (version > currentVersion) throw new JsonException($"Schema version {version} is newer than supported {currentVersion}.");
            int steps = 0; if (version < currentVersion) node = migrator.Migrate(node, currentVersion, out steps);
            var value = node.Deserialize<T>(Options) ?? throw new JsonException("Deserialised to null."); value.SchemaVersion = currentVersion;
            if (steps > 0) { Save(value); return new(value, LoadOutcome.Migrated, $"{steps} migration step(s)"); }
            return new(value, LoadOutcome.Loaded, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            string aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(path, aside, overwrite: true); } catch (IOException ioe) { logger.LogWarning(ioe, "Could not move corrupt config aside"); }
            logger.LogWarning(ex, "Config unreadable; defaults used, file moved to {Aside}", aside);
            return new(new T { SchemaVersion = currentVersion }, LoadOutcome.Corrupt, ex.Message);
        }
    }
    public void Save(T value)
    {
        value.SchemaVersion = currentVersion; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
}
