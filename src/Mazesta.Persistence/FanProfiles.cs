using System.Text.Json; using Mazesta.Core.Fans;
namespace Mazesta.Persistence;

/// <summary>What the user set for one fan output: automatic (the board's own control), a fixed duty, or a curve of temperature against duty.</summary>
public sealed class FanSetting { public string Mode { get; set; } = "auto"; public double Percent { get; set; } = 50; public string Source { get; set; } = "cpu"; public List<FanPoint> Points { get; set; } = []; }

/// <summary>
/// The user's fan profiles, in <c>Data/config/fan-profiles.json</c>: the profile in force, the ones the user saved (a setting for each output), and what the user said each
/// output is (its name and kind, which no board can report reliably: the headers' names differ with every board). A file of its own beside <c>fans.json</c> (the settings of
/// each output); the tray reads it for its menu.
/// </summary>
public sealed class FanProfiles
{
    /// <summary>The file the tray writes a profile name into for the app to put on (the app deletes it once it has).</summary>
    public const string RequestFile = "fan-request.json";
    /// <summary>The ready-made profiles, by name: "auto" gives every output back to the board; the others are curves (see <c>FanCurve.Preset</c>).</summary>
    public static readonly string[] Builtin = ["auto", "silent", "standard", "performance", "full"];
    public static string FileIn(AppPaths paths) => Path.Combine(paths.ConfigDir, "fan-profiles.json");
    public static void Request(AppPaths paths, string name) { Directory.CreateDirectory(paths.ConfigDir); File.WriteAllText(Path.Combine(paths.ConfigDir, RequestFile), JsonSerializer.Serialize(new { name })); }

    public string Active { get; set; } = "auto";
    public Dictionary<string, Dictionary<string, FanSetting>> Custom { get; set; } = [];
    public Dictionary<string, FanLabel> Labels { get; set; } = [];

    private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static FanProfiles Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<FanProfiles>(File.ReadAllText(file), Opt) is { } p ? p.Fixed() : new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
    }
    public void Write(string file) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); string tmp = file + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt)); File.Move(tmp, file, overwrite: true); }
    private FanProfiles Fixed() { Custom ??= []; Labels ??= []; if (string.IsNullOrWhiteSpace(Active)) Active = "auto"; return this; }
}

/// <summary>A name and a kind ("cpu", "pump", "case") the user gave an output, or the page found by measuring it.</summary>
public sealed class FanLabel { public string? Name { get; set; } public string? Kind { get; set; } }

