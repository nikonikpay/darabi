namespace Mazesta.Core.Rgb;

/// <summary>
/// The lighting effects every device is offered, whatever it calls its own: a device that has the effect runs it itself; one that has not but can take a colour for
/// each LED is driven frame by frame by the app (simulated), so a choice made for all the lights reaches all of them. What the device calls an effect differs by maker
/// ("Rainbow", "Rainbow Wave", "Spectrum Cycle", "Pulse"...), so each effect has the names it is known by.
/// </summary>
public static class RgbEffects
{
    // Flow: the colours the user chose (a gradient of two to four) moved along the strip; Rainbow: the whole spectrum moved along it; Cycle: the whole strip through the spectrum, one colour after another.
    public const string Static = "Static", Flow = "Flow", Breathing = "Breathing", Rainbow = "Rainbow", Cycle = "Cycle", Flash = "Flash";
    /// <summary>Left to right, right to left, top to bottom, bottom to top. A strip has one line: left to right and top to bottom run from its first LED to its last, the other two back.</summary>
    public static readonly IReadOnlyList<string> Directions = ["ltr", "rtl", "ttb", "btt"];
    public static bool Reverse(string? direction) => direction is "rtl" or "btt";
    public static readonly IReadOnlyList<string> All = [Static, Flow, Breathing, Rainbow, Cycle, Flash];

    private static readonly Dictionary<string, string[]> Names = new()
    {
        [Breathing] = ["Breathing", "Breathe", "Pulse", "Pulsing"],
        [Rainbow] = ["Rainbow", "Rainbow Wave", "Rainbow Wheel", "Spectrum Wave"],
        [Cycle] = ["Spectrum Cycle", "Color Cycle", "Colour Cycle", "Color Shift", "Rainbow Cycle", "Cycle"],
        [Flash] = ["Flashing", "Flash", "Strobe", "Blink"],
    };

    /// <summary>The effect of this id ("rainbow" in any case), or null when the name is not one of ours (a device's own effect, asked for by its name).</summary>
    public static string? Universal(string? name) => All.FirstOrDefault(e => e.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The name a device gives this effect, from the modes it lists (the first of the effect's names it has), or null when it has none of them.</summary>
    public static string? NativeName(IEnumerable<string> deviceModes, string effect)
    {
        if (!Names.TryGetValue(effect, out var names)) return null;
        var have = deviceModes.ToList();
        return names.Select(n => have.FirstOrDefault(m => m.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(m => m is not null);
    }

    /// <summary>Whether a device's mode name is only another name for one of the effects above (the page lists those once, as the effect).</summary>
    public static bool Covered(string mode) => Universal(mode) is not null || Names.Values.Any(n => n.Any(x => x.Equals(mode, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>A named run of colours the user can pick: laid along a strip it is a still gradient; an effect that moves through colours moves through these.</summary>
public sealed record RgbPalette(string Id, IReadOnlyList<string> Colors);

public static class RgbPalettes
{
    public static readonly IReadOnlyList<RgbPalette> All =
    [
        new("sunset", ["#ff512f", "#f09819", "#dd2476"]),
        new("fire", ["#ff0000", "#ff7a00", "#ffd400"]),
        new("ocean", ["#00e5ff", "#0072ff", "#0a2fff"]),
        new("ice", ["#e0f7ff", "#00b4ff", "#2979ff"]),
        new("forest", ["#00e676", "#00a651", "#b8e000"]),
        new("aurora", ["#00e676", "#00e5ff", "#8e44ff"]),
        new("neon", ["#ff00cc", "#3333ff", "#00f0ff"]),
        new("candy", ["#ff2bd6", "#ffd1f0", "#8ee8ff"]),
        new("royal", ["#8e44ff", "#2979ff", "#ffd400"]),
        new("lava", ["#ff0000", "#7a00ff", "#ff7a00"]),
    ];

    /// <summary>A named palette, or one the page made from a colour the user chose ("custom:#rrggbb,#rrggbb,...": two to six colours, as in the harmony chips).</summary>
    public static RgbPalette? Find(string? id)
    {
        if (id is { } c && c.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            var colors = c[7..].Split(','); return colors.Length is >= 2 and <= 6 && colors.All(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^#[0-9a-fA-F]{6}$")) ? new(c, colors) : null;
        }
        return All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>How the lights were before the app put them out at sleep or shutdown, kept in <c>rgb-before-off.json</c> until they are put back: the app and the lights'
/// own memory do not agree after a power cycle, so the way back is written down. One entry for each device by its key.</summary>
public sealed class RgbSnapshot
{
    public Dictionary<string, RgbSnapshotDevice> Devices { get; set; } = [];
}

public sealed class RgbSnapshotDevice
{
    public string Mode { get; set; } = "";
    public uint Speed { get; set; }
    public uint Brightness { get; set; }
    public uint Direction { get; set; }
    public List<string> ModeColors { get; set; } = [];
    public List<string> Leds { get; set; } = [];
}
