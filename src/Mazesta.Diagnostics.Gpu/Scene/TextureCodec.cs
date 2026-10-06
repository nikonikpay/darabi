using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// A baseline JPEG decoder (sequential, Huffman, 8 bits, one or three components, any sampling): the garden's textures are stored as JPEG
/// pictures, a fifth of the size of the BC3 blocks the GPU reads, and are decoded when the scene is. Written here rather than left to
/// Windows' codecs so the scene is the same bytes on every machine, and reads without a window or a COM apartment.
/// </summary>
internal static class Jpeg
{
    private static readonly byte[] ZigZag = [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];
    private static readonly float[] Cos = MakeCos();
    private static float[] MakeCos()
    {
        var c = new float[64];
        for (int x = 0; x < 8; x++) for (int u = 0; u < 8; u++) c[x * 8 + u] = (u == 0 ? MathF.Sqrt(0.125f) : 0.5f) * MathF.Cos((2 * x + 1) * u * MathF.PI / 16);
        return c;
    }

    private sealed class Table
    {
        private readonly short[] _fast = new short[512]; private readonly int[] _max = new int[17], _min = new int[17], _first = new int[17]; private readonly byte[] _values;
        public Table(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            _values = values.ToArray(); Array.Fill(_fast, (short)-1);
            for (int len = 1, code = 0, k = 0; len <= 16; len++, code <<= 1)
            {
                _first[len] = k; _min[len] = code;
                for (int i = 0; i < counts[len - 1]; i++, k++, code++)
                    if (len <= 9) for (int f = 0; f < 1 << (9 - len); f++) _fast[(code << (9 - len)) + f] = (short)(len << 8 | _values[k]);
                _max[len] = counts[len - 1] > 0 ? code - 1 : -1;
            }
        }
        public int Symbol(ref Bits b)
        {
            b.Fill(); int look = _fast[(int)(b.Buffer >> 23)];
            if (look >= 0) { b.Skip(look >> 8); return look & 255; }
            for (int len = 10; len <= 16; len++)
            {
                int code = (int)(b.Buffer >> (32 - len));
                if (code <= _max[len]) { b.Skip(len); return _values[_first[len] + code - _min[len]]; }
            }
            throw new InvalidDataException("A JPEG picture's data does not follow its own tables.");
        }
    }

    /// <summary>The entropy-coded bits: the next ones are the top of <see cref="Buffer"/>. A stuffed zero after FF is skipped; at a
    /// marker the bits run out as zeros.</summary>
    private ref struct Bits(ReadOnlySpan<byte> data, int at)
    {
        private readonly ReadOnlySpan<byte> _data = data; public int At = at; public uint Buffer; private int _count;
        public void Fill()
        {
            while (_count <= 24)
            {
                uint next = 0;
                if (At < _data.Length) { next = _data[At]; if (next == 0xFF) { if (At + 1 < _data.Length && _data[At + 1] == 0) At += 2; else next = 0; } else At++; }
                Buffer |= next << (24 - _count); _count += 8;
            }
        }
        public void Skip(int n) { Buffer <<= n; _count -= n; }
        /// <summary>A coefficient of <paramref name="n"/> bits, as the standard extends it to a signed value.</summary>
        public int Value(int n)
        {
            if (n == 0) return 0;
            Fill(); int v = (int)(Buffer >> (32 - n)); Skip(n);
            return v < 1 << (n - 1) ? v - (1 << n) + 1 : v;
        }
        /// <summary>At a restart: the bits left of this interval are dropped and the marker stepped over.</summary>
        public void Restart()
        {
            Buffer = 0; _count = 0;
            while (At + 1 < _data.Length && !(_data[At] == 0xFF && _data[At + 1] is >= 0xD0 and <= 0xD7)) At++;
            At += 2;
        }
    }

    private sealed class Component { public int Id, H, V, Quant, Dc, Ac, Stride, Rows, Pred; public byte[] Plane = []; }

