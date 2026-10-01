using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices; using System.Windows.Forms;
using Mazesta.Desktop.ViewModels;
namespace Mazesta.Desktop.Views;

/// <summary>
/// The overlay's window: a layered window holding the picture <see cref="OverlayRenderer"/> draws (its own alpha, no window chrome). It never takes
/// focus and the mouse goes through it to the game or program underneath (a transparent tool window, always on top), and it sits in the chosen
/// corner of the primary screen's work area. It is drawn again only when the view model has new readings (once per monitor poll) while shown.
/// </summary>
public sealed class OverlayWindow : Form
{
    private const int WsExLayered = 0x80000, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExTopmost = 0x8;
    private OverlayViewModel _vm; private readonly bool _rtl;
    private string _corner = "TopLeft";

    public OverlayWindow(OverlayViewModel vm, bool rtl)
    {
        _vm = vm; _rtl = rtl;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; Text = "Mazesta Overlay";
        vm.Updated += OnUpdated;
    }

    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost; return p; }
    }
    protected override bool ShowWithoutActivation => true;

    /// <summary>A new view model (the items, size or layout changed): drawn at once.</summary>
    public void SetModel(OverlayViewModel vm) { _vm.Updated -= OnUpdated; _vm = vm; vm.Updated += OnUpdated; Redraw(); }

    /// <summary>TopLeft, TopRight, BottomLeft or BottomRight of the primary screen, a little in from its edges.</summary>
    public void SetCorner(string corner) { _corner = corner; Redraw(); }

    public new void Show() { if (!Visible) base.Show(); Redraw(); }

    private void OnUpdated() { if (Visible) Redraw(); }

    private void Redraw()
    {
        if (!IsHandleCreated) return;
        float dpi = DeviceDpi / 96f;
        using var bmp = OverlayRenderer.Render(_vm, _rtl, dpi * (float)_vm.Scale);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        int margin = (int)Math.Round(14 * dpi);
        int x = _corner.EndsWith("Right", StringComparison.Ordinal) ? area.Right - bmp.Width - margin : area.Left + margin;
        int y = _corner.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - bmp.Height - margin : area.Top + margin;
        Present(bmp, x, y);
    }

    /// <summary>Hands the picture to Windows with its per-pixel alpha (UpdateLayeredWindow), at its place on screen.</summary>
    private void Present(Bitmap bmp, int x, int y)
    {
        nint screen = GetDC(0), mem = CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height); var src = new POINT(0, 0); var dst = new POINT(x, y);
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
        }
        finally { SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); _ = ReleaseDC(0, screen); }
    }

    protected override void Dispose(bool disposing) { if (disposing) _vm.Updated -= OnUpdated; base.Dispose(disposing); }

    [StructLayout(LayoutKind.Sequential)] private struct POINT(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE(int cx, int cy) { public int Cx = cx, Cy = cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
}
