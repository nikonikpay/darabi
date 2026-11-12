namespace Mazesta.Core.Windows;

/// <summary>Sizes and names for the picture resizer: the longest side brought down to a limit (never up), and a new file beside the original.</summary>
public static class ImageFit
{
    public static readonly int[] Presets = [800, 1280, 1920, 2560];

    /// <summary>The new size with the shape kept; a picture already within the limit keeps its size.</summary>
    public static (int Width, int Height) Fit(int width, int height, int longest)
    {
        if (width <= 0 || height <= 0 || longest <= 0) throw new ArgumentOutOfRangeException(nameof(longest));
        int side = Math.Max(width, height);
        if (side <= longest) return (width, height);
        double k = (double)longest / side;
        return (Math.Max(1, (int)Math.Round(width * k)), Math.Max(1, (int)Math.Round(height * k)));
    }

    /// <summary>"photo.jpg" -> "photo (1280).jpg" beside it, or "photo (1280) (2).jpg" when that is taken: the original is never written over.</summary>
    public static string OutputPath(string source, int longest, string extension, Func<string, bool> exists)
    {
        string dir = Path.GetDirectoryName(source) ?? "", name = Path.GetFileNameWithoutExtension(source);
        string path = Path.Combine(dir, $"{name} ({longest}){extension}");
        for (int n = 2; exists(path); n++) path = Path.Combine(dir, $"{name} ({longest}) ({n}){extension}");
        return path;
    }

    /// <summary>JPEG stays JPEG and PNG stays PNG (its transparency kept); BMP and TIFF become JPEG, a GIF becomes PNG.</summary>
    public static string OutputExtension(string source) => Path.GetExtension(source).ToLowerInvariant() switch
    {
        ".png" or ".gif" => ".png", ".jpeg" => ".jpeg", _ => ".jpg",
    };
}
