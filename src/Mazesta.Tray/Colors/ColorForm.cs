using System.Runtime.InteropServices; using Mazesta.Core.Windows;
namespace Mazesta.Tray.Colors;

/// <summary>
/// The colour just picked, in a small window of its own (as PowerToys' picker shows it): a large swatch, its HEX, RGB and HSL each with a copy
/// button, two lighter and two darker shades, and the last colours picked (kept in a text file beside the tray's log). A swatch click shows that
/// colour; "pick again" goes back to the screen. Esc or a click elsewhere hides it.
/// </summary>
internal sealed class ColorForm : Form
{
    private const int Keep = 20;
    private static readonly Color Back = Color.FromArgb(28, 28, 32), Line = Color.FromArgb(48, 48, 54), Text1 = Color.FromArgb(235, 235, 240), Dim = Color.FromArgb(150, 150, 160);
    private readonly string _file; private readonly Panel _swatch = new() { Dock = DockStyle.Left, Width = 120 };
    private readonly TableLayoutPanel _codes = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12, 0, 0, 0) };
    private readonly FlowLayoutPanel _shades = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
    private readonly FlowLayoutPanel _recent = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
    private readonly ToolTip _tip = new(); private readonly System.Windows.Forms.Timer _settle = new() { Interval = 400 }; private long _shown; private List<Color> _history;

    public ColorForm(string file, Action again)
    {
        _file = file; _history = Read(file);
        Text = TrayText.Color; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10f); BackColor = Back; ForeColor = Text1; Padding = new Padding(14, 10, 14, 12);
        var head = new Panel { Dock = DockStyle.Top, Height = 34 };
        head.Controls.Add(new Label { Text = TrayText.Color, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 11f), RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft });   // "left" is the reading start in a right-to-left label
        head.Controls.Add(Button(TrayText.ColorAgain, again, DockStyle.Left));
        var body = new Panel { Dock = DockStyle.Top, Height = 120 };
        body.Controls.Add(_codes); body.Controls.Add(_swatch);
        _codes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); _codes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 3; i++) _codes.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
        var recentTitle = new Label { Text = TrayText.ColorRecent, Dock = DockStyle.Top, Height = 26, ForeColor = Dim, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.BottomLeft };
        Controls.Add(_recent); Controls.Add(recentTitle); Controls.Add(_shades); Controls.Add(body); Controls.Add(head);
        // the picker's closing hands the front back to the window it took it from, a moment after this one is shown: that is not a click elsewhere
        Deactivate += (_, _) => { if (Environment.TickCount64 - _shown > 700) Hide(); };
        _settle.Tick += (_, _) => { _settle.Stop(); if (Visible && !ContainsFocus) Front(); };
        HandleCreated += (_, _) => { int dark = 1, round = 2; DwmSetWindowAttribute(Handle, 20, ref dark, 4); DwmSetWindowAttribute(Handle, 33 /* corner preference: round */, ref round, 4); };
    }

    /// <summary>A new pick: kept first in the history, shown near the mouse, in front.</summary>
    public void Summon(Color c)
    {
        c = Color.FromArgb(c.R, c.G, c.B);
        _history = [c, .. _history.Where(x => x.ToArgb() != c.ToArgb()).Take(Keep - 1)];
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllLines(_file, _history.Select(x => ColorText.Hex(x.R, x.G, x.B))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ShowColor(c);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea; var size = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(300));
        Bounds = new Rectangle(Math.Clamp(Cursor.Position.X - size.Width / 2, area.X, area.Right - size.Width), Math.Clamp(Cursor.Position.Y + 24, area.Y, area.Bottom - size.Height), size.Width, size.Height);
        _shown = Environment.TickCount64; base.Show(); Front(); _settle.Start();
    }

    private void ShowColor(Color c)
    {
        _swatch.BackColor = c;
        SuspendLayout();
        foreach (Control x in _codes.Controls.Cast<Control>().Concat(_shades.Controls.Cast<Control>()).Concat(_recent.Controls.Cast<Control>()).ToList()) x.Dispose();
        string[] codes = [ColorText.Hex(c.R, c.G, c.B), ColorText.Rgb(c.R, c.G, c.B), ColorText.Hsl(c.R, c.G, c.B)];
        for (int i = 0; i < codes.Length; i++)
        {
            string code = codes[i];
            _codes.Controls.Add(new Label { Text = code, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Consolas", 11f) }, 0, i);
            Button copy = null!; copy = Button(TrayText.ColorCopy, () => { try { Clipboard.SetText(code); copy.Text = TrayText.ColorCopied; } catch (ExternalException) { } }, DockStyle.None);
            _codes.Controls.Add(copy, 1, i);
        }
        // two lighter and two darker, mixed with white and black, the colour itself in the middle
        foreach (double k in new[] { 0.5, 0.25, 0, -0.25, -0.5 }) _shades.Controls.Add(Swatch(Mix(c, k), 64, k == 0));
        foreach (var x in _history) _recent.Controls.Add(Swatch(x, 26, x.ToArgb() == c.ToArgb()));
        ResumeLayout();
    }

    private static Color Mix(Color c, double k)
    {
        int M(byte v) => (int)Math.Round(k >= 0 ? v + (255 - v) * k : v * (1 + k));
        return Color.FromArgb(M(c.R), M(c.G), M(c.B));
    }

    private Control Swatch(Color c, int width, bool current)
    {
        var b = new Button { BackColor = c, Width = LogicalToDeviceUnits(width), Height = LogicalToDeviceUnits(26), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = current ? Text1 : Line; b.FlatAppearance.BorderSize = current ? 2 : 1;
        _tip.SetToolTip(b, ColorText.Hex(c.R, c.G, c.B));
        b.Click += (_, _) => { string hex = ColorText.Hex(c.R, c.G, c.B); try { Clipboard.SetText(hex); } catch (ExternalException) { } ShowColor(c); };
        return b;
    }

    private static Button Button(string text, Action click, DockStyle dock)
    {
        var b = new Button { Text = text, Dock = dock, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Line, ForeColor = Text1, Margin = new Padding(4), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => click();
        return b;
    }

    private static List<Color> Read(string file)
    {
        try { return [.. File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length == 7 && l[0] == '#').Select(l => int.TryParse(l.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int v) ? Color.FromArgb(v >> 16 & 255, v >> 8 & 255, v & 255) : Color.Empty).Where(x => !x.IsEmpty).Take(Keep)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    protected override void Dispose(bool disposing) { if (disposing) { _tip.Dispose(); _settle.Dispose(); } base.Dispose(disposing); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keys) { if (keys == Keys.Escape) { Hide(); return true; } return base.ProcessCmdKey(ref msg, keys); }

    // shown by a shortcut pressed in another program: Windows lets a background window come forward only when it joins that program's input for a moment
    private void Front()
    {
        uint front = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool joined = front != 0 && front != me && AttachThreadInput(me, front, true);
        try { SetForegroundWindow(Handle); Activate(); } finally { if (joined) AttachThreadInput(me, front, false); }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
