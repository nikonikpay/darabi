using System.Drawing; using System.Drawing.Imaging; using Mazesta.Desktop.Services; using Xunit;
namespace Mazesta.Desktop.Tests;

public class ImageResizerTests
{
    [Fact] public void A_picture_gets_a_smaller_copy_beside_it_and_the_original_is_untouched()
    {
        string dir = Directory.CreateTempSubdirectory("mz-img").FullName, src = Path.Combine(dir, "photo.png");
        try
        {
            using (var b = new Bitmap(400, 200)) { using (var g = Graphics.FromImage(b)) g.Clear(Color.Red); b.Save(src, ImageFormat.Png); }
            byte[] before = File.ReadAllBytes(src);
            var r = ImageResizer.Resize(src, 100, 85);
            Assert.Null(r.Error); Assert.Equal(Path.Combine(dir, "photo (100).png"), r.Output);
            using (var o = Image.FromFile(r.Output!)) Assert.Equal((100, 50), (o.Width, o.Height));
            Assert.Equal(before, File.ReadAllBytes(src));
            Assert.Equal("format", ImageResizer.Resize(WriteText(dir), 100, 85).Error);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string WriteText(string dir) { string p = Path.Combine(dir, "not-a-picture.jpg"); File.WriteAllText(p, "hello"); return p; }
}