    /// <summary>The picture as RGB bytes, three a pixel, top row first (a grey one has its value in all three).</summary>
    public static byte[] Decode(ReadOnlySpan<byte> d, out int width, out int height)
    {
        var quant = new int[4][]; var dc = new Table?[4]; var ac = new Table?[4]; var comps = new List<Component>(); int restart = 0, at = 2; width = height = 0;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new InvalidDataException("Not a JPEG picture.");
        static InvalidDataException Short() => new("A JPEG picture is cut short.");
        while (true)
        {
            if (at + 4 > d.Length) throw Short();
            if (d[at] != 0xFF) throw new InvalidDataException("A JPEG picture has no marker where one is due.");
            int marker = d[at + 1], length = d[at + 2] << 8 | d[at + 3]; var seg = at + 2 + length <= d.Length ? d.Slice(at + 4, length - 2) : throw Short(); at += 2 + length;
            if (marker == 0xDB)
                for (int k = 0; k < seg.Length;)
                {
                    int id = seg[k] & 15; bool wide = seg[k] >> 4 != 0; k++; var q = quant[id & 3] = new int[64];
                    for (int i = 0; i < 64; i++) { q[ZigZag[i]] = wide ? seg[k] << 8 | seg[k + 1] : seg[k]; k += wide ? 2 : 1; }
                }
            else if (marker == 0xC4)
                for (int k = 0; k < seg.Length;)
                {
                    int id = seg[k] & 3; bool isAc = seg[k] >> 4 != 0; var counts = seg.Slice(k + 1, 16); int n = 0; foreach (byte c in counts) n += c;
                    (isAc ? ac : dc)[id] = new Table(counts, seg.Slice(k + 17, n)); k += 17 + n;
                }
            else if (marker == 0xC0 || marker == 0xC1)
            {
                if (seg[0] != 8) throw new InvalidDataException("A JPEG picture of other than 8 bits.");
                height = seg[1] << 8 | seg[2]; width = seg[3] << 8 | seg[4];
                for (int c = 0; c < seg[5]; c++) comps.Add(new Component { Id = seg[6 + c * 3], H = seg[7 + c * 3] >> 4, V = seg[7 + c * 3] & 15, Quant = seg[8 + c * 3] & 3 });
            }
            else if (marker is 0xC2 or 0xC3 or (>= 0xC5 and <= 0xCF and not 0xC8 and not 0xCC)) throw new InvalidDataException("A JPEG picture that is not baseline (progressive or lossless).");
            else if (marker == 0xDD) restart = seg[0] << 8 | seg[1];
            else if (marker == 0xDA)
            {
                for (int c = 0; c < seg[0]; c++) { int id = seg[1 + c * 2], tables = seg[2 + c * 2]; foreach (var comp in comps) if (comp.Id == id) { comp.Dc = tables >> 4 & 3; comp.Ac = tables & 3; } }
                break;
            }
            else if (marker == 0xD9) throw Short();
        }
        if (width <= 0 || height <= 0 || comps.Count is not (1 or 3) || comps.Any(c => c.H is < 1 or > 4 || c.V is < 1 or > 4 || quant[c.Quant] is null || dc[c.Dc] is null || ac[c.Ac] is null))
            throw new InvalidDataException("A JPEG picture's header is not one this reads.");
        int hmax = comps.Max(c => c.H), vmax = comps.Max(c => c.V), across = (width + hmax * 8 - 1) / (hmax * 8), down = (height + vmax * 8 - 1) / (vmax * 8);
        foreach (var c in comps) { c.Stride = across * c.H * 8; c.Rows = down * c.V * 8; c.Plane = new byte[c.Stride * c.Rows]; }

        var bits = new Bits(d, at); Span<float> block = stackalloc float[64], half = stackalloc float[64];
        for (int mcu = 0; mcu < across * down; mcu++)
        {
            if (restart > 0 && mcu > 0 && mcu % restart == 0) { bits.Restart(); foreach (var c in comps) c.Pred = 0; }
            int mx = mcu % across, my = mcu / across;
            foreach (var c in comps)
            {
                var q = quant[c.Quant]!; Table tdc = dc[c.Dc]!, tac = ac[c.Ac]!;
                for (int v = 0; v < c.V; v++)
                    for (int h = 0; h < c.H; h++)
                    {
                        block.Clear();
                        c.Pred += bits.Value(tdc.Symbol(ref bits)); block[0] = c.Pred * q[0];
                        for (int k = 1; k < 64;)
                        {
                            int rs = tac.Symbol(ref bits), run = rs >> 4, size = rs & 15;
                            if (size == 0) { if (run != 15) break; k += 16; continue; }
                            k += run; if (k > 63) throw new InvalidDataException("A JPEG picture's block runs past its end.");
                            block[ZigZag[k]] = bits.Value(size) * q[ZigZag[k]]; k++;
                        }
                        // the inverse cosine transform, rows then columns
                        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { float s = 0; for (int u = 0; u < 8; u++) s += Cos[x * 8 + u] * block[y * 8 + u]; half[y * 8 + x] = s; }
                        int ox = (mx * c.H + h) * 8, oy = (my * c.V + v) * 8;
                        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        {
                            float s = 0; for (int u = 0; u < 8; u++) s += Cos[y * 8 + u] * half[u * 8 + x];
                            c.Plane[(oy + y) * c.Stride + ox + x] = (byte)Math.Clamp(MathF.Round(s) + 128, 0, 255);
                        }
                    }
            }
        }

        var rgb = new byte[width * height * 3];
        if (comps.Count == 1) { for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) rgb.AsSpan((y * width + x) * 3, 3).Fill(comps[0].Plane[y * comps[0].Stride + x]); return rgb; }
        var planes = comps.Select(c => c.H == hmax && c.V == vmax ? c.Plane : Enlarged(c, hmax, vmax)).ToArray(); int stride = across * hmax * 8;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float l = planes[0][y * stride + x], cb = planes[1][y * stride + x] - 128f, cr = planes[2][y * stride + x] - 128f; int o = (y * width + x) * 3;
                rgb[o] = (byte)Math.Clamp(MathF.Round(l + 1.402f * cr), 0, 255); rgb[o + 1] = (byte)Math.Clamp(MathF.Round(l - 0.344136f * cb - 0.714136f * cr), 0, 255); rgb[o + 2] = (byte)Math.Clamp(MathF.Round(l + 1.772f * cb), 0, 255);
            }
        return rgb;
    }

    /// <summary>A plane stored smaller than the picture (the two colour planes, as a rule, at half its size each way), at the picture's
    /// size: each sample between the stored ones that lie round it.</summary>
    private static byte[] Enlarged(Component c, int hmax, int vmax)
    {
        int fx = hmax / c.H, fy = vmax / c.V, w = c.Stride * fx, h = c.Rows * fy; var big = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            float sy = Math.Clamp((y + 0.5f) / fy - 0.5f, 0, c.Rows - 1); int y0 = (int)sy, y1 = Math.Min(y0 + 1, c.Rows - 1); float ty = sy - y0;
            for (int x = 0; x < w; x++)
            {
                float sx = Math.Clamp((x + 0.5f) / fx - 0.5f, 0, c.Stride - 1); int x0 = (int)sx, x1 = Math.Min(x0 + 1, c.Stride - 1); float tx = sx - x0;
                float top = c.Plane[y0 * c.Stride + x0] * (1 - tx) + c.Plane[y0 * c.Stride + x1] * tx, bottom = c.Plane[y1 * c.Stride + x0] * (1 - tx) + c.Plane[y1 * c.Stride + x1] * tx;
                big[y * w + x] = (byte)(top * (1 - ty) + bottom * ty + 0.5f);
            }
        }
        return big;
    }
}

