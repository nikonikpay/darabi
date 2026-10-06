using Xunit;
using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class TextureCodecTests
{
    // 24 x 16, colour planes at half size (4:2:0), quality 92: red rises with x, green with y, blue falls with both
    private const string Gradient = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAAQABgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD5a0r4adP9H/Sux0r4afd/0f8ASvftK+Gn3f8AR/0rsdK+GnT/AEf9K/YeOfFv4v3n4nwHh54j/B754DpXw0+7/o/6UV9YaV8NOn+j/pRX8p514t/7U/3n4n9k5D4j/wCxx98//9k=";

    [Fact] public void A_baseline_jpeg_decodes_to_the_picture_it_was_made_from()
    {
        byte[] rgb = Jpeg.Decode(Convert.FromBase64String(Gradient), out int w, out int h);
        Assert.Equal((24, 16), (w, h)); Assert.Equal(24 * 16 * 3, rgb.Length);
        // what libjpeg makes of the same file, within the rounding of two decoders
        foreach (var (x, y, r, g, b) in new[] { (0, 0, 0, 4, 246), (23, 0, 221, 4, 119), (0, 15, 9, 220, 179), (23, 15, 230, 222, 53), (12, 8, 122, 120, 141), (5, 11, 51, 164, 172) })
        {
            int o = (y * w + x) * 3;
            Assert.InRange(rgb[o], r - 4, r + 4); Assert.InRange(rgb[o + 1], g - 4, g + 4); Assert.InRange(rgb[o + 2], b - 4, b + 4);
        }
    }

    [Fact] public void A_picture_that_is_not_a_baseline_jpeg_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => Jpeg.Decode([1, 2, 3, 4], out _, out _));
        byte[] cut = Convert.FromBase64String(Gradient)[..300];
        Assert.Throws<InvalidDataException>(() => Jpeg.Decode(cut, out _, out _));
    }

    /// <summary>What the GPU makes of a BC3 block's texel: its colour (5:6:5 ends, two between) and its alpha (eight steps).</summary>
    private static (int R, int G, int B, int A) Texel(byte[] bc, int size, int x, int y)
    {
        var block = bc.AsSpan((y / 4 * (size / 4) + x / 4) * 16, 16); int k = y % 4 * 4 + x % 4;
        int c0 = block[8] | block[9] << 8, c1 = block[10] | block[11] << 8, pick = (int)(BitConverter.ToUInt32(block[12..]) >> k * 2 & 3);
        static (float, float, float) Rgb(int c) => ((c >> 11 & 31) * 255f / 31, (c >> 5 & 63) * 255f / 63, (c & 31) * 255f / 31);
        var (r0, g0, b0) = Rgb(c0); var (r1, g1, b1) = Rgb(c1); float t = pick switch { 0 => 0, 1 => 1, 2 => 1 / 3f, _ => 2 / 3f };
        int a0 = block[0], a1 = block[1], step = (int)(BitConverter.ToUInt64(block) >> 16 >> k * 3 & 7);
        float a = step == 0 ? a0 : step == 1 ? a1 : ((8 - step) * a0 + (step - 1) * a1) / 7f;
        return ((int)MathF.Round(r0 + (r1 - r0) * t), (int)MathF.Round(g0 + (g1 - g0) * t), (int)MathF.Round(b0 + (b1 - b0) * t), (int)MathF.Round(a));
    }

    [Fact] public void A_texture_s_blocks_hold_its_texels_and_its_mips_halve_down_to_one_block()
    {
        const int size = 32; var rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { int o = (y * size + x) * 4; rgba[o] = (byte)(x * 8); rgba[o + 1] = (byte)(x * 4 + 60); rgba[o + 2] = 40; rgba[o + 3] = (byte)(y * 8); }
        var chain = Bc3.MipChain(rgba, size, cutout: false, data: false);
        Assert.Equal([64 * 16, 16 * 16, 4 * 16, 16], chain.Select(m => m.Length));
        for (int y = 0; y < size; y += 3)
            for (int x = 0; x < size; x += 3)
            {
                var (r, g, b, a) = Texel(chain[0], size, x, y); int o = (y * size + x) * 4;
                Assert.InRange(r, rgba[o] - 8, rgba[o] + 8); Assert.InRange(g, rgba[o + 1] - 6, rgba[o + 1] + 6); Assert.InRange(b, 32, 48); Assert.InRange(a, rgba[o + 3] - 3, rgba[o + 3] + 3);
            }
        var (lr, _, _, la) = Texel(chain[^1], 4, 1, 1);   // the last level: the mean of an eighth of the picture round (12, 12)
        Assert.InRange(lr, 80, 112); Assert.InRange(la, 80, 112);
    }

    [Fact] public void A_leaf_card_keeps_its_cover_in_the_smaller_mips_and_a_normal_map_its_numbers()
    {
        const int size = 64; var card = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) card[(y * size + x) * 4 + 3] = (byte)((x / 2 + y / 2) % 4 == 0 ? 255 : 0);   // thin leaves: a quarter of the card
        var plain = Bc3.MipChain(card, size, cutout: false, data: false); var kept = Bc3.MipChain(card, size, cutout: true, data: false);
        static double Cover(byte[] bc, int s) { int n = 0; for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) if (Texel(bc, s, x, y).A > 127) n++; return n / (double)(s * s); }
        Assert.Equal(0.25, Cover(kept[0], size), 2);
        Assert.Equal(0, Cover(plain[2], size / 4), 2);        // averaged away: every texel a quarter opaque
        Assert.InRange(Cover(kept[2], size / 4), 0.2, 1.0);   // scaled back up: the crown does not thin out with distance

        // a normal map's numbers are stored sRGB-encoded, so the GPU's sRGB texture hands them back as they were
        var flat = new byte[16 * 16 * 4]; for (int i = 0; i < flat.Length; i += 4) { flat[i] = 255; flat[i + 1] = 128; flat[i + 2] = 0; flat[i + 3] = 128; }
        var (r, g, b, a) = Texel(Bc3.MipChain(flat, 16, cutout: false, data: true)[0], 16, 5, 5);
        Assert.Equal(255, r); Assert.InRange(g, 186, 190); Assert.Equal(0, b); Assert.Equal(128, a);   // 0.502 encodes to 0.737
    }
}
