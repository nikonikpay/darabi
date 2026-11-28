using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// The lights soldered on a few ASUS boards (a VRM heatsink's) that the board's Aura controller does not count: it answers "0 mainboard LEDs", so OpenRGB
/// colours the addressable headers only, and in "Direct" mode these lights go dark (their own effects still light them). They take direct colours on a channel
/// of their own, written here to the controller itself beside each frame OpenRGB sends. Only boards on which that channel was seen to work are listed:
/// nothing is sent to any other.
/// </summary>
public static class AsusAuraOnboard
{
    private static readonly (string Board, byte Channel)[] Boards = [("ROG STRIX X870E-E GAMING WIFI7 NEO", 6)];
    /// <summary>More than the heatsink has: the controller ignores colours past the last light.</summary>
    public const int Leds = 20;
    private const int ReportSize = 65;

    /// <summary>The channel of this device's uncounted lights and the path of its controller, or null when the device is not a listed board.</summary>
    public static (string Path, byte Channel)? Find(RgbDevice d)
    {
        if (d.Location.IndexOf(@"\\?\", StringComparison.Ordinal) is not (>= 0 and var at)) return null;
        foreach (var (board, channel) in Boards) if (d.Name.EndsWith(board, StringComparison.OrdinalIgnoreCase)) return (d.Location[at..].Trim(), channel);
        return null;
    }

    /// <summary>The report that colours the channel and shows it at once: the device's colours sampled evenly along their length, so an effect that moves across the board moves here too.</summary>
    public static byte[] Report(byte channel, IReadOnlyList<RgbColor> colors)
    {
        var report = new byte[ReportSize]; report[0] = 0xEC; report[1] = 0x40; report[2] = (byte)(0x80 | channel); report[4] = Leds;
        for (int i = 0; i < Leds && colors.Count > 0; i++)
        {
            var c = colors[i * colors.Count / Leds]; report[5 + i * 3] = c.R; report[6 + i * 3] = c.G; report[7 + i * 3] = c.B;
        }
        return report;
    }

    /// <summary>Colours the device's uncounted lights when it is a listed board. False when it is not, or its controller could not be written to.</summary>
    public static bool Paint(RgbDevice d, IReadOnlyList<RgbColor> colors)
    {
        if (colors.Count == 0 || Find(d) is not { } found) return false;
        using var handle = CreateFileW(found.Path, 0xC0000000 /* read and write */, 3 /* shared: OpenRGB holds it too */, 0, 3 /* open existing */, 0, 0);
        return !handle.IsInvalid && WriteFile(handle, Report(found.Channel, colors), ReportSize, out _, 0);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, byte[] data, int count, out int written, nint overlapped);
}
