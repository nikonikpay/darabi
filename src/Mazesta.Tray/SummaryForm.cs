using System.Drawing.Drawing2D; using System.Runtime.InteropServices;
using Mazesta.Persistence;
namespace Mazesta.Tray;

/// <summary>
/// The tray's summary: the last CPU and GPU temperatures (with the GPU's hot spot), the drives' health, and the recent checks with any problem in
/// red, drawn in the app's ink-and-yellow with each part's own colour (CPU blue, GPU violet, storage orange, as in the app). It only shows what the
/// checks measured: a value a check could not read says so, and the time of each check is written next to it, so an old reading never passes for
/// a current one. Every length is in 96-DPI pixels scaled by <see cref="S"/>: the fonts are in points and grow with the screen's scaling, so a
/// fixed pixel layout would overlap them on a 4K screen at 150–200 %.
/// </summary>
internal sealed class SummaryForm : Form
{
    private static readonly Color Ink = Color.FromArgb(0x0C, 0x0C, 0x0C), Ink2 = Color.FromArgb(0x17, 0x17, 0x15), Paper = Color.FromArgb(0xF3, 0xF1, 0xEA),
        Paper2 = Color.FromArgb(0xC9, 0xC6, 0xBB), Paper3 = Color.FromArgb(0x8F, 0x8C, 0x82), Rule = Color.FromArgb(0x2C, 0x2B, 0x28), Yellow = Color.FromArgb(0xFD, 0xD4, 0x00),
        Fail = Color.FromArgb(0xEE, 0x4B, 0x3D), Pass = Color.FromArgb(0x2E, 0xC0, 0x8E), Cpu = Color.FromArgb(0x5B, 0xA8, 0xFF), Gpu = Color.FromArgb(0xB3, 0x8B, 0xFF),
        Storage = Color.FromArgb(0xFF, 0x9A, 0x4D);
    private readonly TrayContext _tray; private readonly Canvas _canvas; private readonly Button _check, _open;
    private readonly Font _body = new("Segoe UI", 10f), _small = new("Segoe UI", 8.75f), _bold = new("Segoe UI Semibold", 10.5f), _title = new("Segoe UI Semibold", 13f),
        _big = new("Segoe UI Semibold", 26f);