/// <summary>
/// BC3 (DXT5) blocks from RGBA texels, and a texture's smaller mips: what the exporter used to do before the scene file held its
/// textures as JPEG pictures. A block's two end colours lie along its texels' principal axis, drawn in a little; its alphas run in
/// eight steps between the block's highest and lowest.
/// </summary>
internal static class Bc3
{
    private static readonly float[] ToSrgb = MakeToSrgb();
    private static float[] MakeToSrgb()
    {
        var t = new float[16385];
        for (int i = 0; i < t.Length; i++) { float x = i / 16384f; t[i] = (x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - 0.055f) * 255; }
        return t;
    }

    /// <summary>A texture's whole mip chain as BC3, <paramref name="size"/> down to 4, largest first. <paramref name="rgba"/>: its texels,
    /// four bytes each. Each smaller level is the mean of two by two of the one before. <paramref name="cutout"/>: alpha is a leaf card's
    /// opacity, and each level's is scaled so as many of its texels pass the one-half test as at full size (a crown keeps its cover at
    /// a distance). <paramref name="data"/>: the three colour channels hold plain numbers (a normal map's), not colour: the GPU's
    /// texture is sRGB, so they are stored encoded to come back as they were.</summary>
    public static byte[][] MipChain(byte[] rgba, int size, bool cutout, bool data)
    {
        var level = new float[rgba.Length]; for (int i = 0; i < rgba.Length; i++) level[i] = rgba[i];
        float cover = 0; if (cutout) { for (int i = 3; i < level.Length; i += 4) if (level[i] > 127.5f) cover++; cover /= size * size; }
        var chain = new List<byte[]>();
        for (int s = size; ; s /= 2)
        {
            chain.Add(Encode(level, s, data));
            if (s <= 4) break;
            int h = s / 2; var next = new float[h * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    for (int c = 0; c < 4; c++)
                        next[(y * h + x) * 4 + c] = (level[(y * 2 * s + x * 2) * 4 + c] + level[(y * 2 * s + x * 2 + 1) * 4 + c] + level[((y * 2 + 1) * s + x * 2) * 4 + c] + level[((y * 2 + 1) * s + x * 2 + 1) * 4 + c]) * 0.25f;
            if (cutout && cover > 0)
            {
                float lo = 0.5f, hi = 8f;
                for (int k = 0; k < 12; k++)
                {
                    float mid = (lo + hi) / 2, pass = 0; for (int i = 3; i < next.Length; i += 4) if (next[i] * mid > 127.5f) pass++;
                    if (pass / (h * h) < cover) lo = mid; else hi = mid;
                }
                for (int i = 3; i < next.Length; i += 4) next[i] = MathF.Min(next[i] * hi, 255);
            }
            level = next;
        }
        return [.. chain];
    }

    private static byte[] Encode(float[] texels, int size, bool data)
    {
        int blocks = size / 4; var bc = new byte[blocks * blocks * 16];
        Span<float> rgb = stackalloc float[48]; Span<int> alpha = stackalloc int[16]; Span<float> palette = stackalloc float[12];
        for (int by = 0; by < blocks; by++)
            for (int bx = 0; bx < blocks; bx++)
            {
                float mr = 0, mg = 0, mb = 0; int a0 = 0, a1 = 255;
                for (int k = 0; k < 16; k++)
                {
                    int o = ((by * 4 + k / 4) * size + bx * 4 + k % 4) * 4;
                    for (int c = 0; c < 3; c++) { float v = texels[o + c]; rgb[k * 3 + c] = MathF.Floor((data ? ToSrgb[(int)(Math.Clamp(v, 0, 255) * (16384f / 255) + 0.5f)] : Math.Clamp(v, 0, 255)) + 0.5f); }
                    mr += rgb[k * 3]; mg += rgb[k * 3 + 1]; mb += rgb[k * 3 + 2];
                    int a = alpha[k] = (int)(Math.Clamp(texels[o + 3], 0, 255) + 0.5f); a0 = Math.Max(a0, a); a1 = Math.Min(a1, a);
                }
                mr /= 16; mg /= 16; mb /= 16;
                float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                for (int k = 0; k < 16; k++) { float r = rgb[k * 3] - mr, g = rgb[k * 3 + 1] - mg, b = rgb[k * 3 + 2] - mb; xx += r * r; xy += r * g; xz += r * b; yy += g * g; yz += g * b; zz += b * b; }
                float ax = 1, ay = 1, az = 1;
                for (int k = 0; k < 6; k++)
                {
                    float nx = xx * ax + xy * ay + xz * az, ny = xy * ax + yy * ay + yz * az, nz = xz * ax + yz * ay + zz * az, len = MathF.Max(MathF.Sqrt(nx * nx + ny * ny + nz * nz), 1e-6f);
                    ax = nx / len; ay = ny / len; az = nz / len;
                }
                float tmin = float.MaxValue, tmax = float.MinValue;
                for (int k = 0; k < 16; k++) { float t = (rgb[k * 3] - mr) * ax + (rgb[k * 3 + 1] - mg) * ay + (rgb[k * 3 + 2] - mb) * az; tmin = MathF.Min(tmin, t); tmax = MathF.Max(tmax, t); }
                float inset = (tmax - tmin) / 16; tmax -= inset; tmin += inset;
                int c0 = To565(mr + ax * tmax, mg + ay * tmax, mb + az * tmax), c1 = To565(mr + ax * tmin, mg + ay * tmin, mb + az * tmin);
                From565(c0, palette); From565(c1, palette[3..]);
                for (int c = 0; c < 3; c++) { palette[6 + c] = (2 * palette[c] + palette[3 + c]) / 3; palette[9 + c] = (palette[c] + 2 * palette[3 + c]) / 3; }
                uint picks = 0;
                for (int k = 0; k < 16; k++)
                {
                    int best = 0; float least = float.MaxValue;
                    for (int p = 0; p < 4; p++)
                    {
                        float dr = rgb[k * 3] - palette[p * 3], dg = rgb[k * 3 + 1] - palette[p * 3 + 1], db = rgb[k * 3 + 2] - palette[p * 3 + 2], dist = dr * dr + dg * dg + db * db;
                        if (dist < least) { least = dist; best = p; }
                    }
                    picks |= (uint)best << k * 2;
                }
                ulong steps = 0;
                if (a0 != a1)
                    for (int k = 0; k < 16; k++)
                    {
                        int best = 0; float least = float.MaxValue;
                        for (int p = 0; p < 8; p++)
                        {
                            float value = p == 0 ? a0 : p == 1 ? a1 : ((8 - p) * a0 + (p - 1) * a1) / 7f, dist = MathF.Abs(alpha[k] - value);
                            if (dist < least) { least = dist; best = p; }
                        }
                        steps |= (ulong)best << k * 3;
                    }
                var block = bc.AsSpan((by * blocks + bx) * 16, 16);
                block[0] = (byte)a0; block[1] = (byte)a1; for (int k = 0; k < 6; k++) block[2 + k] = (byte)(steps >> k * 8);
                block[8] = (byte)c0; block[9] = (byte)(c0 >> 8); block[10] = (byte)c1; block[11] = (byte)(c1 >> 8);
                MemoryMarshal.Write(block[12..], picks);
            }
        return bc;
    }

    private static int To565(float r, float g, float b)
    {
        int R = (int)Math.Clamp(MathF.Round(r), 0, 255), G = (int)Math.Clamp(MathF.Round(g), 0, 255), B = (int)Math.Clamp(MathF.Round(b), 0, 255);
        return (R >> 3) << 11 | (G >> 2) << 5 | (B >> 3);
    }
    private static void From565(int v, Span<float> rgb) { rgb[0] = (v >> 11 & 31) * 255f / 31; rgb[1] = (v >> 5 & 63) * 255f / 63; rgb[2] = (v & 31) * 255f / 31; }
}
