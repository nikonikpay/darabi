using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using Mazesta.Core.Windows;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Pictures made smaller with GDI+ (already in the app through Windows Forms, so nothing new is shipped). A new file is written beside each
/// original, which is never touched. The camera's turn (EXIF orientation) is applied, and the other metadata (place, camera) is not copied.
/// Formats GDI+ cannot read (HEIC, WebP) are reported as not supported, not skipped in silence.
/// </summary>
public static class ImageResizer
{
    public sealed record Result(string Source, string? Output, int Width, int Height, int NewWidth, int NewHeight, long OldBytes, long NewBytes, string? Error);

    public static Result Resize(string source, int longest, int quality)
    {
        try
        {
            Image img;
            try { img = Image.FromFile(source); }
            catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { return new(source, null, 0, 0, 0, 0, 0, 0, "format"); }   // GDI+'s answers to a file it cannot read
            using var _ = img;
            Turn(img);
            var (w, h) = ImageFit.Fit(img.Width, img.Height, longest);
            string ext = ImageFit.OutputExtension(source), output = ImageFit.OutputPath(source, longest, ext, File.Exists);
            using (var bmp = new Bitmap(w, h, ext == ".png" ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb))
            {
                bmp.SetResolution(img.HorizontalResolution, img.VerticalResolution);
                using (var g = Graphics.FromImage(bmp))
                {
                    if (ext != ".png") g.Clear(Color.White);   // a transparent GIF/PNG pixel has no colour in a JPEG
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality; g.SmoothingMode = SmoothingMode.HighQuality;
                    using var wrap = new ImageAttributes(); wrap.SetWrapMode(WrapMode.TileFlipXY);   // no grey seam at the edges
                    g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, wrap);
                }
                if (ext == ".png") bmp.Save(output, ImageFormat.Png);
                else
                {
                    using var p = new EncoderParameters(1); p.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 30, 100));
                    bmp.Save(output, ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid), p);
                }
            }
            return new(source, output, img.Width, img.Height, w, h, new FileInfo(source).Length, new FileInfo(output).Length, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException) { return new(source, null, 0, 0, 0, 0, 0, 0, ex.Message); }
    }

    // EXIF 0x0112: how the camera was held; the pixels are turned so that every viewer shows the picture upright without the tag.
    private static void Turn(Image img)
    {
        if (!img.PropertyIdList.Contains(0x0112)) return;
        var turn = img.GetPropertyItem(0x0112)?.Value is { Length: > 0 } v ? v[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX, 3 => RotateFlipType.Rotate180FlipNone, 4 => RotateFlipType.Rotate180FlipX, 5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone, 7 => RotateFlipType.Rotate270FlipX, 8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone,
        } : RotateFlipType.RotateNoneFlipNone;
        if (turn != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(turn);
    }
}
