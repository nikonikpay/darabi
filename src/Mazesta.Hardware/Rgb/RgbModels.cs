namespace Mazesta.Hardware.Rgb;

/// <summary>What OpenRGB calls a device's kind (its <c>device_type</c>); only the ones a PC's parts can be are named, the rest are <see cref="Other"/>.</summary>
public enum RgbKind { Motherboard, Memory, Graphics, Cooler, Strip, Keyboard, Mouse, Storage, Case, Other }

[Flags] public enum RgbModeFlags { None = 0, Speed = 1, DirectionLr = 2, DirectionUd = 4, DirectionHv = 8, Brightness = 16, PerLedColor = 32, ModeColors = 64, RandomColor = 128 }

/// <summary>One lighting mode of a device (Static, Direct, Rainbow, Breathing...), exactly as the device reports it. <see cref="ColorMode"/> is OpenRGB's:
/// 0 none, 1 a colour for each LED, 2 colours of the mode itself, 3 random.</summary>
public sealed record RgbMode(string Name, uint Value, RgbModeFlags Flags, uint SpeedMin, uint SpeedMax, uint BrightnessMin, uint BrightnessMax, uint ColorsMin, uint ColorsMax,
    uint Speed, uint Brightness, uint Direction, uint ColorMode, IReadOnlyList<RgbColor> Colors)
{
    public bool PerLed => ColorMode == 1; public bool ModeColors => ColorMode == 2;
}

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:x2}{G:x2}{B:x2}";
    public static RgbColor? Parse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        var hexa = System.Globalization.NumberStyles.HexNumber;
        return byte.TryParse(hex.AsSpan(1, 2), hexa, null, out var r) && byte.TryParse(hex.AsSpan(3, 2), hexa, null, out var g) && byte.TryParse(hex.AsSpan(5, 2), hexa, null, out var b) ? new RgbColor(r, g, b) : null;
    }
}

/// <summary>One output of a device (a header on a board, a stick's LED strip...). OpenRGB lets a zone with <see cref="LedsMin"/> below <see cref="LedsMax"/> be resized:
/// an addressable header is described with no LEDs until it is told how many the strip plugged into it has.</summary>
public sealed record RgbZone(int Index, string Name, uint Type, uint LedsMin, uint LedsMax, uint LedsCount)
{
    public bool Resizable => LedsMax > LedsMin;
}

public sealed record RgbDevice(int Index, RgbKind Kind, string Name, string Vendor, string Description, string Location, IReadOnlyList<RgbMode> Modes, int ActiveMode, int LedCount, IReadOnlyList<RgbColor> Colors, IReadOnlyList<RgbZone> Zones);
