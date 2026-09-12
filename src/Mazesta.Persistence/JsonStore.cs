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
        T? migrated = null; int migrationSteps = 0;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("Root is not an object.");
            int version = node["schemaVersion"]?.GetValue<int>() ?? 0;
            if (version > currentVersion) throw new JsonException($"Schema version {version} is newer than supported {currentVersion}.");
            int steps = 0; if (version < currentVersion) node = migrator.Migrate(node, currentVersion, out steps);
            var value = node.Deserialize<T>(Options) ?? throw new JsonException("Deserialised to null."); value.SchemaVersion = currentVersion;
            migrated = value; migrationSteps = steps;
            if (steps == 0) return new(value, LoadOutcome.Loaded, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(path, aside, overwrite: true); } catch (Exception ioe) when (ioe is IOException or UnauthorizedAccessException) { logger.LogWarning(ioe, "Could not move corrupt config aside"); }
            logger.LogWarning(ex, "Config unreadable; defaults used, file moved to {Aside}", aside);
            return new(new T { SchemaVersion = currentVersion }, LoadOutcome.Corrupt, ex.Message);
        }
        // The migration re-save happens OUTSIDE the try above on purpose: a write that fails (the
        // data folder is read-only, the file is held open) says nothing about the file being
        // corrupt. Classifying it as Corrupt would move the customer's settings aside and lose
        // them. The migration itself succeeded, so the outcome stays Migrated either way.
        bool saved = Save(migrated!);
        return new(migrated!, LoadOutcome.Migrated, saved ? $"{migrationSteps} migration step(s)" : $"{migrationSteps} migration step(s); could not write the migrated file back");
    }
    /// <summary>Writes the document atomically. Returns false (and logs) instead of throwing when
    /// the file cannot be written: callers are UI paths such as window close and the settings page,
    /// where an unwritable data folder must not take the app down.</summary>
    public bool Save(T value)
    {
        try
        {
            value.SchemaVersion = currentVersion; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not save {Path}", path);
            return false;
        }
    }
}
