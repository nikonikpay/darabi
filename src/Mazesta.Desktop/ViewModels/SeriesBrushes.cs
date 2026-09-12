using System.Windows;
using System.Windows.Media;
using Mazesta.Core.Hardware;

namespace Mazesta.Desktop.ViewModels;

/// <summary>
/// Resolves the per-hardware-kind accent brush. A <see cref="HardwareKind"/> with no
/// `Brush.Series.*` entry in the theme (a kind added later, a theme that has not caught up) must not
/// throw a ResourceReferenceKeyNotFoundException out of a property getter during rendering, so the
/// lookup falls back to the muted text brush and finally to a plain grey.
/// </summary>
internal static class SeriesBrushes
{
    private static readonly Brush Fallback = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAE));

    public static Brush For(HardwareKind kind)
    {
        var app = Application.Current;
        if (app is null) return Fallback;
        return app.TryFindResource($"Brush.Series.{kind}") as Brush
            ?? app.TryFindResource("Brush.TextMuted") as Brush
            ?? Fallback;
    }
}
