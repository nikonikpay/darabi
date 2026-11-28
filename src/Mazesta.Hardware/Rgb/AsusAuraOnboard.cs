using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// The lights soldered on ASUS boards (a VRM heatsink's) that the board's Aura controller does not count: it answers "0 mainboard LEDs", so OpenRGB
/// colours the addressable headers only, and in "Direct" mode these lights go dark (their own effects still light them). They take direct colours on a channel
/// of their own, past the headers' ones, written here to the controller itself beside each frame OpenRGB sends. Such a board gets one more zone, so a spectrum
/// or a moving effect is laid along these lights themselves. Where the channel has been seen to work only that one is written; on another ASUS board of the same
/// kind the channels past the headers are all written (a colour sent to a channel the board has not lights nothing, and nothing is saved to the board), and the
/// zone's name says the lights may not be there. No other maker's board is touched.
/// </summary>
public static class AsusAuraOnboard
{
    private static readonly (string Board, byte Channel)[] Seen = [("ROG STRIX X870E-E GAMING WIFI7 NEO", 6)];
    private static readonly byte[] Likely = [4, 5, 6, 7];
    /// <summary>The USB product ids of the Aura controllers soldered on ASUS motherboards.</summary>
    private static readonly string[] Controllers = ["18F3", "1939", "19AF", "1AA6", "1BED"];
    /// <summary>The colours these lights are given: the heatsink of the board above has three (counted on it).</summary>
    public const int Leds = 3;
    /// <summary>The lights written in one report: the colours are repeated over them for a board with more than <see cref="Leds"/>, and the controller ignores what is past its last light.</summary>
    private const int Slots = 20;
    private const int ReportSize = 65;
    public const string SeenZone = "Onboard lights", LikelyZone = "Onboard lights (if the board has them)";

    /// <summary>The controller and channels of this device's uncounted lights, or null when it has none the app can drive.</summary>
    public static RgbOnboard? Find(RgbDevice d)
    {
        if (d.Kind != RgbKind.Motherboard || d.Location.IndexOf(@"\\?\", StringComparison.Ordinal) is not (>= 0 and var at)) return null;
        string path = d.Location[at..].Trim();
        if (!Controllers.Any(id => path.Contains($"VID_0B05&PID_{id}", StringComparison.OrdinalIgnoreCase))) return null;
        foreach (var (board, channel) in Seen) if (d.Name.EndsWith(board, StringComparison.OrdinalIgnoreCase)) return new(path, [channel], Leds);
        // A zone of a fixed length is the controller's own count of the board's lights: OpenRGB colours those itself.
        return d.Zones.Count > 0 && d.Zones.All(z => z.Resizable) ? new(path, Likely, Leds) : null;
    }

    /// <summary>The device with its uncounted lights added as its last zone (their colours are the ones last written: the controller cannot be asked), or the device as it is.</summary>
    public static RgbDevice Extend(RgbDevice d, Func<string, IReadOnlyList<RgbColor>?> written)
    {
        if (Find(d) is not { } own) return d;
        var last = written(own.Path);
        var zone = new RgbZone(d.Zones.Count, own.Channels.Count == 1 ? SeenZone : LikelyZone, 1, (uint)own.Leds, (uint)own.Leds, (uint)own.Leds);
        return d with { LedCount = d.LedCount + own.Leds, Zones = [.. d.Zones, zone], Colors = [.. d.Colors, .. Enumerable.Range(0, own.Leds).Select(i => last is not null && i < last.Count ? last[i] : default)], Onboard = own };
    }

    /// <summary>The report that colours a channel and shows it at once.</summary>
    public static byte[] Report(byte channel, IReadOnlyList<RgbColor> colors)
    {
        var report = new byte[ReportSize]; report[0] = 0xEC; report[1] = 0x40; report[2] = (byte)(0x80 | channel); report[4] = Slots;
        for (int i = 0; i < Slots && colors.Count > 0; i++)
        {
            var c = colors[i % colors.Count]; report[5 + i * 3] = c.R; report[6 + i * 3] = c.G; report[7 + i * 3] = c.B;
        }
        return report;
    }

    /// <summary>Colours the uncounted lights. False when the controller could not be written to.</summary>
    public static bool Paint(RgbOnboard own, IReadOnlyList<RgbColor> colors)
    {
        if (colors.Count == 0) return false;
        using var handle = CreateFileW(own.Path, 0xC0000000 /* read and write */, 3 /* shared: OpenRGB holds it too */, 0, 3 /* open existing */, 0, 0);
        if (handle.IsInvalid) return false;
        bool all = true;
        foreach (byte channel in own.Channels) all &= WriteFile(handle, Report(channel, colors), ReportSize, out _, 0);
        return all;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, byte[] data, int count, out int written, nint overlapped);
}
