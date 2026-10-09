using System.Text.Json; using Mazesta.Core.Tuning;
namespace Mazesta.Persistence;

/// <summary>
/// The automatic GPU profiles (<see cref="GpuRules"/>) in <c>Data/config/gpu-rules.json</c>: the app's tuning page writes them and the tray, which is always on,
/// follows them. A file of its own for the reason <see cref="GpuStartup"/> is one; replaced whole through a temporary file; a missing or damaged file reads as no rules.
/// </summary>
public static class GpuRulesFile
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static string FileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "gpu-rules.json");

    public static GpuRules Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<GpuRules>(File.ReadAllText(file), Json) ?? GpuRules.None : GpuRules.None; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return GpuRules.None; }
    }

    public static void Write(string file, GpuRules rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(rules, Json));
        File.Move(tmp, file, overwrite: true);
    }
}
