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
    public static readonly Color Ink = Color.FromArgb(0x15, 0x17, 0x1A), Ink2 = Color.FromArgb(0x1C, 0x1F, 0x23), Ink3 = Color.FromArgb(0x25, 0x29, 0x30), Ink4 = Color.FromArgb(0x2F, 0x34, 0x3B),
        Well = Color.FromArgb(0x0E, 0x10, 0x12), Paper = Color.FromArgb(0xEC, 0xEB, 0xE6), Paper2 = Color.FromArgb(0xA9, 0xAC, 0xB0), Paper3 = Color.FromArgb(0x73, 0x7A, 0x82), Rule = Color.FromArgb(0x2A, 0x2E, 0x34),
        Yellow = Color.FromArgb(0xFD, 0xD4, 0x00), OnYellow = Color.FromArgb(0x0C, 0x0C, 0x0C), Pass = Color.FromArgb(0x2E, 0xC0, 0x8E), Fail = Color.FromArgb(0xEE, 0x4B, 0x3D), Warn = Color.FromArgb(0xF2, 0xA1, 0x22),
        Cpu = Color.FromArgb(0x5B, 0xA8, 0xFF), Gpu = Color.FromArgb(0xB3, 0x8B, 0xFF), Ram = Color.FromArgb(0x35, 0xD0, 0xE0), Storage = Color.FromArgb(0xFF, 0x9A, 0x4D), Net = Color.FromArgb(0xFF, 0x78, 0xB9),
        Select = Color.FromArgb(0x3A, 0x34, 0x10);

    // Segoe MDL2 Assets glyphs (on every Windows 10 and 11)
    public const string Lock = "", Edit = "", Picture = "", Drive = "", Search = "", Volume = "";

    /// <summary>The window's frame and colours: a dark title bar, the app's ink, the app's text size.</summary>
    public static void Window(Form f)
    {
        f.AutoScaleMode = AutoScaleMode.Dpi; f.Font = new Font("Segoe UI", 10f); f.BackColor = Ink; f.ForeColor = Paper;
        f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        f.HandleCreated += (_, _) => { int on = 1; _ = DwmSetWindowAttribute(f.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4); };
    }

    /// <summary>The top band of a window: a round icon in the tool's colour, the title, and one line saying what it is for.</summary>
    public static Control Banner(string glyph, Color accent, string title, string? subtitle) => new BannerPanel(glyph, accent, title, subtitle);

    private sealed class BannerPanel : Panel
    {
        private readonly string _glyph, _title, _sub; private readonly Color _accent;
        public BannerPanel(string glyph, Color accent, string title, string? sub)
        {
            _glyph = glyph; _accent = accent; _title = title; _sub = sub ?? ""; Dock = DockStyle.Top; Height = 70; DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var back = new LinearGradientBrush(ClientRectangle, Ink2, Color.FromArgb(0x1C + _accent.R / 8, 0x1F + _accent.G / 8, 0x23 + _accent.B / 8), LinearGradientMode.Horizontal)) g.FillRectangle(back, ClientRectangle);
            using (var line = new SolidBrush(_accent)) g.FillRectangle(line, 0, Height - 3, Width, 3);
            int d = Height - 26, x = Width - 18 - d, y = 11;
            using (var disc = new SolidBrush(_accent)) g.FillEllipse(disc, x, y, d, d);
            using (var gf = new Font("Segoe MDL2 Assets", d * 0.42f, FontStyle.Regular, GraphicsUnit.Pixel)) using (var dark = new SolidBrush(OnYellow))
            {
                var sz = g.MeasureString(_glyph, gf); g.DrawString(_glyph, gf, dark, x + (d - sz.Width) / 2 + 1, y + (d - sz.Height) / 2 + 1);
            }
            using var rtl = new StringFormat(StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap) { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
            using var tf = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Point); using var sf = new Font("Segoe UI", 9.5f, GraphicsUnit.Point);
            using (var p = new SolidBrush(Paper)) g.DrawString(_title, tf, p, new RectangleF(16, 10, x - 28, 30), rtl);
            using (var p2 = new SolidBrush(Paper2)) g.DrawString(_sub, sf, p2, new RectangleF(16, 40, x - 28, 24), rtl);
        }
    }

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(4), Padding = new Padding(10, 0, 10, 0),
            BackColor = primary ? Yellow : Ink4, ForeColor = primary ? OnYellow : Paper, Font = new Font("Segoe UI Semibold", 10f) };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0xFF, 0xE2, 0x4A) : Color.FromArgb(0x3C, 0x42, 0x4B);
        b.EnabledChanged += (_, _) => { b.BackColor = b.Enabled ? (primary ? Yellow : Ink4) : Ink3; b.ForeColor = b.Enabled ? (primary ? OnYellow : Paper) : Paper3; };
        return b;
    }

    /// <summary>An option as a chip: dim when off, yellow when on (a plain check box is a white square on the dark window).</summary>
    public static CheckBox Chip(string text)
    {
        var c = new CheckBox { Text = text, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, AutoSize = true, MinimumSize = new Size(0, 30), Cursor = Cursors.Hand, Margin = new Padding(0, 3, 0, 3), Padding = new Padding(8, 0, 8, 0), BackColor = Ink3, ForeColor = Paper2 };
        c.FlatAppearance.BorderColor = Rule; c.FlatAppearance.CheckedBackColor = Yellow; c.FlatAppearance.MouseOverBackColor = Ink4;
        void Tint() => c.ForeColor = !c.Enabled ? Paper3 : c.Checked ? OnYellow : Paper2;
        c.CheckedChanged += (_, _) => Tint(); c.EnabledChanged += (_, _) => Tint();
        return c;
    }

    public static void Field(TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.BackColor = Ink3; t.ForeColor = Paper; }

    /// <summary>A drop-down drawn by hand: the system's draws the chosen row in blue with dark text.</summary>
    public static void Combo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList; c.FlatStyle = FlatStyle.Flat; c.BackColor = Ink3; c.ForeColor = Paper; c.DrawMode = DrawMode.OwnerDrawFixed; c.ItemHeight = 26;
        c.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(hot ? Select : Ink3)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, c.GetItemText(c.Items[e.Index]), c.Font, Rectangle.Inflate(e.Bounds, -6, 0), hot ? Yellow : Paper, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
        var head = new Font("Segoe UI Semibold", 9.5f);
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
            Color back = chosen ? Select : e.ItemIndex % 2 == 0 ? Well : Color.FromArgb(0x13, 0x15, 0x18);
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

    /// <summary>The scroll bars dark too (Windows' own dark theme for them).</summary>
    public static void DarkScroll(Control c) => c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string app, string? id);
}
