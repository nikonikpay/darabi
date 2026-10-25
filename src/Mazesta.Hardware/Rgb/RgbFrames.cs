using Mazesta.Core.Rgb;
namespace Mazesta.Hardware.Rgb;

/// <summary>The colours of each LED of a device at one moment of an effect, for a device that has not the effect itself and is driven by the app. Pure: the same inputs
/// give the same frame, so a frame can be tested without a light.</summary>
public static class RgbFrames
{
    /// <summary>One frame. <paramref name="seconds"/> is the time since the effect began; speed and brightness are percents. Rainbow and Cycle always run the whole colour
    /// wheel; only Flow moves the palette (two or more colours) the user chose.</summary>
    public static RgbColor[] Frame(string effect, double seconds, int leds, RgbColor color, IReadOnlyList<RgbColor>? palette, int speedPercent, int brightnessPercent, string? direction = null)
    {
        leds = Math.Max(leds, 1); var frame = new RgbColor[leds];
        double rate = 0.2 + 1.8 * Math.Clamp(speedPercent, 0, 100) / 100.0, level = Math.Clamp(brightnessPercent, 0, 100) / 100.0;
        double dir = RgbEffects.Reverse(direction) ? 1 : -1;   // the pattern runs towards the last LED (ltr, ttb) or back (rtl, btt)
        switch (effect)
        {
            case RgbEffects.Rainbow: for (int i = 0; i < leds; i++) frame[i] = Hue(Frac((double)i / leds + dir * seconds * rate * 0.5)); break;
            case RgbEffects.Cycle: Array.Fill(frame, Hue(Frac(seconds * rate * 0.25))); break;
            case RgbEffects.Flow when palette is { Count: > 1 }: for (int i = 0; i < leds; i++) frame[i] = Sample(palette, Frac((double)i / leds + dir * seconds * rate * 0.5), loop: true); break;
            case RgbEffects.Breathing: Array.Fill(frame, Scale(color, 0.5 - 0.5 * Math.Cos(2 * Math.PI * seconds * rate * 0.5))); break;
            case RgbEffects.Flash: Array.Fill(frame, Frac(seconds * rate) < 0.5 ? color : new RgbColor(0, 0, 0)); break;
            default: Array.Fill(frame, color); break;
        }
        return level >= 1 ? frame : [.. frame.Select(c => Scale(c, level))];
    }

    /// <summary>A still gradient along a strip, first colour at its start and last at its end.</summary>
    public static RgbColor[] Gradient(IReadOnlyList<RgbColor> palette, int leds)
        => [.. Enumerable.Range(0, Math.Max(leds, 1)).Select(i => palette.Count == 1 ? palette[0] : Sample(palette, leds <= 1 ? 0 : (double)i / (leds - 1), loop: false))];

    /// <summary>A point along a run of colours (0 to 1); with <paramref name="loop"/> the last colour runs back into the first.</summary>
    internal static RgbColor Sample(IReadOnlyList<RgbColor> palette, double pos, bool loop)
    {
        int n = palette.Count; if (n == 1) return palette[0];
        double at = loop ? Frac(pos) * n : Math.Clamp(pos, 0, 1) * (n - 1); int i = Math.Min((int)at, loop ? n - 1 : n - 2); double t = at - i;
        var a = palette[i]; var b = palette[loop ? (i + 1) % n : i + 1];
        return new RgbColor(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
    private static double Frac(double x) => x - Math.Floor(x);
    private static RgbColor Scale(RgbColor c, double f) { f = Math.Clamp(f, 0, 1); return new((byte)Math.Round(c.R * f), (byte)Math.Round(c.G * f), (byte)Math.Round(c.B * f)); }

    /// <summary>Full-strength hue (0 to 1 round the wheel).</summary>
    internal static RgbColor Hue(double h)
    {
        h = Frac(h) * 6; int i = (int)h; double f = h - i, q = 1 - f;
        (double r, double g, double b) = i switch { 0 => (1d, f, 0d), 1 => (q, 1d, 0d), 2 => (0d, 1d, f), 3 => (0d, q, 1d), 4 => (f, 0d, 1d), _ => (1d, 0d, q) };
        return new((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
