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
    public static readonly string Face = LoadFace();
    private static string LoadFace()
    {
        try
        {
            if (FontFamily.Families.Any(f => f.Name == "Vazirmatn")) return "Vazirmatn";
            string file = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", "Vazirmatn-Variable.ttf");
            if (File.Exists(file) && AddFontResourceEx(file, 0x10 /* FR_PRIVATE */, 0) > 0) return "Vazirmatn";
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        return "Segoe UI";
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, nint reserved);
    public static Font Reg(float size, FontStyle style = FontStyle.Regular) => new(Face, size, style);
    public static Font Reg(float size, GraphicsUnit unit) => new(Face, size, FontStyle.Regular, unit);
    public static Font Semi(float size) => new(Face, size, FontStyle.Bold);
    public static Font Semi(float size, GraphicsUnit unit) => new(Face, size, FontStyle.Bold, unit);

    // a mid grey (not the app's near-black): the windows are small tools beside other programs, and the near-black ones read as heavy
    public static readonly Color Ink = Color.FromArgb(0x2B, 0x2F, 0x36), Ink2 = Color.FromArgb(0x33, 0x38, 0x40), Ink3 = Color.FromArgb(0x3E, 0x44, 0x4D), Ink4 = Color.FromArgb(0x4B, 0x52, 0x5C),
        Well = Color.FromArgb(0x23, 0x27, 0x2D), Paper = Color.FromArgb(0xF2, 0xF1, 0xEC), Paper2 = Color.FromArgb(0xC3, 0xC7, 0xCC), Paper3 = Color.FromArgb(0x93, 0x9A, 0xA3), Rule = Color.FromArgb(0x5A, 0x61, 0x6B),
        Yellow = Color.FromArgb(0xFD, 0xD4, 0x00), OnYellow = Color.FromArgb(0x0C, 0x0C, 0x0C), Pass = Color.FromArgb(0x2E, 0xC0, 0x8E), Fail = Color.FromArgb(0xEE, 0x4B, 0x3D), Warn = Color.FromArgb(0xF2, 0xA1, 0x22),
        Cpu = Color.FromArgb(0x5B, 0xA8, 0xFF), Gpu = Color.FromArgb(0xB3, 0x8B, 0xFF), Ram = Color.FromArgb(0x35, 0xD0, 0xE0), Storage = Color.FromArgb(0xFF, 0x9A, 0x4D), Net = Color.FromArgb(0xFF, 0x78, 0xB9),
        Select = Color.FromArgb(0x57, 0x4F, 0x1C), Stripe = Color.FromArgb(0x29, 0x2D, 0x34);

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
        private static Bitmap? LoadLogo() { try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch (Exception ex) when (ex is ArgumentException or IOException) { return null; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 8 || Height < 8) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var back = new LinearGradientBrush(ClientRectangle, Yellow, Color.FromArgb(0xF0, 0xC4, 0x00), LinearGradientMode.Horizontal)) g.FillRectangle(back, ClientRectangle);
            using (var line = new SolidBrush(Color.FromArgb(0xB8, 0x92, 0x00))) g.FillRectangle(line, 0, Height - 3, Width, 3);
            // right: the Mazesta logo on a dark disc; beside it the tool's own symbol
            int d = Height - 24, x = Width - 16 - d, y = 10, d2 = d - 8, x2 = x - 10 - d2, y2 = y + 4;
            using (var disc = new SolidBrush(Ink)) { g.FillEllipse(disc, x, y, d, d); g.FillEllipse(disc, x2, y2, d2, d2); }
            if (Logo is not null) { int s = d * 7 / 10; g.DrawImage(Logo, x + (d - s) / 2, y + (d - s) / 2, s, s); }
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

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(4), Padding = new Padding(10, 0, 10, 0),
            BackColor = primary ? Yellow : Ink4, ForeColor = primary ? OnYellow : Paper, Font = Theme.Semi(10f) };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0xFF, 0xE2, 0x4A) : Color.FromArgb(0x3C, 0x42, 0x4B);
        b.EnabledChanged += (_, _) => { b.BackColor = b.Enabled ? (primary ? Yellow : Ink4) : Ink3; b.ForeColor = b.Enabled ? (primary ? OnYellow : Paper) : Paper3; };
        return b;
    }

    private static readonly Font ChipFont = Theme.Reg(10f);

    /// <summary>An option as a chip: dim when off, yellow when on (a plain check box is a white square on the dark window).</summary>
    public static CheckBox Chip(string text)
    {
        // sized by hand: the automatic size of a button-looking check box cut the end off a long right-to-left text
        var c = new CheckBox { Text = text, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, AutoSize = false, Font = ChipFont, Height = 30, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 6, 3), BackColor = Ink3, ForeColor = Paper2 };
        c.Width = TextRenderer.MeasureText(text, ChipFont, new Size(int.MaxValue, 30), TextFormatFlags.NoPrefix).Width + 30;
        c.FlatAppearance.BorderColor = Rule; c.FlatAppearance.CheckedBackColor = Yellow; c.FlatAppearance.MouseOverBackColor = Ink4;
        void Tint() => c.ForeColor = !c.Enabled ? Paper3 : c.Checked ? OnYellow : Paper2;
        c.CheckedChanged += (_, _) => Tint(); c.EnabledChanged += (_, _) => Tint();
        return c;
    }

    public static void Field(TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Ink3; t.ForeColor = Paper; }

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
    public static void List(ListView lv, Func<(int Column, bool Ascending)?>? sort = null, Action<int>? clicked = null)
    {
        lv.OwnerDraw = true; lv.BackColor = Well; lv.ForeColor = Paper; lv.BorderStyle = BorderStyle.None; lv.FullRowSelect = true; lv.HeaderStyle = clicked is null ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.Clickable;
        lv.SmallImageList ??= new ImageList { ImageSize = new Size(16, 26) };
        lv.ColumnClick += (_, e) => clicked?.Invoke(e.Column);
        // the last column takes what is left, so no bare (white) stretch of the system's header shows after it
        void Fill() { if (lv.Columns.Count == 0) return; int others = 0; for (int i = 0; i < lv.Columns.Count - 1; i++) others += lv.Columns[i].Width; lv.Columns[^1].Width = Math.Max(80, lv.ClientSize.Width - others); }
        lv.SizeChanged += (_, _) => Fill(); lv.HandleCreated += (_, _) => Fill(); lv.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex < lv.Columns.Count - 1) Fill(); };
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
                using (var fill = new SolidBrush(_c.Enabled ? Ink3 : Ink2)) g.FillRectangle(fill, all);
                var b = Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom);
                if (b.Width < 4) b = _c.RightToLeft == RightToLeft.Yes ? new Rectangle(0, 0, 24, _c.Height) : new Rectangle(_c.Width - 24, 0, 24, _c.Height);
                using (var fill = new SolidBrush(Ink4)) g.FillRectangle(fill, b);
                int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2;
                using (var arrow = new SolidBrush(_c.Enabled ? Paper : Paper3)) g.FillPolygon(arrow, [new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3)]);
                bool rtl = _c.RightToLeft == RightToLeft.Yes; var text = rtl ? Rectangle.FromLTRB(b.Right + 2, 0, _c.Width - 6, _c.Height) : Rectangle.FromLTRB(6, 0, b.Left - 2, _c.Height);
                TextRenderer.DrawText(g, _c.Text, _c.Font, text, _c.Enabled ? Paper : Paper3, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Side(_c, _c.Text));
                using var edge = new Pen(_c.Focused || _c.DroppedDown ? Yellow : Rule);
                g.DrawRectangle(edge, 0, 0, _c.Width - 1, _c.Height - 1);
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
