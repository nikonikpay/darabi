using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.Drawing.Text; using System.Globalization;
using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels;
namespace Mazesta.Desktop.Views;

/// <summary>
/// Draws the on-screen overlay into a picture with an alpha channel (the window shows it as is; see <see cref="OverlayWindow"/>): a small
/// translucent plate. Stacked (one column or two): on top the frame-rate box - the rate big, 1 % low and frame time beside it, the session's
/// average, lowest and highest under it, and the last minute as a trace on a scope screen with the 1 % low as a dashed level - then a box per part
/// in the part's colour (edge and a faint wash), its title bold on a solid tag: each reading's label, its number white with the unit small in the
/// hue, and a thin scale for a share of a fixed top; a charted item draws its last minute instead. As a line: the same boxes side by side in one
/// strip, each reading a small label over its number, no charts. Numbers are set in Bahnschrift, the DIN face Windows ships. In a right-to-left
/// language the boxes, labels and titles mirror; numbers and the frame-rate box read left to right. Sizes are in device-independent pixels.
/// </summary>
public static class OverlayRenderer
{
    private static readonly Color Ink = Color.FromArgb(0x0E, 0x10, 0x12), Label = Hex("#FFC3C7CC"), Faint = Hex("#FF8A9097"), Game = Hex("#FFFDD400"),
        GameEdge = Hex("#59FDD400"), GameTint = Hex("#14FDD400"), Mark = Hex("#FFA4A8AD");
    private static readonly FontFamily NumFace = Family("Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI"), TagFace = Family("Bahnschrift", "Segoe UI");
    /// <summary>Labels and captions in the app's own face (IRANSansX, embedded for the reports too), else Segoe UI.</summary>
    private static readonly FontFamily TextFace = AppFace() ?? Family("Segoe UI Semibold", "Segoe UI"), PlainFace = AppFace() ?? Family("Segoe UI");
    private static System.Drawing.Text.PrivateFontCollection? s_fonts;

