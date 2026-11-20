using System.Drawing.Drawing2D; using System.Runtime.InteropServices;
namespace Mazesta.Tray;

/// <summary>
/// The look of the tray's windows, taken from the app (app.css): the same ink greys, the yellow of the logo and the part colours, so a window
/// opened from the tray or from Explorer's menu is recognisably Mazesta's. One place for the colours, a banner with an icon, flat buttons,
/// chips for options, a readable drop-down and a list with its own header (the system's dark header had text nobody could read).
/// Native on purpose: a web view per window would add browser processes and hundreds of MB to a tray that is meant to stay small.
/// </summary>
internal static class Theme
{
    // The app's own typeface (Vazirmatn, which the page loads as a web font): the setup installs it for Windows, and when it is not installed the copy
    // beside the exe is loaded for this process alone, so GDI and GDI+ both find it. Without either, Segoe UI.
    // GDI+ finds a family by name only among the installed fonts, so a font loaded for this process alone is held in a collection and fonts are made from its
    // families (the semi-bold is the variable font's own "SemiBold" instance). The same file is also added to GDI (FR_PRIVATE), because the controls draw their
    // text with GDI (TextRenderer, the list views) and look the face up by name there.
    private static readonly System.Drawing.Text.PrivateFontCollection? Own = LoadOwn();
    public static readonly string Face = Own is null ? "Segoe UI" : "Vazirmatn";
    private static System.Drawing.Text.PrivateFontCollection? LoadOwn()
    {
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", "Vazirmatn-Variable.ttf");
            if (!File.Exists(file) || AddFontResourceEx(file, 0x10 /* FR_PRIVATE */, 0) == 0) return null;
            var c = new System.Drawing.Text.PrivateFontCollection(); c.AddFontFile(file); return c.Families.Any(f => f.Name == "Vazirmatn") ? c : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, nint reserved);
    private static Font Make(float size, bool semi, GraphicsUnit unit)
    {
        if (Own is null) return new Font(Face, size, semi ? FontStyle.Bold : FontStyle.Regular, unit);
        var family = Own.Families.FirstOrDefault(f => f.Name == (semi ? "Vazirmatn SemiBold" : "Vazirmatn")) ?? Own.Families[0];
        return new Font(family, size, FontStyle.Regular, unit);
    }
    public static Font Reg(float size, FontStyle style = FontStyle.Regular) => Make(size, (style & FontStyle.Bold) != 0, GraphicsUnit.Point);
    public static Font Reg(float size, GraphicsUnit unit) => Make(size, false, unit);
    public static Font Semi(float size) => Make(size, true, GraphicsUnit.Point);
    public static Font Semi(float size, GraphicsUnit unit) => Make(size, true, unit);

    // Windows 11's own dark greys (what PowerToys' windows use: window #202020, fields #2B2B2B, list card #272727), neutral and not the app's near-black or bluish grey,
    // so a window beside other programs sits quietly; the accent is the logo's yellow where PowerToys uses the system accent
    public static readonly Color Ink = Color.FromArgb(0x20, 0x20, 0x20), Ink2 = Color.FromArgb(0x27, 0x27, 0x27), Ink3 = Color.FromArgb(0x2D, 0x2D, 0x2D), Ink4 = Color.FromArgb(0x3A, 0x3A, 0x3A),
        Well = Color.FromArgb(0x27, 0x27, 0x27), Paper = Color.FromArgb(0xF3, 0xF3, 0xF3), Paper2 = Color.FromArgb(0xCF, 0xCF, 0xCF), Paper3 = Color.FromArgb(0x9B, 0x9B, 0x9B), Rule = Color.FromArgb(0x45, 0x45, 0x45),
        Yellow = Color.FromArgb(0xFD, 0xD4, 0x00), OnYellow = Color.FromArgb(0x0C, 0x0C, 0x0C), Pass = Color.FromArgb(0x2E, 0xC0, 0x8E), Fail = Color.FromArgb(0xEE, 0x4B, 0x3D), Warn = Color.FromArgb(0xF2, 0xA1, 0x22),
        Cpu = Color.FromArgb(0x5B, 0xA8, 0xFF), Gpu = Color.FromArgb(0xB3, 0x8B, 0xFF), Ram = Color.FromArgb(0x35, 0xD0, 0xE0), Storage = Color.FromArgb(0xFF, 0x9A, 0x4D), Net = Color.FromArgb(0xFF, 0x78, 0xB9),
        Select = Color.FromArgb(0x57, 0x4F, 0x1C), Stripe = Color.FromArgb(0x2B, 0x2B, 0x2B);

