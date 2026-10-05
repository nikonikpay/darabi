using System.Text.Json; using Mazesta.Core.Rgb;
namespace Mazesta.Persistence;

/// <summary>
/// The lights' scene on disk (<c>Data/config/rgb-scene.json</c>). A file of its own, written whole through a temporary file, because the tray and the app
/// both read it and either may write it; a damaged or missing file reads as the empty scene (nothing is applied). The LED counts an earlier version kept in
/// <c>rgb-zones.json</c> are taken over once.
/// </summary>
public static class RgbSceneStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static string FileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "rgb-scene.json");

    public static RgbScene Read(AppPaths paths)
    {
        try
        {
            string file = FileIn(paths);
            if (File.Exists(file)) return JsonSerializer.Deserialize<RgbScene>(File.ReadAllText(file), Json) is { } s ? Fixed(s) : new();
            string old = Path.Combine(paths.ConfigDir, "rgb-zones.json");
            if (File.Exists(old)) return Fixed(new RgbScene { Zones = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(old)) ?? [] });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return new();
    }

    public static void Write(AppPaths paths, RgbScene scene)
    {
        string file = FileIn(paths); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(scene, Json)); File.Move(tmp, file, overwrite: true);
    }

    private static RgbScene Fixed(RgbScene s) { s.Devices ??= []; s.Zones ??= []; return s; }
}
