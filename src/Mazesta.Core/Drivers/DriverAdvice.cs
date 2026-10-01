namespace Mazesta.Core.Drivers;

public enum NvidiaLine { GameReady, Studio }

/// <summary>Which NVIDIA driver line suits the computer, and why: the creative programs and the game launchers found installed (by the names
/// Windows lists them under). <see cref="Suggested"/> is null when both kinds are there: then the user chooses, and the page says what each is for.</summary>
public sealed record DriverAdviceResult(NvidiaLine? Suggested, IReadOnlyList<string> Creative, IReadOnlyList<string> Games);

/// <summary>
/// Game Ready or Studio, from what is installed. NVIDIA says both carry the same features and fixes; Game Ready comes out with each big game,
/// Studio less often, after testing with creative programs. So creative programs and no games suggest Studio, games and no creative programs Game
/// Ready, both suggest nothing (the user decides), and neither suggests Game Ready, NVIDIA's own default for a GeForce card.
/// </summary>
public static class DriverAdvice
{
    // Matched at the start of a word of the program's installed name, so "Blender" finds "Blender 4.2" but "Steam" does not find "Steamworks Common".
    private static readonly string[] CreativeNames =
    [
        "Lumion", "Twinmotion", "D5 Render", "Enscape", "Chaos Vantage", "V-Ray", "Corona Renderer", "Chaos Corona", "3ds Max", "Autodesk 3ds Max", "Maya", "Autodesk Maya",
        "Blender", "Cinema 4D", "Maxon Cinema 4D", "Houdini", "Revit", "Autodesk Revit", "Archicad", "SketchUp", "AutoCAD", "Rhino", "Rhinoceros", "Unreal Engine",
        "Adobe Premiere", "Adobe After Effects", "Adobe Photoshop", "Adobe Illustrator", "Adobe Substance", "Adobe Lightroom", "Adobe Media Encoder", "DaVinci Resolve",
        "Substance 3D", "ZBrush", "KeyShot", "Marvelous Designer", "SOLIDWORKS", "Autodesk Inventor", "Autodesk Fusion", "CATIA", "Redshift", "Octane", "Unity Hub", "Nuke", "Mari",
    ];
    private static readonly string[] GameNames =
    [
        "Steam", "Epic Games Launcher", "Battle.net", "EA app", "Origin", "Ubisoft Connect", "Uplay", "GOG Galaxy", "Riot Client", "Riot Vanguard", "Rockstar Games Launcher",
        "Call of Duty", "Valorant", "League of Legends", "Counter-Strike", "Fortnite", "Minecraft Launcher", "Roblox", "Genshin Impact", "Wargaming.net Game Center",
    ];

    public static DriverAdviceResult Advise(IEnumerable<string> installed)
    {
        var names = installed.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var creative = Found(names, CreativeNames); var games = Found(names, GameNames);
        NvidiaLine? line = creative.Count > 0 && games.Count == 0 ? NvidiaLine.Studio : creative.Count > 0 ? null : NvidiaLine.GameReady;
        return new(line, creative, games);
    }

    private static List<string> Found(List<string> installed, string[] wanted) =>
        [.. installed.Where(n => wanted.Any(w => StartsWord(n, w))).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(12)];

    private static bool StartsWord(string name, string word)
    {
        for (int at = name.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = name.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            bool start = at == 0 || !char.IsLetterOrDigit(name[at - 1]);
            int end = at + word.Length;
            bool stop = end == name.Length || !char.IsLetter(name[end]);
            if (start && stop) return true;
        }
        return false;
    }
}