    // Segoe MDL2 Assets glyphs (on every Windows 10 and 11)
    public const string Lock = "", Edit = "", Picture = "", Drive = "", Search = "", Volume = "";

    /// <summary>The window's frame and colours: a dark title bar, the app's ink, the app's text size.</summary>
    public static void Window(Form f)
    {
        f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = Theme.Reg(10f); f.BackColor = Ink; f.ForeColor = Paper;
        f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        f.HandleCreated += (_, _) => { int on = 1; _ = DwmSetWindowAttribute(f.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4); };
    }

    /// <summary>The top band of a window: a round icon in the tool's colour, the title, and one line saying what it is for.</summary>
    public static Control Banner(string glyph, Color accent, string title, string? subtitle) => new BannerPanel(glyph, accent, title, subtitle);

    private sealed class BannerPanel : Panel
    {
        private readonly string _glyph, _title, _sub; private readonly Color _accent;
        private static readonly Bitmap? Logo = LoadLogo();
        public BannerPanel(string glyph, Color accent, string title, string? sub)
        {
            _glyph = glyph; _accent = accent; _title = title; _sub = sub ?? ""; Dock = DockStyle.Top; Height = 70; DoubleBuffered = true;
            ResizeRedraw = true;   // without it a drag of the window's edge left old copies of the title behind
        }
        private static Bitmap? LoadLogo() { try { return Icon.ExtractIcon(Application.ExecutablePath, 0, 128)?.ToBitmap() ?? Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch (Exception ex) when (ex is ArgumentException or IOException) { return null; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 8 || Height < 8) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var back = new LinearGradientBrush(ClientRectangle, Yellow, Color.FromArgb(0xF0, 0xC4, 0x00), LinearGradientMode.Horizontal)) g.FillRectangle(back, ClientRectangle);
            using (var line = new SolidBrush(Color.FromArgb(0xB8, 0x92, 0x00))) g.FillRectangle(line, 0, Height - 3, Width, 3);
            // right: the Mazesta logo on a dark disc; beside it the tool's own symbol
            int d = Height - 24, x = Width - 16 - d, y = 10, d2 = d - 8, x2 = x - 10 - d2, y2 = y + 4;
            using (var disc = new SolidBrush(Ink)) g.FillEllipse(disc, x2, y2, d2, d2);
            if (Logo is not null) g.DrawImage(Logo, x, y, d, d);   // the logo as it is: a dark ring round it read as a grey border
            using (var gf = new Font("Segoe MDL2 Assets", d2 * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel)) using (var tint = new SolidBrush(_accent))
            {
                var sz = g.MeasureString(_glyph, gf); g.DrawString(_glyph, gf, tint, x2 + (d2 - sz.Width) / 2 + 1, y2 + (d2 - sz.Height) / 2 + 1);
            }
            int room = x2 - 28; if (room < 20) return;
            using var rtl = new StringFormat(StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap) { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
            using var tf = Theme.Semi(15f, GraphicsUnit.Point); using var sf = Theme.Reg(9.5f, GraphicsUnit.Point);
            using (var p = new SolidBrush(OnYellow)) g.DrawString(_title, tf, p, new RectangleF(16, 10, room, 30), rtl);
            using (var p2 = new SolidBrush(Color.FromArgb(0x4A, 0x3C, 0x00))) g.DrawString(_sub, sf, p2, new RectangleF(16, 40, room, 24), rtl);
        }
    }

    public static Button Button(string text, bool primary = false, string glyph = "") => new RoundButton(text, primary, glyph);

