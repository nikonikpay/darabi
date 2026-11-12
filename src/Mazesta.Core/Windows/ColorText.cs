using System.Globalization;
namespace Mazesta.Core.Windows;

/// <summary>A picked screen colour written the three ways people paste it: #RRGGBB, rgb(r, g, b) and hsl(h, s%, l%) (whole numbers, as CSS takes them).</summary>
public static class ColorText
{
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
    public static string Rgb(byte r, byte g, byte b) => string.Create(CultureInfo.InvariantCulture, $"rgb({r}, {g}, {b})");

    public static string Hsl(byte r, byte g, byte b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0, max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min, l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double h = d == 0 ? 0 : max == R ? 60 * (((G - B) / d) % 6) : max == G ? 60 * ((B - R) / d + 2) : 60 * ((R - G) / d + 4);
        if (h < 0) h += 360;
        return string.Create(CultureInfo.InvariantCulture, $"hsl({Math.Round(h) % 360}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)");
    }
}
