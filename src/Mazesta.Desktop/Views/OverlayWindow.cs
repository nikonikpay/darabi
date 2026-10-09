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
    private int? _x, _y; private bool _editing, _resizing; private Point _grab; private double _startScale; private int _startWidth, _width, _height; private long _lastResize;
    private const int GripSize = 28;
    /// <summary>The overlay was put down somewhere with the mouse (screen pixels).</summary>
    public event Action<int, int>? Placed;
    /// <summary>The mouse asks for another size (the wheel, or the corner pulled): the scale it would have.</summary>
    public event Action<double>? Resized;
    public bool Editing => _editing;

    public OverlayWindow(OverlayViewModel vm, bool rtl)
    {
        _vm = vm; _rtl = rtl;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; Text = "Mazesta Overlay";
        vm.Updated += OnUpdated;
        // Another topmost window (a game, the test's full-screen window) that was raised after this one sits above it: "topmost" is an order among the topmost
        // windows, and the last one raised is the front one. So while shown the overlay raises itself again, without taking focus.
        _raise = new System.Windows.Forms.Timer { Interval = 400 }; _raise.Tick += (_, _) => Raise();
    }
    private readonly System.Windows.Forms.Timer _raise;
    private void Raise() { if (IsHandleCreated && Visible) SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); _raise.Enabled = Visible; if (Visible) Raise(); }

    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost; return p; }
    }
    protected override bool ShowWithoutActivation => true;

    /// <summary>A new view model (the items, size or layout changed): drawn at once.</summary>
    public void SetModel(OverlayViewModel vm) { _vm.Updated -= OnUpdated; _vm = vm; vm.Updated += OnUpdated; Redraw(); }

    /// <summary>TopLeft, TopRight, BottomLeft or BottomRight of the primary screen, a little in from its edges.</summary>
    public void SetCorner(string corner) { _corner = corner; Redraw(); }

    /// <summary>A place chosen with the mouse, or null to sit in the corner again.</summary>
    public void SetPlace(int? x, int? y) { _x = x; _y = y; Redraw(); }

    /// <summary>In this mode the mouse reaches the overlay: drag it to move it, the wheel or the corner grip sets its size. Out of it the mouse goes through again.</summary>
    public void SetEditing(bool on)
    {
        if (_editing == on || !IsHandleCreated) return;
        _editing = on; _resizing = false;
        if (on && (_x is null || _y is null)) { _x = Left; _y = Top; }   // it stays where it is: the mode does not move it
        int style = GetWindowLong(Handle, GwlExStyle);
        SetWindowLong(Handle, GwlExStyle, on ? style & ~WsExTransparent : style | WsExTransparent);
        Cursor = on ? Cursors.SizeAll : Cursors.Default;
        Redraw();
    }

    private bool InGrip(Point p) => p.X >= _width - GripSize && p.Y >= _height - GripSize;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_editing || e.Button != MouseButtons.Left) return;
        if (InGrip(e.Location)) { _resizing = true; _startScale = _vm.Scale; _startWidth = Math.Max(1, _width); } else _grab = e.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_editing) return;
        if (!Capture) { Cursor = InGrip(e.Location) ? Cursors.SizeNWSE : Cursors.SizeAll; return; }
        if (_resizing)
        {
            // Pulled by the corner: the width follows the pointer; the picture is made again at most a dozen times a second.
            long now = Environment.TickCount64; if (now - _lastResize < 80) return; _lastResize = now;
            Resized?.Invoke(Math.Clamp(_startScale * (MousePosition.X - Left) / _startWidth, 0.7, 1.6));
            return;
        }
        _x = MousePosition.X - _grab.X; _y = MousePosition.Y - _grab.Y;
        SetWindowPos(Handle, 0, _x.Value, _y.Value, 0, 0, SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_editing || !Capture) return;
        Capture = false;
        if (_resizing) { _resizing = false; Resized?.Invoke(Math.Clamp(_startScale * (MousePosition.X - Left) / _startWidth, 0.7, 1.6)); }
        else if (_x is { } x && _y is { } y) Placed?.Invoke(x, y);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_editing) Resized?.Invoke(Math.Clamp(Math.Round(_vm.Scale + Math.Sign(e.Delta) * 0.05, 2), 0.7, 1.6));
    }

    public new void Show() { if (!Visible) base.Show(); Redraw(); }

    private void OnUpdated() { if (Visible) Redraw(); }

    private void Redraw()
    {
        if (!IsHandleCreated) return;
        float dpi = DeviceDpi / 96f;
        using var bmp = OverlayRenderer.Render(_vm, _rtl && !_vm.English, dpi * (float)_vm.Scale);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        int margin = (int)Math.Round(14 * dpi);
        int x = _corner.EndsWith("Right", StringComparison.Ordinal) ? area.Right - bmp.Width - margin : area.Left + margin;
        int y = _corner.StartsWith("Bottom", StringComparison.Ordinal) ? area.Bottom - bmp.Height - margin : area.Top + margin;
        if (_x is { } px && _y is { } py)
        {
            // Where the mouse put it, kept on the screens there are now (a monitor that was unplugged must not leave it out of reach).
            var all = SystemInformation.VirtualScreen;
            x = Math.Clamp(px, all.Left, Math.Max(all.Left, all.Right - bmp.Width)); y = Math.Clamp(py, all.Top, Math.Max(all.Top, all.Bottom - bmp.Height));
        }
        _width = bmp.Width; _height = bmp.Height;
        if (_editing)
        {
            // The mode shows itself: a faint wash over the whole picture (so the mouse reaches all of it, not just the letters), a dashed edge and the size grip.
            using var g = Graphics.FromImage(bmp);
            using (var wash = new SolidBrush(Color.FromArgb(40, 0xFD, 0xD4, 0x00))) g.FillRectangle(wash, 0, 0, bmp.Width, bmp.Height);
            using (var edge = new Pen(Color.FromArgb(0xFD, 0xD4, 0x00), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(edge, 1, 1, bmp.Width - 3, bmp.Height - 3);
            using var grip = new SolidBrush(Color.FromArgb(0xFD, 0xD4, 0x00));
            g.FillPolygon(grip, [new Point(bmp.Width - 2, bmp.Height - GripSize + 8), new Point(bmp.Width - 2, bmp.Height - 2), new Point(bmp.Width - GripSize + 8, bmp.Height - 2)]);
        }
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

    protected override void Dispose(bool disposing) { if (disposing) { _vm.Updated -= OnUpdated; _raise.Dispose(); } base.Dispose(disposing); }

    [StructLayout(LayoutKind.Sequential)] private struct POINT(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE(int cx, int cy) { public int Cx = cx, Cy = cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    private static readonly nint HwndTopmost = -1; private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpNoOwnerZOrder = 0x200;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private const int GwlExStyle = -20;
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
}