    /// <summary>A button with round corners, drawn here (the flat system button is square and its text sits wherever the system puts it); an icon beside the text when given.</summary>
    private sealed class RoundButton : Button
    {
        private readonly bool _primary, _hasIcon; private readonly string _glyph; private bool _hot; private static readonly Font IconFont = new("Segoe MDL2 Assets", 11f);
        public RoundButton(string text, bool primary, string glyph)
        {
            _primary = primary; _glyph = glyph; _hasIcon = glyph.Length > 0; Text = text; AutoSize = true; MinimumSize = new Size(0, 36); Cursor = Cursors.Hand; Margin = new Padding(4); Font = Theme.Semi(10f);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        public override Size GetPreferredSize(Size proposed) => new(TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, 40), TextFormatFlags.NoPrefix).Width + 34 + (_hasIcon ? 26 : 0), 36);
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Ink);
            Color fill = !Enabled ? Ink3 : _primary ? (_hot ? Color.FromArgb(0xFF, 0xE2, 0x4A) : Yellow) : (_hot ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Ink4);
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 9)) { using var b = new SolidBrush(fill); g.FillPath(b, path); if (Focused && ShowFocusCues) { using var pen = new Pen(Paper3); g.DrawPath(pen, path); } }
            Color fore = !Enabled ? Paper3 : _primary ? OnYellow : Paper; bool rtl = RightToLeft == RightToLeft.Yes; int icon = _hasIcon ? 26 : 0;
            if (_hasIcon) TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(rtl ? Width - 14 - 18 : 14, 0, 18, Height), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, rtl ? new Rectangle(0, 0, Width - icon, Height) : new Rectangle(icon, 0, Width - icon, Height), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath(); int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }

    /// <summary>A panel with round corners (and a border that turns yellow while <see cref="Active"/> says so): the frame of a text box or a list.</summary>
    public sealed class RoundPanel : Panel
    {
        public Color Fill = Ink3, Edge = Rule; public int Radius = 10; public Func<bool>? Active;
        public RoundPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Ink);
            using var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
            using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
            bool on = Active?.Invoke() == true; using var pen = new Pen(on ? Yellow : Edge, on ? 2 : 1); g.DrawPath(pen, path);
        }
    }

    private static readonly Font ChipFont = Theme.Reg(10f);

    /// <summary>An option as a chip with an icon: dim when off, yellow when on (a plain check box is a white square on the dark window).</summary>
    public static CheckBox Chip(string text, string glyph = "") => new ChipBox(text, glyph);

    private sealed class ChipBox : CheckBox
    {
        private readonly string _glyph; private bool _hot; private static readonly Font IconFont = new("Segoe MDL2 Assets", 11f);
        public ChipBox(string text, string glyph)
        {
            _glyph = glyph; Text = text; Appearance = Appearance.Button; AutoSize = false; Font = ChipFont; Height = 32; Cursor = Cursors.Hand; Margin = new Padding(0, 3, 5, 3);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            // sized by hand: the automatic size of a button-looking check box cut the end off a long right-to-left text
            Width = TextRenderer.MeasureText(text, ChipFont, new Size(int.MaxValue, 30), TextFormatFlags.NoPrefix).Width + 20 + (glyph.Length > 0 ? 22 : 0);
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Ink);
            Color fill = Checked ? Yellow : _hot ? Ink4 : Ink3, edge = Checked ? Yellow : Rule, fore = !Enabled ? Paper3 : Checked ? OnYellow : Paper2;
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 10)) { using (var b = new SolidBrush(fill)) g.FillPath(b, path); using var pen = new Pen(edge); g.DrawPath(pen, path); }
            bool rtl = RightToLeft == RightToLeft.Yes; int pad = 9, icon = _glyph.Length > 0 ? 22 : 0;
            if (icon > 0) TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(rtl ? Width - pad - 18 : pad, 0, 18, Height), fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var text = rtl ? Rectangle.FromLTRB(pad, 0, Width - pad - icon, Height) : Rectangle.FromLTRB(pad + icon, 0, Width - pad, Height);
            TextRenderer.DrawText(g, Text, Font, text, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left));
        }
    }

    /// <summary>
    /// A caption for a group (<paramref name="strong"/>, yellow) or a field: its icon at the right edge, the text beside it reading from the right, and, when
    /// <paramref name="help"/> is given, a round "?" at the left edge that opens the explanation in a small card.
    /// </summary>
    public static Control Caption(string glyph, string text, bool strong = false, string? help = null, int width = 370) => new CaptionBar(glyph, text, strong, help, width);

    private sealed class CaptionBar : Control
    {
        private readonly string _glyph, _text; private readonly bool _strong; private readonly string? _help; private bool _hot;
        private static readonly Font IconFont = new("Segoe MDL2 Assets", 11f);
        private Rectangle Q => new(0, (Height - 24) / 2, 24, 24);
        public CaptionBar(string glyph, string text, bool strong, string? help, int width)
        {
            _glyph = glyph; _text = text; _strong = strong; _help = help; Width = width; Height = strong ? 32 : 26; RightToLeft = RightToLeft.Yes;
            Margin = strong ? new Padding(0, 8, 0, 2) : new Padding(0, 6, 0, 0); Font = strong ? Theme.Semi(10.5f) : Theme.Reg(9.5f);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseMove(MouseEventArgs e) { bool on = _help is not null && Q.Contains(e.Location); if (on != _hot) { _hot = on; Cursor = on ? Cursors.Hand : Cursors.Default; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (_help is not null && Q.Contains(e.Location)) ShowHelp(this, _text, _help); base.OnMouseClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Ink);
            Color tone = _strong ? Yellow : Paper2;
            TextRenderer.DrawText(g, _glyph, IconFont, new Rectangle(Width - 22, 0, 20, Height), _strong ? Yellow : Paper3, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _text, Font, Rectangle.FromLTRB(_help is null ? 0 : 30, 0, Width - 28, Height), tone, TextFormatFlags.Right | TextFormatFlags.RightToLeft | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_help is null) return;
            using (var b = new SolidBrush(_hot ? Yellow : Ink4)) g.FillEllipse(b, Q);
            TextRenderer.DrawText(g, "", IconFont, Q, _hot ? OnYellow : Paper, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>The explanation card under a caption's "?": a title and a few short lines, closing when anything else is clicked.</summary>
    private static void ShowHelp(Control owner, string title, string text)
    {
        const int w = 360; var body = Theme.Reg(10f);
        int h = TextRenderer.MeasureText(text, body, new Size(w - 32, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft).Height;
        var card = new RoundPanel { Width = w, Height = h + 62, Fill = Ink3, Edge = Yellow, Radius = 10, Padding = new Padding(16, 10, 16, 12), RightToLeft = RightToLeft.Yes };
        card.Controls.Add(new Label { Dock = DockStyle.Fill, Text = text, Font = body, ForeColor = Paper, BackColor = Ink3, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.TopLeft });
        card.Controls.Add(new Label { Dock = DockStyle.Top, Height = 30, Text = title, Font = Theme.Semi(10.5f), ForeColor = Yellow, BackColor = Ink3, RightToLeft = RightToLeft.Yes, TextAlign = ContentAlignment.MiddleLeft });
        var drop = new ToolStripDropDown { AutoClose = true, Padding = Padding.Empty, BackColor = Ink, DropShadowEnabled = true };
        drop.Items.Add(new ToolStripControlHost(card) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = card.Size });
        drop.Closed += (_, _) => drop.Dispose();
        drop.Show(owner, new Point(0, owner.Height));
    }

    public static void Field(TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Ink3; t.ForeColor = Paper; }

    /// <summary>A text (or number) box inside a round frame that rings yellow while it has the focus: what <see cref="Field"/> cannot be, a box with round corners.</summary>
    public static RoundPanel Boxed(Control inner, int width, int height = 36)
    {
        inner.BackColor = Ink3; inner.ForeColor = Paper; inner.Dock = DockStyle.Fill;
        if (inner is TextBox t) t.BorderStyle = BorderStyle.None; else if (inner is NumericUpDown n) n.BorderStyle = BorderStyle.None;
        var p = new RoundPanel { Width = width, Height = height, Fill = Ink3, Radius = 8, Padding = new Padding(10, (height - 20) / 2, 10, 0), Margin = new Padding(0, 4, 0, 2) };
        p.Active = () => inner.ContainsFocus; inner.GotFocus += (_, _) => p.Invalidate(); inner.LostFocus += (_, _) => p.Invalidate();
        p.Controls.Add(inner); return p;
    }

    // in a right-to-left drop-down Persian text reads from the right edge; Latin text ("1280 px") sits at the same edge but keeps its own order
    private static TextFormatFlags Side(ComboBox c, string text) =>
        c.RightToLeft != RightToLeft.Yes ? TextFormatFlags.Left : text.Any(ch => ch >= '؀' && ch <= 'ۿ') ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Right;

    /// <summary>A drop-down drawn by hand: the system's draws the chosen row in blue with dark text.</summary>
    public static void Combo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList; c.FlatStyle = FlatStyle.Flat; c.BackColor = Ink3; c.ForeColor = Paper; c.DrawMode = DrawMode.OwnerDrawFixed; c.ItemHeight = 26;
        _ = new ComboSkin(c);
        c.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(hot ? Select : Ink3)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, c.GetItemText(c.Items[e.Index]), c.Font, Rectangle.Inflate(e.Bounds, -6, 0), hot ? Yellow : Paper, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Side(c, c.GetItemText(c.Items[e.Index]) ?? ""));
        };
    }

    /// <summary>
    /// A details list drawn by hand: a header with readable text (and an arrow on the sorted column, when <paramref name="sort"/> says which), rows with
    /// a faint stripe, and the chosen row in yellow. Icons and the indent of a tree row (the item's image and <see cref="ListViewItem.IndentCount"/>) are drawn here too.
    /// </summary>
    public static void List(ListView lv, Func<(int Column, bool Ascending)?>? sort = null, Action<int>? clicked = null, int flex = -1)
    {
        lv.OwnerDraw = true; lv.BackColor = Well; lv.ForeColor = Paper; lv.BorderStyle = BorderStyle.None; lv.FullRowSelect = true; lv.HeaderStyle = ColumnHeaderStyle.Clickable;   // a non-clickable header was painted by the system, white, and skipped DrawColumnHeader
        lv.SmallImageList ??= new ImageList { ImageSize = new Size(16, 26) };
        lv.ColumnClick += (_, e) => clicked?.Invoke(e.Column);
        // one column (the last, unless told) takes what the others leave, so no bare (white) stretch of the system's header shows after it. One routine, guarded:
        // setting a width raises the width event, and the old two-way handlers fed each other, so the columns kept growing and shrinking while the window was dragged.
        bool busy = false;
        void Fill()
        {
            if (busy || lv.Columns.Count < 2 || !lv.IsHandleCreated) return;
            int at = flex < 0 ? lv.Columns.Count - 1 : flex, others = 0; for (int i = 0; i < lv.Columns.Count; i++) if (i != at) others += lv.Columns[i].Width;
            int want = Math.Max(80, lv.ClientSize.Width - others);
            if (want == lv.Columns[at].Width) return;
            busy = true; try { lv.Columns[at].Width = want; } finally { busy = false; }
        }
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lv, true);
        lv.ClientSizeChanged += (_, _) => Fill(); lv.HandleCreated += (_, _) => Fill(); lv.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex != (flex < 0 ? lv.Columns.Count - 1 : flex)) Fill(); };
        HeaderSkin.Attach(lv, sort);
        var head = Theme.Semi(9.5f);
        lv.DrawColumnHeader += (_, e) =>
        {
            var g = e.Graphics; using (var b = new SolidBrush(Ink3)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Yellow, 2)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using (var p = new Pen(Rule)) g.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 6, e.Bounds.Right - 1, e.Bounds.Bottom - 8);
            var s = sort?.Invoke(); bool on = s is { } x && x.Column == e.ColumnIndex; string arrow = on ? (s!.Value.Ascending ? "  ▲" : "  ▼") : "";
            var align = e.Header!.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(g, e.Header.Text + arrow, head, Rectangle.Inflate(e.Bounds, -8, 0), on ? Yellow : Paper, align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        lv.DrawItem += (_, _) => { };
        lv.DrawSubItem += (_, e) =>
        {
            var g = e.Graphics; var item = e.Item!; bool chosen = item.Selected;
            Color back = chosen ? Select : e.ItemIndex % 2 == 0 ? Well : Stripe;
            using (var b = new SolidBrush(back)) g.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.Inflate(-8, 0);
            if (e.ColumnIndex == 0)
            {
                if (chosen) using (var bar = new SolidBrush(Yellow)) g.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
                int indent = item.IndentCount * 18; r.X += indent; r.Width -= indent;
                if (lv.SmallImageList is { } il && item.ImageIndex >= 0 && item.ImageIndex < il.Images.Count) { g.DrawImage(il.Images[item.ImageIndex], r.X, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16); r.X += 22; r.Width -= 22; }
            }
            var align = lv.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            Color fore = item.ForeColor == SystemColors.WindowText || item.ForeColor.IsEmpty ? Paper : item.ForeColor;
            TextRenderer.DrawText(g, e.SubItem!.Text, lv.Font, r, chosen && fore == Paper ? Color.White : fore, align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }

    /// <summary>Paints over the system's flat drop-down after it has drawn itself: a border all round (it had none at the arrow side) and an arrow button in the theme's greys.</summary>
    /// <summary>
    /// The list's header painted here, whole (columns, the stretch after the last one, the yellow rule): the list view's own owner-draw call for the header
    /// came only for virtual lists, and the others showed the system's white header.
    /// </summary>
    private sealed class HeaderSkin : NativeWindow
    {
        private readonly ListView _lv; private readonly Func<(int Column, bool Ascending)?>? _sort; private static readonly Font Head = Theme.Semi(9.5f);
        private HeaderSkin(ListView lv, nint header, Func<(int Column, bool Ascending)?>? sort)
        {
            _lv = lv; _sort = sort; AssignHandle(header);
            lv.ColumnClick += (_, _) => { try { lv.BeginInvoke(() => InvalidateRect(Handle, 0, true)); } catch (InvalidOperationException) { } };
            lv.HandleDestroyed += (_, _) => ReleaseHandle();
        }
        public static void Attach(ListView lv, Func<(int Column, bool Ascending)?>? sort)
        {
            void Go() { nint h = SendMessage(lv.Handle, 0x101F /* LVM_GETHEADER */, 0, 0); if (h != 0) _ = new HeaderSkin(lv, h, sort); }
            lv.HandleCreated += (_, _) => Go(); if (lv.IsHandleCreated) Go();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x14 /* WM_ERASEBKGND */) { m.Result = 1; return; }
            if (m.Msg == 0xF /* WM_PAINT */) { Paint(); ValidateRect(Handle, 0); m.Result = 0; return; }
            base.WndProc(ref m);
        }
        private void Paint()
        {
            GetClientRect(Handle, out var rc); if (rc.Right < 2 || rc.Bottom < 2) return;
            using var bmp = new Bitmap(rc.Right, rc.Bottom);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Ink3);
                using (var p = new Pen(Yellow, 2)) g.DrawLine(p, 0, rc.Bottom - 1, rc.Right, rc.Bottom - 1);
                var s = _sort?.Invoke();
                for (int i = 0; i < _lv.Columns.Count; i++)
                {
                    var r = new Rc(); if (SendMessage(Handle, 0x1207 /* HDM_GETITEMRECT */, i, ref r) == 0) continue;
                    var col = _lv.Columns[i]; bool on = s is { } x && x.Column == i;
                    using (var p = new Pen(Rule)) g.DrawLine(p, r.Right - 1, r.Top + 6, r.Right - 1, r.Bottom - 8);
                    string text = col.Text + (on ? (s!.Value.Ascending ? "  ▲" : "  ▼") : "");
                    TextRenderer.DrawText(g, text, Head, Rectangle.Inflate(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom - 2), -8, 0), on ? Yellow : Paper,
                        (col.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }
            using var wg = Graphics.FromHwnd(Handle); wg.DrawImageUnscaled(bmp, 0, 0);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Rc { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern nint SendMessage(nint w, int msg, nint wp, nint lp);
        [DllImport("user32.dll")] private static extern nint SendMessage(nint w, int msg, nint wp, ref Rc lp);
        [DllImport("user32.dll")] private static extern bool GetClientRect(nint w, out Rc r);
        [DllImport("user32.dll")] private static extern bool ValidateRect(nint w, nint r);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(nint w, nint r, bool erase);
    }

    private sealed class ComboSkin : NativeWindow
    {
        private readonly ComboBox _c;
        public ComboSkin(ComboBox c)
        {
            _c = c; c.HandleCreated += (_, _) => AssignHandle(c.Handle); c.HandleDestroyed += (_, _) => ReleaseHandle();
            if (c.IsHandleCreated) AssignHandle(c.Handle);
            c.GotFocus += (_, _) => c.Invalidate(); c.LostFocus += (_, _) => c.Invalidate(); c.DropDown += (_, _) => c.Invalidate(); c.DropDownClosed += (_, _) => c.Invalidate();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0xF /* WM_PAINT */ && _c.DropDownStyle == ComboBoxStyle.DropDownList) { Draw(); m.Result = 0; return; }
            base.WndProc(ref m);
        }
        // the whole control drawn here: the system's flat one has a white arrow button and no border at that side
        private void Draw()
        {
            nint hdc = BeginPaint(_c.Handle, out var ps);
            try
            {
                var info = new ComboInfo { Size = Marshal.SizeOf<ComboInfo>() }; GetComboBoxInfo(_c.Handle, ref info);
                using var g = Graphics.FromHdc(hdc); g.SmoothingMode = SmoothingMode.AntiAlias; var all = new Rectangle(0, 0, _c.Width, _c.Height);
                g.Clear(_c.Parent?.BackColor ?? Ink);   // the corners outside the rounded face take the window's colour
                var b = Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom);
                if (b.Width < 4) b = _c.RightToLeft == RightToLeft.Yes ? new Rectangle(0, 0, 28, _c.Height) : new Rectangle(_c.Width - 28, 0, 28, _c.Height);
                using var face = RoundRect(new Rectangle(0, 0, _c.Width - 1, _c.Height - 1), 8);
                using (var fill = new SolidBrush(_c.Enabled ? Ink3 : Ink2)) g.FillPath(fill, face);
                int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
                using (var chev = new Pen(_c.Enabled ? Paper2 : Paper3, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(chev, [new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2)]);
                bool rtl = _c.RightToLeft == RightToLeft.Yes; var text = rtl ? Rectangle.FromLTRB(b.Right + 2, 0, _c.Width - 6, _c.Height) : Rectangle.FromLTRB(6, 0, b.Left - 2, _c.Height);
                TextRenderer.DrawText(g, _c.Text, _c.Font, text, _c.Enabled ? Paper : Paper3, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Side(_c, _c.Text));
                using var edge = new Pen(_c.Focused || _c.DroppedDown ? Yellow : Rule);
                g.DrawPath(edge, face);
            }
            finally { EndPaint(_c.Handle, ref ps); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Ps { public nint Dc; public bool Erase; public Rc Paint; public bool Restore, Update; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved; }
        [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out Ps ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref Ps ps);
        [StructLayout(LayoutKind.Sequential)] private struct Rc { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ComboInfo { public int Size; public Rc Item, Button; public int State; public nint Combo, Edit, List; }
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(nint window, ref ComboInfo info);
    }

    /// <summary>The scroll bars dark too (Windows' own dark theme for them).</summary>
    public static void DarkScroll(Control c) => c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string app, string? id);
}
