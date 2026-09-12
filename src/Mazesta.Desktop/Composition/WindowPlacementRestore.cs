using System.Windows;
using Mazesta.Persistence;

namespace Mazesta.Desktop.Composition;

/// <summary>
/// Puts a window back where the customer left it. The saved rectangle is clamped onto a screen that
/// actually exists right now: a technician's box loses and gains monitors (a second screen at the
/// bench, a TV, RDP), and a window restored onto a screen that is no longer attached is invisible
/// with no way to get it back.
/// </summary>
public static class WindowPlacementRestore
{
    /// <summary>Minimum on-screen overlap, in DIPs, before the placement is treated as off-screen.</summary>
    private const double MinVisible = 120;

    public static void Apply(Window window, WindowPlacement? saved)
    {
        if (saved is null) return;
        double width = Clamp(saved.Width, window.MinWidth, SystemParameters.VirtualScreenWidth);
        double height = Clamp(saved.Height, window.MinHeight, SystemParameters.VirtualScreenHeight);
        var target = new Rect(saved.Left, saved.Top, width, height);
        if (!IsVisibleEnough(target)) return;             // keep WPF's default placement

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = target.Left; window.Top = target.Top;
        window.Width = target.Width; window.Height = target.Height;
        if (saved.Maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>True when enough of the rectangle lands inside the current virtual desktop that the
    /// user can see and drag the window.</summary>
    internal static bool IsVisibleEnough(Rect target)
    {
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                               SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var overlap = Rect.Intersect(target, desktop);
        return !overlap.IsEmpty
            && overlap.Width >= Math.Min(MinVisible, target.Width)
            && overlap.Height >= Math.Min(MinVisible, target.Height);
    }

    internal static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || value <= 0) return min > 0 ? min : 1;
        if (max > 0 && value > max) value = max;
        return value < min ? min : value;
    }
}
