using System.Drawing; using System.Windows.Forms;
using Mazesta.Persistence;

namespace Mazesta.Desktop.Composition;

/// <summary>
/// Puts a window back where the customer left it. The saved rectangle is clamped onto a screen that
/// actually exists right now: a technician's box loses and gains monitors (a second screen at the
/// bench, a TV, RDP), and a window restored onto a screen that is no longer attached is invisible
/// with no way to get it back. The placement is kept in device-independent pixels (as the WPF edition
/// saved it), so a saved placement reads the same at any display scale.
/// </summary>
public static class WindowPlacementRestore
{
    /// <summary>Minimum on-screen overlap, in pixels, before the placement is treated as off-screen.</summary>
    private const double MinVisible = 120;

    public static void Apply(Form window, WindowPlacement? saved)
    {
        if (saved is null) return;
        double k = window.DeviceDpi / 96.0;
        var desktop = SystemInformation.VirtualScreen;
        double width = Clamp(saved.Width * k, window.MinimumSize.Width, desktop.Width);
        double height = Clamp(saved.Height * k, window.MinimumSize.Height, desktop.Height);
        var target = new RectangleF((float)(saved.Left * k), (float)(saved.Top * k), (float)width, (float)height);
        if (!IsVisibleEnough(target)) return;             // keep Windows's default placement

        window.StartPosition = FormStartPosition.Manual;
        window.Bounds = Rectangle.Round(target);
        if (saved.Maximized) window.WindowState = FormWindowState.Maximized;
    }

    /// <summary>Where the window is now, for saving: its normal place when maximised, in device-independent pixels.</summary>
    public static WindowPlacement Capture(Form window)
    {
        double k = window.DeviceDpi / 96.0; bool maximized = window.WindowState == FormWindowState.Maximized;
        var b = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
        return new WindowPlacement(b.Left / k, b.Top / k, b.Width / k, b.Height / k, maximized);
    }

    /// <summary>True when enough of the rectangle lands inside the current virtual desktop that the
    /// user can see and drag the window.</summary>
    internal static bool IsVisibleEnough(RectangleF target)
    {
        var overlap = RectangleF.Intersect(target, SystemInformation.VirtualScreen);
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
