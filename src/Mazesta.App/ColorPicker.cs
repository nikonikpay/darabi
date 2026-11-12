using System.Drawing; using System.Windows.Forms; using System.Drawing.Drawing2D; using Mazesta.Core.Windows;
namespace Mazesta.App;

/// <summary>
/// The screen colour picker: the whole screen (every monitor) is captured once and shown still in a borderless window over it, with a
/// magnifier of the pixels under the cursor and the colour's code. A click picks, Esc or the right button cancels, the arrow keys move by
/// one pixel. Nothing runs before or after a pick: the window and its picture exist only while picking.
/// </summary>
public sealed class ColorPicker : Form
{
    private const int Cells = 15, Zoom = 10, Glass = Cells * Zoom, Label = 26, Gap = 18;
    private readonly Bitmap _shot; private Point _at; public Color? Picked { get; private set; }

    private ColorPicker(Bitmap shot, Rectangle screen)
    {
        _shot = shot;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = screen;
        TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; KeyPreview = true; Cursor = Cursors.Cross; BackColor = Color.Black;
        _at = new Point(Cursor.Position.X - screen.X, Cursor.Position.Y - screen.Y);
    }

    /// <summary>Shows the picker and returns the colour picked, or null when cancelled.</summary>
    public static Color? Pick()
    {
        var screen = SystemInformation.VirtualScreen;
        using var shot = new Bitmap(screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);
        using var form = new ColorPicker(shot, screen);
        form.ShowDialog();
        return form.Picked;
    }

    private Color At(Point p) => _shot.GetPixel(Math.Clamp(p.X, 0, _shot.Width - 1), Math.Clamp(p.Y, 0, _shot.Height - 1));

    // the magnifier and its label sit below-right of the cursor, or flip to the other side near an edge
    private Rectangle GlassRect(Point p)
    {
        int x = p.X + Gap, y = p.Y + Gap;
        if (x + Glass > ClientSize.Width) x = p.X - Gap - Glass;
        if (y + Glass + Label > ClientSize.Height) y = p.Y - Gap - Glass - Label;
        return new Rectangle(x, y, Glass, Glass + Label);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(_shot, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
        var r = GlassRect(_at); var lens = new Rectangle(r.X, r.Y, Glass, Glass);
        g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_shot, lens, new Rectangle(_at.X - Cells / 2, _at.Y - Cells / 2, Cells, Cells), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        var mid = new Rectangle(r.X + Cells / 2 * Zoom, r.Y + Cells / 2 * Zoom, Zoom, Zoom);
        g.DrawRectangle(Pens.Black, mid); mid.Inflate(1, 1); g.DrawRectangle(Pens.White, mid);
        g.DrawRectangle(Pens.White, lens);
        var c = At(_at); var band = new Rectangle(r.X, r.Y + Glass, Glass, Label);
        using (var b = new SolidBrush(Color.FromArgb(235, 20, 20, 24))) g.FillRectangle(b, band);
        using (var b = new SolidBrush(c)) g.FillRectangle(b, band.X + 6, band.Y + 5, Label - 10, Label - 10);
        TextRenderer.DrawText(g, ColorText.Hex(c.R, c.G, c.B), Font, new Rectangle(band.X + Label, band.Y, Glass - Label, Label), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void MoveTo(Point p)
    {
        if (p == _at) return;
        var old = GlassRect(_at); old.Inflate(2, 2); Invalidate(old);
        _at = p; var now = GlassRect(_at); now.Inflate(2, 2); Invalidate(now);
    }

    protected override void OnMouseMove(MouseEventArgs e) => MoveTo(e.Location);
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Picked = At(e.Location); Close(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var d = e.KeyCode switch { Keys.Left => new Size(-1, 0), Keys.Right => new Size(1, 0), Keys.Up => new Size(0, -1), Keys.Down => new Size(0, 1), _ => Size.Empty };
        if (d != Size.Empty) { Cursor.Position += d; MoveTo(PointToClient(Cursor.Position)); }
        else if (e.KeyCode is Keys.Enter or Keys.Space) { Picked = At(_at); Close(); }
        else if (e.KeyCode == Keys.Escape) Close();
    }

    // one window over monitors of different scales: Windows' "new DPI, new size" would shrink it off the picture, so its size stays as set
    protected override void WndProc(ref Message m) { if (m.Msg == 0x02E0) return; base.WndProc(ref m); }   // WM_DPICHANGED
}
