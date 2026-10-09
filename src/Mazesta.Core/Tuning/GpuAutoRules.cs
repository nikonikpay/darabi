namespace Mazesta.Core.Tuning;

/// <summary>A graphics program (Lumion, Chaos Vantage, 3ds Max...) and the saved GPU profile the card is put in while it is the program in use.</summary>
public sealed record GpuAppRule(string Exe, string Name, string Profile);

/// <summary>Which saved profile goes with a game (any full-screen game) and with each listed program. A null game profile leaves the card as it is during games.</summary>
public sealed record GpuRules(string? GameProfile, IReadOnlyList<GpuAppRule> Apps)
{
    public static readonly GpuRules None = new(null, []);
    public bool IsEmpty => GameProfile is null && Apps.Count == 0;
}

/// <summary>Which profile the card should be in now. The listed program in front wins; then a full-screen game; then a listed program running behind;
/// otherwise null: the card goes back to its own start-up profile.</summary>
public static class GpuAutoRules
{
    /// <param name="running">The executables running now, lower case with ".exe".</param>
    /// <param name="foreground">The executable owning the window in front, or null.</param>
    /// <param name="fullScreenGame">The window in front fills the screen as a game does (Windows' own "running a full-screen game" state).</param>
    public static string? Decide(GpuRules rules, IReadOnlySet<string> running, string? foreground, bool fullScreenGame)
    {
        GpuAppRule? Match(string? exe) => exe is null ? null : rules.Apps.FirstOrDefault(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));
        if (Match(foreground) is { } front) return front.Profile;
        if (fullScreenGame && rules.GameProfile is { } game) return game;
        return rules.Apps.FirstOrDefault(a => running.Contains(a.Exe.ToLowerInvariant()))?.Profile;
    }

    /// <summary>"C:\\Apps\\Lumion\\Lumion.exe", a quoted path or a registry icon value ("path,0") to the executable's file name; null when it is not an executable.</summary>
    public static string? ExeName(string? pathOrIcon)
    {
        if (string.IsNullOrWhiteSpace(pathOrIcon)) return null;
        string p = pathOrIcon.Trim().Trim('"'); int comma = p.LastIndexOf(',');
        if (comma > 0 && int.TryParse(p[(comma + 1)..], out _)) p = p[..comma];
        p = p.Trim().Trim('"');
        string name = p.Replace('/', '\\').Split('\\')[^1];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4 ? name : null;
    }
}