    public SummaryForm(TrayContext tray)
    {
        _tray = tray;
        Text = TrayText.SummaryTitle; RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; BackColor = Ink; ForeColor = Paper;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; ShowInTaskbar = true; StartPosition = FormStartPosition.Manual;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(S(500), S(640));
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(area.Right - Width - S(16), area.Bottom - Height - S(16));   // next to the tray, like Windows' own flyouts
        _canvas = new Canvas { Dock = DockStyle.Fill }; _canvas.Paint += (_, e) => Draw(e.Graphics);
        _check = MakeButton(TrayText.CheckNow, false); _check.Click += async (_, _) => await _tray.CheckAllAsync();
        _open = MakeButton(TrayText.OpenApp, true); _open.Click += (_, _) => _tray.OpenApp();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = S(60), Padding = new Padding(S(14), S(12), S(14), S(12)), BackColor = Ink2, FlowDirection = FlowDirection.LeftToRight };
        bar.Controls.AddRange([_open, _check]);
        Controls.Add(_canvas); Controls.Add(bar);
        _tray.Changed += OnChanged;
        FormClosed += (_, _) => _tray.Changed -= OnChanged;
        HandleCreated += (_, _) => { int on = 1; _ = DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)); };   // dark title bar
        OnChanged();
    }

    /// <summary>A length in 96-DPI pixels, at this screen's scaling.</summary>
    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    private Button MakeButton(string text, bool primary)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, S(36)), FlatStyle = FlatStyle.Flat, Font = _bold, Cursor = Cursors.Hand, Margin = new Padding(0, 0, S(10), 0),
            Padding = new Padding(S(10), 0, S(10), 0), BackColor = primary ? Yellow : Ink2, ForeColor = primary ? Ink : Paper };
        b.FlatAppearance.BorderColor = primary ? Yellow : Paper3; b.FlatAppearance.MouseOverBackColor = primary ? Paper : Rule;
        return b;
    }

    private void OnChanged()
    {
        if (IsDisposed) return;
        _check.Enabled = !_tray.IsChecking; _check.Text = _tray.IsChecking ? TrayText.Checking : TrayText.CheckNow;
        _canvas.Invalidate();
    }

    /// <summary>The height one line of <paramref name="font"/> needs, with a little air.</summary>
    private static int Line(Graphics g, Font font) => (int)Math.Ceiling(font.GetHeight(g) * 1.15);

    private void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int w = _canvas.ClientSize.Width, pad = S(18), y = S(14);
        var checks = _tray.Checks;
        var temps = checks.LastOrDefault(c => c.Kind == "temps" && c.Error is null);
        var health = checks.LastOrDefault(c => c.Kind == "health" && c.Error is null);

        int titleH = Line(g, _title), smallH = Line(g, _small), bodyH = Line(g, _body);
        Write(g, TrayText.SummaryTitle, _title, Paper, new Rectangle(pad, y, w - 2 * pad, titleH)); y += titleH;
        var last = checks.LastOrDefault();
        Write(g, _tray.IsChecking ? TrayText.Checking : TrayText.Status(last?.Time, null), _small, Paper3, new Rectangle(pad, y, w - 2 * pad, smallH)); y += smallH + S(12);

        // Two temperature tiles, each in its part's colour, with the time of the reading; the GPU's hot spot small under its core temperature.
        int tileW = (w - 2 * pad - S(12)) / 2, tileH = S(12) + bodyH + Line(g, _big) + smallH + S(10);
        Tile(g, new Rectangle(w - pad - tileW, y, tileW, tileH), Cpu, TrayText.Cpu, temps?.CpuTempC, temps?.Time, null);
        Tile(g, new Rectangle(pad, y, tileW, tileH), Gpu, TrayText.Gpu, temps?.GpuTempC, temps?.Time, temps?.GpuHotSpotC);
        y += tileH + S(16);

        // Drives, from the last health check: the verdict and the life left, and the temperature.
        Heading(g, TrayText.Drives, Storage, health?.Time, ref y, w, pad);
        if (health is null || health.Drives.Count == 0) { Write(g, TrayText.NoDrives, _body, Paper3, new Rectangle(pad, y, w - 2 * pad, bodyH)); y += bodyH + S(4); }
        else foreach (var d in health.Drives.Take(5))
        {
            var status = TrayText.DriveHealth(d) + (d.TemperatureC is { } t ? $" · {TrayText.Num(t, "0")}°C" : "");
            int statusW = S(170);
            Write(g, d.Name, _body, Paper, new Rectangle(pad + statusW + S(8), y, w - 2 * pad - statusW - S(8), bodyH), latin: true);
            Write(g, status, _body, d.NeedsAttention ? Fail : d.Status == "Healthy" ? Pass : Paper2, new Rectangle(pad, y, statusW, bodyH), alignFar: true);
            y += bodyH + S(4);
            using var rule = new Pen(Rule); g.DrawLine(rule, pad, y - S(2), w - pad, y - S(2));
        }
        y += S(10);

        // The recent checks, newest first; a problem or a failed check is red and says what it was.
        Heading(g, TrayText.Recent, Yellow, null, ref y, w, pad);
        if (checks.Count == 0) Write(g, TrayText.NoChecks, _body, Paper3, new Rectangle(pad, y, w - 2 * pad, bodyH));
        int bottom = _canvas.ClientSize.Height - S(8), timeW = S(48), whatW = S(72), textW = w - 2 * pad - timeW - whatW - S(8);
        foreach (var c in checks.Reverse())
        {
            string what = c.Kind == "health" ? TrayText.Health : TrayText.Temps;
            string result = c.Error is { } e ? $"{TrayText.CheckFailed}: {e}" : c.Problems.Count > 0 ? string.Join(" ", c.Problems) : TrayText.AllGood;
            if (c.Error is null && c.Kind == "temps" && c.Problems.Count == 0)
                result = $"CPU {(c.CpuTempC is { } ct ? TrayText.Num(ct, "0") + "°C" : "—")} · GPU {(c.GpuTempC is { } gt ? TrayText.Num(gt, "0") + "°C" : "—")}";
            var size = TextRenderer.MeasureText(g, result, _small, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.RightToLeft);
            int rowH = Math.Max(smallH, size.Height) + S(6);
            if (y + rowH > bottom) break;
            Write(g, TrayText.Time(c.Time), _small, Paper3, new Rectangle(w - pad - timeW, y, timeW, smallH), latin: true);
            Write(g, what, _small, Paper2, new Rectangle(w - pad - timeW - whatW, y, whatW - S(4), smallH));
            Write(g, result, _small, c.IsProblem ? Fail : Paper2, new Rectangle(pad, y, textW, rowH), wrap: true);
            y += rowH;
        }
    }

    private void Tile(Graphics g, Rectangle r, Color hue, string label, double? value, DateTimeOffset? time, double? hotSpot)
    {
        using (var back = new SolidBrush(Ink2)) g.FillRectangle(back, r);
        using (var top = new SolidBrush(hue)) g.FillRectangle(top, r.X, r.Y, r.Width, S(3));
        int bodyH = Line(g, _body), bigH = Line(g, _big), smallH = Line(g, _small), inner = r.Width - S(24), y = r.Y + S(10);
        Write(g, label, _body, hue, new Rectangle(r.X + S(12), y, inner, bodyH)); y += bodyH;
        if (value is { } v) Write(g, TrayText.Num(v, "0") + " °C", _big, Paper, new Rectangle(r.X + S(12), y, inner, bigH), latin: true);
        else Write(g, TrayText.NotAvailable, _bold, Paper3, new Rectangle(r.X + S(12), y + (bigH - bodyH) / 2, inner, bodyH));
        y += bigH;
        if (time is { } t) Write(g, TrayText.Time(t), _small, Paper3, new Rectangle(r.X + S(12), y, inner, smallH), latin: true);
        if (hotSpot is { } hs) Write(g, $"{TrayText.HotSpot} {TrayText.Num(hs, "0")} °C", _small, Paper2, new Rectangle(r.X + S(12), y, inner, smallH), alignFar: true);
    }

    private void Heading(Graphics g, string text, Color hue, DateTimeOffset? time, ref int y, int w, int pad)
    {
        int h = Line(g, _bold);
        using (var mark = new SolidBrush(hue)) g.FillRectangle(mark, w - pad - S(4), y + h / 4, S(4), h / 2);
        Write(g, text, _bold, Paper, new Rectangle(pad, y, w - 2 * pad - S(12), h));
        if (time is { } t) Write(g, TrayText.Time(t), _small, Paper3, new Rectangle(pad, y, S(60), h), latin: true, alignFar: true);
        y += h + S(2);
        using var rule = new Pen(Color.FromArgb(0x55, Paper)); g.DrawLine(rule, pad, y, w - pad, y);
        y += S(8);
    }

    /// <summary>Right-aligned Persian text by default; <paramref name="latin"/> runs (names, numbers) are drawn left to right, flush right in the
    /// right-to-left layout unless <paramref name="alignFar"/> puts them on the left. Text is centred in its row's height.</summary>
    private static void Write(Graphics g, string s, Font font, Color color, Rectangle r, bool latin = false, bool alignFar = false, bool wrap = false)
    {
        var flags = TextFormatFlags.NoPrefix | (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter)
            | (alignFar ? TextFormatFlags.Left : TextFormatFlags.Right) | (latin ? 0 : TextFormatFlags.RightToLeft);
        TextRenderer.DrawText(g, s, font, r, color, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _body.Dispose(); _small.Dispose(); _bold.Dispose(); _title.Dispose(); _big.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class Canvas : Control
    {
        public Canvas() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); BackColor = Ink; }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