    private static FontFamily? AppFace()
    {
        if (s_fonts is not null) return s_fonts.Families.FirstOrDefault();
        try
        {
            var fonts = new System.Drawing.Text.PrivateFontCollection();
            foreach (var file in new[] { "IRANSansXFaNum-Regular.ttf", "IRANSansXFaNum-Bold.ttf" })
            {
                using var stream = typeof(OverlayRenderer).Assembly.GetManifestResourceStream("Fonts." + file);
                if (stream is null) return null;
                byte[] data = new byte[stream.Length]; stream.ReadExactly(data);
                // GDI+ reads the font from this memory for as long as the collection lives: it is never freed (one copy for the process).
                nint at = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
                System.Runtime.InteropServices.Marshal.Copy(data, 0, at, data.Length);
                fonts.AddMemoryFont(at, data.Length);
            }
            s_fonts = fonts;
            return fonts.Families.FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
    }
    private static readonly StringFormat Tight = MakeFormat(false), TightRtl = MakeFormat(true);

    /// <summary>The overlay at <paramref name="pixelsPerDip"/> (the screen's scale times the user's size), drawn premultiplied for a layered window.</summary>
    public static Bitmap Render(OverlayViewModel vm, bool rtl, float pixelsPerDip)
    {
        using var probe = new Bitmap(1, 1); using var pg = Graphics.FromImage(probe);
        Prepare(pg);
        var size = vm.IsLine ? Line(pg, vm, rtl, 0, 0, null) : Stack(pg, vm, rtl, 0, 0, null);
        float pad = 9;   // the plate's 1 px edge and 8 px padding
        float w = size.Width + 2 * pad, h = size.Height + 2 * pad;
        var bmp = new Bitmap(Math.Max(1, (int)Math.Ceiling(w * pixelsPerDip)), Math.Max(1, (int)Math.Ceiling(h * pixelsPerDip)), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        Prepare(g); g.Clear(Color.Transparent); g.ScaleTransform(pixelsPerDip, pixelsPerDip);
        using (var plate = Rounded(new RectangleF(0.5f, 0.5f, w - 1, h - 1), 9))
        {
            using var fill = new SolidBrush(Color.FromArgb((int)Math.Round(vm.Opacity * 255), Ink)); g.FillPath(fill, plate);
            using var edge = new Pen(Hex("#1FFFFFFF"), 1); g.DrawPath(edge, plate);
        }
        if (vm.IsLine) Line(g, vm, rtl, pad, pad, g); else Stack(g, vm, rtl, pad, pad, g);
        return bmp;
    }

    // ——— Stacked: the header, the frame-rate box, then the part boxes wrapping at the panel's width ———
    private static SizeF Stack(Graphics m, OverlayViewModel vm, bool rtl, float x0, float y0, Graphics? g)
    {
        float W = (float)vm.PanelWidth, y = y0;
        // The mark, and the program the frame rate is measured in.
        float head = 13, hy = y + 1;
        if (g is not null)
        {
            float barX = rtl ? x0 + W - 4 - 3 : x0 + 4;
            using (var b = new SolidBrush(Game)) g.FillRectangle(b, barX, hy + (head - 10) / 2, 3, 10);
            var tag = Text(m, "MAZESTA", TagFace, 9, FontStyle.Bold);
            float tagX = rtl ? barX - 7 - tag.Width : barX + 3 + 7;
            Draw(g, "MAZESTA", TagFace, 9, FontStyle.Bold, Mark, tagX, hy + (head - tag.Height) / 2);
            if (vm.HeroApp.Length > 0)
            {
                float room = W - 8 - tag.Width - 3 - 7 - 10;
                var app = Text(m, vm.HeroApp, NumFace, 10, FontStyle.Regular);
                float aw = Math.Min(app.Width, room), ax = rtl ? x0 + 4 : x0 + W - 4 - aw;
                DrawClipped(g, vm.HeroApp, NumFace, 10, FontStyle.Regular, Faint, new RectangleF(ax, hy + (head - app.Height) / 2, aw, app.Height));
            }
        }
        y += 1 + head + 7;
        if (vm.HasHero) y += Hero(m, vm, rtl, x0, y, W, g) + 8;
        // The part boxes: one per line, or two side by side; they wrap from the start edge.
        float sw = (float)vm.SectionWidth; int perRow = vm.TwoColumns ? 2 : 1;
        var blocks = vm.Blocks;
        for (int i = 0; i < blocks.Count; i += perRow)
        {
            float rowH = 0;
            for (int k = 0; k < perRow && i + k < blocks.Count; k++)
            {
                float bx = x0 + k * (sw + 8); if (rtl) bx = x0 + W - (bx - x0) - sw;
                rowH = Math.Max(rowH, Block(m, blocks[i + k], rtl, bx, y, sw, g));
            }
            y += rowH + (i + perRow < blocks.Count ? 8 : 0);
        }
        if (blocks.Count == 0 && vm.HasHero) y -= 8;
        return new SizeF(W, y - y0);
    }

    /// <summary>A box measured first, then its wash and edge painted, then its content over them.</summary>
    private static float Boxed(Func<Graphics?, float> body, Graphics? g, RectangleF at, Color tint, Color edge)
    {
        float h = body(null);
        if (g is null) return h;
        using (var box = Rounded(new RectangleF(at.X + 0.5f, at.Y + 0.5f, at.Width - 1, h - 1), 6))
        using (var b = new SolidBrush(tint)) using (var e = new Pen(edge, 1)) { g.FillPath(b, box); g.DrawPath(e, box); }
        body(g);
        return h;
    }

    private static float Hero(Graphics m, OverlayViewModel vm, bool rtl, float x0, float y0, float W, Graphics? g)
        => Boxed(gg => HeroBody(m, vm, rtl, x0, y0, W, gg), g, new RectangleF(x0, y0, W, 0), GameTint, GameEdge);

    private static float HeroBody(Graphics m, OverlayViewModel vm, bool rtl, float x0, float y0, float W, Graphics? g)
    {
        float ix = x0 + 10, iw = W - 20, y = y0 + 7;
        var chip = Chip(m, "GAME");
        if (g is not null) DrawChip(g, "GAME", Game, rtl ? ix + iw - chip.Width : ix, y);
        y += chip.Height + 2;
        // The rate big at the left, the 1 % low and frame time stacked at the right: numbers read left to right in either language.
        float bigH = 40, rightH = 0;
        var right = new List<(OverlayRow Row, string Tag)>();
        if (vm.HeroLow is { } low) right.Add((low, "1% LOW"));
        if (vm.HeroFrameTime is { } ft) right.Add((ft, "MS"));
        foreach (var _ in right) rightH += 20;
        float gridH = Math.Max(vm.HeroFps is null ? 0 : bigH, rightH);
        if (g is not null)
        {
            if (vm.HeroFps is { } fps)
            {
                var n = Text(m, fps.Number, NumFace, 44, FontStyle.Bold);
                Draw(g, fps.Number, NumFace, 44, FontStyle.Bold, Color.White, ix, y + gridH - n.Height + 4);
                var t = Text(m, "FPS", TagFace, 10.5f, FontStyle.Bold);
                Draw(g, "FPS", TagFace, 10.5f, FontStyle.Bold, Game, ix + n.Width + 6, y + gridH - t.Height - 2);
            }
            float ry = y + gridH - rightH;
            foreach (var (row, tag) in right)
            {
                var n = Text(m, row.Number, NumFace, 16, FontStyle.Bold);
                float tx = ix + iw - 40;
                Draw(g, tag, TagFace, 9, FontStyle.Bold, Faint, tx + 5, ry + 20 - Text(m, tag, TagFace, 9, FontStyle.Bold).Height - 3);
                Draw(g, row.Number, NumFace, 16, FontStyle.Bold, Color.White, tx - n.Width, ry + 20 - n.Height);
                ry += 20;
            }
        }
        y += gridH;
        if (vm.HasSessionStats)
        {
            y += 7;
            float sx = ix;
            foreach (var (row, tag) in new[] { (vm.HeroAvg, "AVG"), (vm.HeroMin, "MIN"), (vm.HeroMax, "MAX") })
            {
                if (row is null) continue;
                var t = Text(m, tag, TagFace, 9, FontStyle.Bold); var n = Text(m, row.Number, NumFace, 17, FontStyle.Bold);
                if (g is not null) { Draw(g, tag, TagFace, 9, FontStyle.Bold, Game, sx, y); Draw(g, row.Number, NumFace, 17, FontStyle.Bold, Color.White, sx, y + t.Height); }
                sx += Math.Max(t.Width, n.Width) + 14;
            }
            y += Text(m, "AVG", TagFace, 9, FontStyle.Bold).Height + Text(m, "0", NumFace, 17, FontStyle.Bold).Height;
        }
        if (vm.HeroFps is not null)
        {
            y += 8;
            if (g is not null) FrameChart(g, new RectangleF(ix, y, iw, 72), vm.HeroTrend, vm.HeroLowValue, Game, OverlayViewModel.TrendLength);
            y += 72;
        }
        y += 9;
        return y - y0;
    }

    private static float Block(Graphics m, OverlaySection s, bool rtl, float x0, float y0, float w, Graphics? g)
        => Boxed(gg => BlockBody(m, s, rtl, x0, y0, w, gg), g, new RectangleF(x0, y0, w, 0), Hex(s.HueTint), Hex(s.HueEdge));

    private static float BlockBody(Graphics m, OverlaySection s, bool rtl, float x0, float y0, float w, Graphics? g)
    {
        float ix = x0 + 9, iw = w - 18, y = y0 + 7;
        var hue = Hex(s.Hue);
        var chip = Chip(m, s.Title);
        if (g is not null)
        {
            DrawChip(g, s.Title, hue, rtl ? ix + iw - chip.Width : ix, y);
            if (s.Subtitle.Length > 0)
            {
                var sub = Text(m, s.Subtitle, NumFace, 9.5f, FontStyle.Regular);
                float room = iw - chip.Width - 8, sw = Math.Min(sub.Width, room);
                DrawClipped(g, s.Subtitle, NumFace, 9.5f, FontStyle.Regular, Faint, new RectangleF(rtl ? ix : ix + iw - sw, y + (chip.Height - sub.Height) / 2, sw, sub.Height));
            }
        }
        y += chip.Height + 4;
        foreach (var row in s.Rows)
        {
            y += 3;
            var label = Text(m, row.Label, TextFace, 11, FontStyle.Regular, rtl);
            var n = Text(m, row.Number, NumFace, 15.5f, FontStyle.Bold); var u = Text(m, row.UnitText, NumFace, 9, FontStyle.Bold);
            float lineH = Math.Max(label.Height, n.Height);
            if (g is not null)
            {
                Draw(g, row.Label, TextFace, 11, FontStyle.Regular, Label, rtl ? ix + iw - label.Width : ix, y + (lineH - label.Height) / 2, rtl);
                float numW = n.Width + (row.UnitText.Length > 0 ? 3 + u.Width : 0), nx = rtl ? ix : ix + iw - numW;
                Draw(g, row.Number, NumFace, 15.5f, FontStyle.Bold, Color.White, nx, y + (lineH - n.Height) / 2);
                if (row.UnitText.Length > 0) Draw(g, row.UnitText, NumFace, 9, FontStyle.Bold, hue, nx + n.Width + 3, y + (lineH + n.Height) / 2 - u.Height - 2);
            }
            y += lineH;
            if (row.HasBar)
            {
                y += 4;
                if (g is not null)
                {
                    using (var back = new SolidBrush(Hex("#1AFFFFFF"))) g.FillRectangle(back, ix, y, iw, 2);
                    float fw = (float)(iw * Math.Clamp(row.Fraction, 0, 1));
                    using var fill = new SolidBrush(hue); g.FillRectangle(fill, ix, y, fw, 2);   // fills from the left in either language, as before
                }
                y += 2;
            }
            if (row.HasChart)
            {
                y += 4;
                if (g is not null)
                {
                    var r = new RectangleF(ix, y, iw, 26);
                    using (var screen = Rounded(r, 2)) using (var b = new SolidBrush(Hex("#4D000000"))) g.FillPath(b, screen);
                    Sparkline(g, r, row.Trend, row.TrendMax, hue, OverlayViewModel.TrendLength);
                    if (row.TrendMaxValue.Length > 0)
                    {
                        // The word, then the value in its own left-to-right run ("71 °C" would be reordered inside right-to-left text).
                        string word = Loc.Get("Overlay_ChartMaxWord"); var wd = Text(m, word, PlainFace, 8, FontStyle.Regular, rtl); var vl = Text(m, row.TrendMaxValue, NumFace, 8, FontStyle.Regular);
                        var dim = Hex("#AAFFFFFF");
                        if (rtl) { Draw(g, word, PlainFace, 8, FontStyle.Regular, dim, r.Right - 4 - wd.Width, r.Y + 1, true); Draw(g, row.TrendMaxValue, NumFace, 8, FontStyle.Regular, dim, r.Right - 4 - wd.Width - 3 - vl.Width, r.Y + 1); }
                        else { Draw(g, word, PlainFace, 8, FontStyle.Regular, dim, r.X + 4, r.Y + 1); Draw(g, row.TrendMaxValue, NumFace, 8, FontStyle.Regular, dim, r.X + 4 + wd.Width + 3, r.Y + 1); }
                    }
                }
                y += 26 + 1;
            }
            y += 2;
        }
        y += 7;
        return y - y0;
    }

    // ——— Line: one strip of boxes side by side, each reading a label over its number ———
    private static SizeF Line(Graphics m, OverlayViewModel vm, bool rtl, float x0, float y0, Graphics? g)
    {
        // Measured first in reading order, then placed: from the left, or from the right in a right-to-left language.
        var items = new List<(float W, Func<float, float, Graphics, float> Paint)>();
        float tagH = Text(m, "AVG", TagFace, 9, FontStyle.Bold).Height, numH = Text(m, "0", NumFace, 15, FontStyle.Bold).Height;
        float inner = Math.Max(tagH + numH, Text(m, "0", NumFace, 26, FontStyle.Bold).Height);
        float H = inner + 10;
        items.Add((3 + 2 + 6, (x, y, gg) => { using var b = new SolidBrush(Game); gg.FillRectangle(b, x + (rtl ? 6 : 2), y + 4, 3, H - 8); return 0; }));
        if (vm.HasHero)
        {
            var parts = new List<(float W, Action<float, float, Graphics> Paint)>();
            if (vm.HeroFps is { } fps)
            {
                var n = Text(m, fps.Number, NumFace, 26, FontStyle.Bold); var t = Text(m, "FPS", TagFace, 10, FontStyle.Bold);
                parts.Add((n.Width + 4 + t.Width + 12, (x, y, gg) =>
                {
                    Draw(gg, fps.Number, NumFace, 26, FontStyle.Bold, Color.White, x, y + (inner - n.Height) / 2);
                    Draw(gg, "FPS", TagFace, 10, FontStyle.Bold, Game, x + n.Width + 4, y + (inner + n.Height) / 2 - t.Height - 4);
                }));
            }
            var stats = new[] { (vm.HeroLow, "1% LOW"), (vm.HeroAvg, "AVG"), (vm.HeroMin, "MIN"), (vm.HeroMax, "MAX"), (vm.HeroFrameTime, "MS") }.Where(p => p.Item1 is not null).ToList();
            for (int i = 0; i < stats.Count; i++)
            {
                var (row, tag) = stats[i]; var t = Text(m, tag, TagFace, 9, FontStyle.Bold); var n = Text(m, row!.Number, NumFace, 15, FontStyle.Bold);
                parts.Add((Math.Max(t.Width, n.Width) + (i < stats.Count - 1 ? 14 : 0), (x, y, gg) =>
                {
                    float top = y + (inner - tagH - numH) / 2;
                    Draw(gg, tag, TagFace, 9, FontStyle.Bold, Game, x, top); Draw(gg, row.Number, NumFace, 15, FontStyle.Bold, Color.White, x, top + tagH);
                }));
            }
            float hw = 9 + parts.Sum(p => p.W) + 9;
            items.Add((hw + 6, (x, y, gg) =>
            {
                float bx = rtl ? x + 6 : x;
                using var box = Rounded(new RectangleF(bx + 0.5f, y + 0.5f, hw - 1, H - 1), 6);
                using (var tint = new SolidBrush(GameTint)) gg.FillPath(tint, box);
                using (var edge = new Pen(GameEdge, 1)) gg.DrawPath(edge, box);
                float px = bx + 9; foreach (var p in parts) { p.Paint(px, y + 5, gg); px += p.W; }   // the frame-rate box reads left to right
                return 0;
            }));
        }
        foreach (var s in vm.Blocks)
        {
            var hue = Hex(s.Hue); var chip = Chip(m, s.Title);
            var rows = s.Rows.Select(r =>
            {
                var l = Text(m, r.Label, TextFace, 9.5f, FontStyle.Regular, rtl); var n = Text(m, r.Number, NumFace, 15, FontStyle.Bold); var u = Text(m, r.UnitText, NumFace, 8.5f, FontStyle.Bold);
                return (Row: r, L: l, N: n, U: u, W: Math.Max(l.Width, n.Width + (r.UnitText.Length > 0 ? 2 + u.Width : 0)));
            }).ToList();
            float bw = 8 + chip.Width + 9 + rows.Sum(r => r.W) + 11 * Math.Max(0, rows.Count - 1) + 9;
            items.Add((bw + 6, (x, y, gg) =>
            {
                float bx = rtl ? x + 6 : x;
                using (var box = Rounded(new RectangleF(bx + 0.5f, y + 0.5f, bw - 1, H - 1), 6))
                using (var tint = new SolidBrush(Hex(s.HueTint))) using (var edge = new Pen(Hex(s.HueEdge), 1)) { gg.FillPath(tint, box); gg.DrawPath(edge, box); }
                float cx = rtl ? bx + bw - 9 - chip.Width : bx + 8;
                DrawChip(gg, s.Title, hue, cx, y + (H - chip.Height) / 2);
                float px = rtl ? cx - 9 : cx + chip.Width + 9;
                foreach (var r in rows)
                {
                    float rx = rtl ? px - r.W : px, top = y + 5 + (inner - r.L.Height - r.N.Height) / 2;
                    Draw(gg, r.Row.Label, TextFace, 9.5f, FontStyle.Regular, Faint, rtl ? rx + r.W - r.L.Width : rx, top, rtl);
                    float nx = rtl ? rx + r.W - (r.N.Width + (r.Row.UnitText.Length > 0 ? 2 + r.U.Width : 0)) : rx;
                    Draw(gg, r.Row.Number, NumFace, 15, FontStyle.Bold, Color.White, nx, top + r.L.Height);
                    if (r.Row.UnitText.Length > 0) Draw(gg, r.Row.UnitText, NumFace, 8.5f, FontStyle.Bold, hue, nx + r.N.Width + 2, top + r.L.Height + r.N.Height - r.U.Height - 2);
                    px = rtl ? rx - 11 : rx + r.W + 11;
                }
                return 0;
            }));
        }
        float total = items.Sum(i => i.W) - (items.Count > 1 ? 6 : 0);
        if (g is not null)
        {
            float x = rtl ? x0 + total : x0;
            foreach (var (w, paint) in items) { if (rtl) { x -= w; paint(x, y0, g); } else { paint(x, y0, g); x += w; } }
        }
        return new SizeF(total, H);
    }

    // ——— Charts ———
    /// <summary>The frame-rate trace on a scope screen: dotted divisions, the area under the line faintly filled, a dashed level (the 1 % low), the
    /// newest point marked; scaled from zero to a little above the highest shown. A missing reading (NaN) breaks the line.</summary>
    private static void FrameChart(Graphics g, RectangleF r, double[] values, double level, Color stroke, int capacity)
    {
        using (var screen = Rounded(r, 3)) using (var b = new SolidBrush(Hex("#59000000"))) g.FillPath(b, screen);
        using (var div = new Pen(Hex("#21FFFFFF"), 1) { DashPattern = [1, 3] })
        {
            for (int i = 1; i < 6; i++) { float x = r.X + (float)Math.Round(r.Width * i / 6) + 0.5f; g.DrawLine(div, x, r.Y, x, r.Bottom); }
            for (int i = 1; i < 3; i++) { float y = r.Y + (float)Math.Round(r.Height * i / 3) + 0.5f; g.DrawLine(div, r.X, y, r.Right, y); }
        }
        if (values.Length < 2) return;
        var finite = values.Where(double.IsFinite).ToList();
        if (finite.Count < 2) return;
        double top = Math.Max(finite.Max(), double.IsFinite(level) ? level : 0) * 1.15;
        if (top <= 0) return;
        float step = r.Width / Math.Max(1, capacity - 1), x0 = r.Right - (values.Length - 1) * step;
        float Y(double v) => (float)(r.Bottom - 2 - v / top * (r.Height - 4));
        using (var fill = new LinearGradientBrush(new PointF(0, r.Y), new PointF(0, r.Bottom), Color.FromArgb(0x48, stroke), Color.FromArgb(0, stroke)))
            foreach (var run in Runs(values, i => new PointF(x0 + i * step, Y(values[i]))))
            {
                using var area = new GraphicsPath(); area.AddLines([.. run, new PointF(run[^1].X, r.Bottom), new PointF(run[0].X, r.Bottom)]); g.FillPath(fill, area);
            }
        if (double.IsFinite(level)) { float y = (float)Math.Round(Y(level)) + 0.5f; using var p = new Pen(Hex("#8CFFFFFF"), 1) { DashPattern = [4, 3] }; g.DrawLine(p, r.X, y, r.Right, y); }
        using (var pen = new Pen(stroke, 1.75f) { LineJoin = LineJoin.Round })
            foreach (var run in Runs(values, i => new PointF(x0 + i * step, Y(values[i])))) if (run.Count > 1) g.DrawLines(pen, run.ToArray());
        if (double.IsFinite(values[^1])) using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, x0 + (values.Length - 1) * step - 5, Y(values[^1]) - 2.5f, 5, 5);
    }

    /// <summary>A row's last minute on its fixed 0..max scale, filled faintly below the line; NaN is a gap.</summary>
    private static void Sparkline(Graphics g, RectangleF r, double[] values, double max, Color stroke, int capacity)
    {
        using (var grid = new Pen(Hex("#22FFFFFF"), 1) { DashStyle = DashStyle.Dot }) g.DrawLine(grid, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
        if (values.Length < 2 || max <= 0) return;
        float step = r.Width / Math.Max(1, capacity - 1), x0 = r.Right - (values.Length - 1) * step;
        PointF P(int i) => new(x0 + i * step, r.Bottom - (float)Math.Clamp(values[i] / max, 0, 1) * r.Height);
        using var tint = new SolidBrush(Color.FromArgb(46, stroke)); using var pen = new Pen(stroke, 1.5f) { LineJoin = LineJoin.Round };
        foreach (var run in Runs(values, P))
        {
            using var area = new GraphicsPath(); area.AddLines([.. run, new PointF(run[^1].X, r.Bottom), new PointF(run[0].X, r.Bottom)]); g.FillPath(tint, area);
            if (run.Count > 1) g.DrawLines(pen, run.ToArray());
        }
    }

    private static IEnumerable<List<PointF>> Runs(double[] values, Func<int, PointF> at)
    {
        var run = new List<PointF>();
        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i])) { if (run.Count > 0) yield return run; run = []; continue; }
            run.Add(at(i));
        }
        if (run.Count > 0) yield return run;
    }

    // ——— Pieces ———
    /// <summary>A title on a solid tag of its colour, the text in the plate's ink.</summary>
    private static SizeF Chip(Graphics m, string text) { var t = Text(m, text, TagFace, 11, FontStyle.Bold); return new(t.Width + 12, t.Height + 3); }
    private static void DrawChip(Graphics g, string text, Color hue, float x, float y)
    {
        var t = Text(g, text, TagFace, 11, FontStyle.Bold);
        using (var tag = Rounded(new RectangleF(x, y, t.Width + 12, t.Height + 3), 3)) using (var b = new SolidBrush(hue)) g.FillPath(b, tag);
        Draw(g, text, TagFace, 11, FontStyle.Bold, Ink, x + 6, y + 2);
    }

    private static SizeF Text(Graphics m, string text, FontFamily face, float size, FontStyle style, bool rtl = false)
    {
        if (text.Length == 0) return new SizeF(0, size * 1.2f);
        using var font = new Font(face, size, Usable(face, style), GraphicsUnit.Pixel);
        var s = m.MeasureString(text, font, PointF.Empty, rtl ? TightRtl : Tight);
        return new SizeF(s.Width, font.GetHeight(m));
    }
    private static void Draw(Graphics g, string text, FontFamily face, float size, FontStyle style, Color color, float x, float y, bool rtl = false)
    {
        if (text.Length == 0) return;
        using var font = new Font(face, size, Usable(face, style), GraphicsUnit.Pixel); using var b = new SolidBrush(color);
        var w = g.MeasureString(text, font, PointF.Empty, rtl ? TightRtl : Tight).Width;
        g.DrawString(text, font, b, new RectangleF(x, y, w + 1, font.GetHeight(g) + 1), rtl ? TightRtl : Tight);
    }
    private static void DrawClipped(Graphics g, string text, FontFamily face, float size, FontStyle style, Color color, RectangleF r)
    {
        using var font = new Font(face, size, Usable(face, style), GraphicsUnit.Pixel); using var b = new SolidBrush(color);
        using var f = (StringFormat)Tight.Clone(); f.Trimming = StringTrimming.EllipsisCharacter; f.FormatFlags |= StringFormatFlags.NoWrap;
        g.DrawString(text, font, b, new RectangleF(r.X, r.Y, r.Width + 1, r.Height + 1), f);
    }
    private static FontStyle Usable(FontFamily face, FontStyle style) => face.IsStyleAvailable(style) ? style : FontStyle.Regular;

    private static StringFormat MakeFormat(bool rtl)
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        if (rtl) f.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        return f;
    }

    private static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias;   // not grid-fitted: snapping each glyph to whole pixels pulls small text apart ("Tem p") and coarsens Persian joins
        g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighSpeed;   // plain sRGB blending, as WPF drew it: gamma-corrected blending makes a faint wash read three times as strong
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    private static FontFamily Family(params string[] names)
    {
        foreach (var n in names) { try { return new FontFamily(n); } catch (ArgumentException) { } }
        return FontFamily.GenericSansSerif;
    }

    /// <summary>"#RRGGBB" or "#AARRGGBB".</summary>
    public static Color Hex(string hex)
    {
        string h = hex.TrimStart('#');
        uint v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return h.Length == 6 ? Color.FromArgb(255, (int)(v >> 16 & 0xFF), (int)(v >> 8 & 0xFF), (int)(v & 0xFF)) : Color.FromArgb((int)(v >> 24), (int)(v >> 16 & 0xFF), (int)(v >> 8 & 0xFF), (int)(v & 0xFF));
    }
}
