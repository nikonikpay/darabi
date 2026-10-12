namespace Mazesta.Core.Tray;

/// <summary>
/// A global shortcut as the settings keep it ("Alt+U", "Ctrl+Shift+F9"): the modifiers and one key. It needs Alt, Ctrl or the Windows key (a bare letter or
/// Shift+letter would take that key from every program), and a letter, a digit, F1-F24 or one of the navigation keys.
/// </summary>
public sealed record Hotkey(int Modifiers, int VirtualKey)
{
    public const int Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;
    private static readonly (string Name, int Vk)[] Named = [("Insert", 0x2D), ("Delete", 0x2E), ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22)];

    public static Hotkey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int mods = 0, key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int m = part.ToLowerInvariant() switch { "alt" => Alt, "ctrl" or "control" => Ctrl, "shift" => Shift, "win" => Win, _ => 0 };
            if (m != 0) { mods |= m; continue; }
            if (key != 0) return null;
            key = KeyOf(part);
            if (key == 0) return null;
        }
        return key == 0 || (mods & (Alt | Ctrl | Win)) == 0 ? null : new(mods, key);
    }

    private static int KeyOf(string name)
    {
        if (name.Length == 1 && char.ToUpperInvariant(name[0]) is var c && (c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')) return c;
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out int f) && f is >= 1 and <= 24) return 0x6F + f;
        foreach (var (n, vk) in Named) if (n.Equals(name, StringComparison.OrdinalIgnoreCase)) return vk;
        return 0;
    }

    /// <summary>The text for a parsed shortcut, in the one spelling the settings keep ("Ctrl+Alt+U").</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl"); if ((Modifiers & Alt) != 0) parts.Add("Alt"); if ((Modifiers & Shift) != 0) parts.Add("Shift"); if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(VirtualKey is >= 0x70 and <= 0x87 ? "F" + (VirtualKey - 0x6F) : Named.FirstOrDefault(n => n.Vk == VirtualKey).Name ?? ((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }

    /// <summary>The same text in the spelling the settings keep, or null when it is not a usable shortcut.</summary>
    public static string? Normalize(string? text) => TryParse(text)?.ToString();
}

/// <summary>The overlay's four shortcuts: show or hide, move and resize, the next layout, the next set of items.</summary>
public sealed record OverlayHotkeys(string Toggle = "Alt+O", string Move = "Alt+M", string Layout = "Alt+U", string Preset = "Alt+P")
{
    public IReadOnlyList<Hotkey> Parsed() => [Hotkey.TryParse(Toggle)!, Hotkey.TryParse(Move)!, Hotkey.TryParse(Layout)!, Hotkey.TryParse(Preset)!];

    /// <summary>These four when every one is usable and no two are the same; otherwise the defaults, so a damaged setting never leaves the overlay without a key.</summary>
    public OverlayHotkeys Valid()
    {
        var all = new[] { Toggle, Move, Layout, Preset }.Select(Hotkey.Normalize).ToList();
        return all.Any(a => a is null) || all.Distinct().Count() != 4 ? new() : new(all[0]!, all[1]!, all[2]!, all[3]!);
    }
}
