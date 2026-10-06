using System.IO.Compression; using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The light that reaches each part of the courtyard from everything but the key light and the lamps themselves: the sky where it can
/// be seen, and what the sunlit paving, the walls and the lit rooms give back. A rasteriser cannot work that out as it draws, so it is
/// worked out beforehand, by the app's own ray tracer (<see cref="GardenLightBaker"/>), at the points of a grid <see cref="Spacing"/>
/// apart that fills the courtyard and the hall - at each point for the six ways a surface there can face - and kept beside the scene
/// in garden.light, embedded in this assembly. The Direct3D garden reads it for every pixel (GardenRaster.hlsl's Bounced).
/// <para>The garden's light changes through the day (<see cref="GardenDay"/>), and light adds up: the volume is kept in
/// <see cref="Parts"/>, each the light of one source alone - the sky, of one unit of brightness all over (<see cref="Sky"/>); the
/// lamps, all lit (<see cref="Lamps"/>); and the sun, of one unit, at each of <see cref="Suns"/> points of its arc
/// (<see cref="FirstSun"/> on: point k is (k + 1/2) / Suns of the way from sunrise to sunset). <see cref="Mix"/> puts a moment's
/// light together from them.</para>
/// <para>Format (gzip, little-endian): "MZLT", version, the <see cref="GardenScene.Stamp"/> of the scene it was worked out for, the
/// grid's first point and spacing, its counts along x, y and z, the number of parts, then for each part four bytes for each point's
/// six directions (x fastest, then y, then z; +x, -x, +y, -y, +z, -z): red, green and blue sharing one exponent.</para>
/// </summary>
public sealed class GardenLightVolume
{
    public const int Version = 2, Sky = 0, Lamps = 1, FirstSun = 2;
    public ulong SceneStamp { get; private init; }
    public Vector3 Origin { get; private init; }
    public float Spacing { get; private init; }
    public int X { get; private init; } public int Y { get; private init; } public int Z { get; private init; }
    /// <summary>Each part's radiance (irradiance over pi, linear) for point <c>x + X * (y + Y * z)</c>, direction d: three floats at
    /// <c>((that) * 6 + d) * 3</c>.</summary>
    public IReadOnlyList<float[]> Parts { get; private init; } = [];
    public int Suns => Parts.Count - FirstSun;
    private float[][]? _texels;

    public GardenLightVolume() { }
    public GardenLightVolume(ulong sceneStamp, Vector3 origin, float spacing, int x, int y, int z, IReadOnlyList<float[]> parts)
    {
        if (parts.Count <= FirstSun || parts.Any(p => p.Length != x * y * z * 18)) throw new ArgumentException("The light does not fill the grid.", nameof(parts));
        SceneStamp = sceneStamp; Origin = origin; Spacing = spacing; X = x; Y = y; Z = z; Parts = parts;
    }

    /// <summary>The light of <paramref name="part"/> a surface at <paramref name="p"/> facing <paramref name="n"/> gets, from the
    /// grid point nearest it (the shader blends the eight round it): for the tests.</summary>
    public Vector3 At(int part, Vector3 p, Vector3 n)
    {
        var light = Parts[part];
        var q = (p - Origin) / Spacing; int x = Math.Clamp((int)MathF.Round(q.X), 0, X - 1), y = Math.Clamp((int)MathF.Round(q.Y), 0, Y - 1), z = Math.Clamp((int)MathF.Round(q.Z), 0, Z - 1);
        Vector3 Face(int d) { int o = ((x + X * (y + Y * z)) * 6 + d) * 3; return new(light[o], light[o + 1], light[o + 2]); }
        return n.X * n.X * Face(n.X < 0 ? 1 : 0) + n.Y * n.Y * Face(n.Y < 0 ? 3 : 2) + n.Z * n.Z * Face(n.Z < 0 ? 5 : 4);
    }

    /// <summary>The two sun parts a sun at <paramref name="s"/> of its arc lies between, and how far from the first to the second.</summary>
    public (int A, int B, float T) SunParts(float s)
    {
        float f = Math.Clamp(s * Suns - 0.5f, 0, Suns - 1); int a = Math.Min((int)f, Suns - 1);
        return (a, Math.Min(a + 1, Suns - 1), f - a);
    }

    /// <summary>How many floats the texture the shader reads holds: six blocks side by side along x, one for each direction, four floats a texel.</summary>
    public int TexelFloats => X * 6 * Y * Z * 4;

    /// <summary>A moment's light as that texture: the sky's part times the sky's colour, the lamps' times how far they are lit, and
    /// the sun's, between the two points of its arc it stands between, times its light. Some two million multiplications, done
    /// eight or sixteen at a time: it is put together anew for every frame.</summary>
    public void Mix(float[] into, Vector3 sky, float lamps, Vector3 sun, float s)
    {
        if (into.Length != TexelFloats) throw new ArgumentException("Not the size of the volume's texture.", nameof(into));
        var t = _texels ??= [.. Parts.Select(Texels)]; var (a, b, f) = SunParts(s); int n = Vector<float>.Count;
        float[] p0 = t[Sky], p1 = t[Lamps], pa = t[FirstSun + a], pb = t[FirstSun + b];
        Vector3 wa = sun * (1 - f), wb = sun * f; int i = 0;
        if (Vector.IsHardwareAccelerated && n % 4 == 0)
        {
            Vector<float> vs = Weights(sky), vl = Weights(new(lamps)), va = Weights(wa), vb = Weights(wb);
            for (; i <= into.Length - n; i += n) (new Vector<float>(p0, i) * vs + new Vector<float>(p1, i) * vl + new Vector<float>(pa, i) * va + new Vector<float>(pb, i) * vb).CopyTo(into, i);
        }
        for (; i < into.Length; i++)
        {
            int c = i & 3; float Of(Vector3 w) => c == 0 ? w.X : c == 1 ? w.Y : c == 2 ? w.Z : 0;
            into[i] = p0[i] * Of(sky) + p1[i] * (c < 3 ? lamps : 0) + pa[i] * Of(wa) + pb[i] * Of(wb);
        }
    }

    /// <summary>A colour as the weights of red, green, blue and nothing, over and over.</summary>
    private static Vector<float> Weights(Vector3 c)
    {
        Span<float> w = stackalloc float[Vector<float>.Count];
        for (int i = 0; i < w.Length; i++) w[i] = (i & 3) switch { 0 => c.X, 1 => c.Y, 2 => c.Z, _ => 0 };
        return new Vector<float>(w);
    }

    /// <summary>One part in the texture's order: six blocks along x, x fastest, then y, then z; red, green, blue and a fourth float of nothing.</summary>
    private float[] Texels(float[] light)
    {
        var t = new float[TexelFloats];
        for (int z = 0; z < Z; z++) for (int y = 0; y < Y; y++) for (int x = 0; x < X; x++) for (int d = 0; d < 6; d++)
        {
            int from = ((x + X * (y + Y * z)) * 6 + d) * 3, to = ((z * Y + y) * X * 6 + d * X + x) * 4;
            t[to] = light[from]; t[to + 1] = light[from + 1]; t[to + 2] = light[from + 2];
        }
        return t;
    }

    public void Write(Stream to)
    {
        using var gz = new GZipStream(to, CompressionLevel.SmallestSize, leaveOpen: true); using var w = new BinaryWriter(gz);
        w.Write("MZLT"u8); w.Write(Version); w.Write(SceneStamp); w.Write(Origin.X); w.Write(Origin.Y); w.Write(Origin.Z); w.Write(Spacing); w.Write(X); w.Write(Y); w.Write(Z); w.Write(Parts.Count);
        foreach (var light in Parts)
        {
            var packed = new byte[light.Length / 3 * 4];
            for (int i = 0; i < light.Length / 3; i++) Pack(new(light[i * 3], light[i * 3 + 1], light[i * 3 + 2]), packed.AsSpan(i * 4, 4));
            // the four bytes of each texel in planes of their own (all the reds, then the greens...): gzip makes less of them that way
            for (int plane = 0; plane < 4; plane++) for (int i = plane; i < packed.Length; i += 4) w.Write(packed[i]);
        }
    }

    public static GardenLightVolume Read(Stream compressed)
    {
        using var gz = new GZipStream(compressed, CompressionMode.Decompress); using var ms = new MemoryStream(); gz.CopyTo(ms);
        var d = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        if (d.Length < 48 || !d[..4].SequenceEqual("MZLT"u8)) throw new InvalidDataException("Not a Mazesta light file.");
        int version = BitConverter.ToInt32(d[4..]); if (version != Version) throw new InvalidDataException($"Light file version {version}; this build reads {Version}.");
        ulong stamp = BitConverter.ToUInt64(d[8..]); var f = MemoryMarshal.Cast<byte, float>(d.Slice(16, 16)); var n = MemoryMarshal.Cast<byte, int>(d.Slice(32, 16));
        int x = n[0], y = n[1], z = n[2], parts = n[3];
        if (x is < 2 or > 512 || y is < 2 or > 512 || z is < 2 or > 512 || parts is <= FirstSun or > 64 || !(f[3] > 0) || d.Length != 48 + (long)parts * x * y * z * 24) throw new InvalidDataException($"A light grid of {x} x {y} x {z} in {parts} parts, in {d.Length} bytes.");
        int count = x * y * z * 6; var all = new float[parts][]; Span<byte> texel = stackalloc byte[4];
        for (int p = 0; p < parts; p++)
        {
            var planes = d.Slice(48 + p * count * 4, count * 4); var light = all[p] = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                for (int plane = 0; plane < 4; plane++) texel[plane] = planes[plane * count + i];
                var c = Unpack(texel); light[i * 3] = c.X; light[i * 3 + 1] = c.Y; light[i * 3 + 2] = c.Z;
            }
        }
        return new GardenLightVolume(stamp, new(f[0], f[1], f[2]), f[3], x, y, z, all);
    }

    /// <summary>A colour as three bytes sharing one power of two (Ward's RGBE): light from the hall's shade to the sunlit wall, in four bytes.
    /// Each colour keeps six bits (steps of a sixty-fourth: under two parts in a hundred of the brightest) - what the ray tracer found
    /// is a mean of some hundreds of rays, and its last bits are chance, which no compression makes smaller.</summary>
    internal static void Pack(Vector3 c, Span<byte> to)
    {
        float most = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (!(most > 1e-20f)) { to.Clear(); return; }
        int e = (int)MathF.Floor(MathF.Log2(most)) + 1; float scale = MathF.ScaleB(256, -e);   // most / 2^e is in [0.5, 1)
        to[0] = (byte)((int)Math.Clamp(c.X * scale, 0, 255) & ~3); to[1] = (byte)((int)Math.Clamp(c.Y * scale, 0, 255) & ~3); to[2] = (byte)((int)Math.Clamp(c.Z * scale, 0, 255) & ~3); to[3] = (byte)Math.Clamp(e + 128, 1, 255);
    }
    internal static Vector3 Unpack(ReadOnlySpan<byte> from) => from[3] == 0 ? Vector3.Zero : new Vector3(from[0] + 2f, from[1] + 2f, from[2] + 2f) * MathF.ScaleB(1f / 256, from[3] - 128);
}

/// <summary>
/// Works the garden's <see cref="GardenLightVolume"/> out with the ray tracer (GardenRay.hlsl's Bake), on a GPU that has ray-tracing
/// hardware: from each grid point rays are sent out round each of the six directions and the mean of the light they find is taken -
/// the surfaces they meet lit with shadow rays, and by a bounce of their own - once for each of the volume's parts, with that part's
/// light alone: the sky, the lamps, the sun at each point of its arc. Run after the scene is exported, by the hardware test that
/// writes garden.light (tools/scene/export_garden.py says how); the app itself never bakes.
/// </summary>
internal static class GardenLightBaker
{
    /// <summary>The grid: from just inside the outer walls' feet to over the hall's roof, a point every 0.75 m.</summary>
    public static readonly Vector3 Origin = new(-14.25f, 0.2f, -33.75f);
    public const float Spacing = 0.75f; public const int X = 39, Y = 13, Z = 57;
    /// <summary>The baker follows light for two bounces. In the garden that is nearly all of it; in the hall's rooms, whose pale
    /// plaster gives most of the light back each time, what the sky and the sun send in at the windows goes on from wall to wall:
    /// there the two bounces are taken times what the rest add (1 / (1 - 0.7), less the part already counted).</summary>
    public const float Rebound = 2.6f;
    /// <summary>At how many points of its arc the sun's light is worked out: every 18 degrees round, between which bounced light changes little.</summary>
    public const int Suns = 6;

    public static GardenLightVolume Bake(D3D12Session s, GardenScene scene, uint rays = 1024)
    {
        int count = X * Y * Z * 6;
        var g = new GardenGpu(s, scene, GardenScene.Mode.Raster);
        using var ray = new GardenRay(s, g, GardenRay.BakeRow, (count + GardenRay.BakeRow - 1) / GardenRay.BakeRow / 8 * 8 + 8, []);
        float[] Part(GardenRay.FrameSetup light) => Settled(ray.BakeLight(new(Origin, Spacing), new(X, Y, Z), count, rays, (ref GardenFrame f) =>
        {
            // nothing shines but what the part is of: no key light, no lamp lit, no sky, nothing glowing, and no flame wavering
            f.SunOn = 0; f.SunColor = Vector3.Zero; f.LightCount = 0; f.SkyZenith = f.SkyHorizon = f.GroundColor = Vector3.Zero; f.Post = default; f.Day = default; f.Logo.W = 0;
            light(ref f);
        }));
        float[] Indoors(float[] light)
        {
            var (_, low, high) = GardenRaster.Rounds[1];
            for (int z = 0; z < Z; z++) for (int y = 0; y < Y; y++) for (int x = 0; x < X; x++)
            {
                var p = Origin + new Vector3(x, y, z) * Spacing; if (p.X < low.X || p.Y < low.Y || p.Z < low.Z || p.X > high.X || p.Y > high.Y || p.Z > high.Z) continue;
                int at = (x + X * (y + Y * z)) * 18; for (int k = 0; k < 18; k++) light[at + k] *= Rebound;
            }
            return light;
        }
        var parts = new List<float[]>
        {
            Indoors(Part((ref GardenFrame f) => { f.SkyZenith = Vector3.One; f.Post.Y = 1; })),
            Part((ref GardenFrame f) => { f.LightCount = (uint)g.PointLights.Length; f.Day = new(1, 1, 1, 0); f.Logo.W = 0.45f; }),
        };
        for (int k = 0; k < Suns; k++)
        {
            var toSun = GardenDay.SunDirection((k + 0.5f) / Suns, g.SunDirection.X < 0 ? -1 : 1);
            parts.Add(Indoors(Part((ref GardenFrame f) => { f.SunOn = 1; f.SunDir = toSun; f.SunColor = Vector3.One; })));
        }
        return new GardenLightVolume(scene.Stamp, Origin, Spacing, X, Y, Z, parts);
    }

    /// <summary>A part's light from what the ray tracer found (red, green, blue, and the share of the rays that met the back of a
    /// surface). A point inside a wall or under the floor sees the backs of surfaces: its light means nothing. It takes the mean of
    /// its neighbours that do stand in the open, and so on inward, so nothing a surface reads is black for no reason.</summary>
    private static float[] Settled(float[] raw)
    {
        int points = X * Y * Z; var light = new float[points * 18]; var open = new bool[points];
        for (int p = 0; p < points; p++)
        {
            float back = 0; for (int d = 0; d < 6; d++) back += raw[(p * 6 + d) * 4 + 3];
            open[p] = back / 6 < 0.2f;
            for (int d = 0; d < 6; d++) for (int c = 0; c < 3; c++) light[(p * 6 + d) * 3 + c] = raw[(p * 6 + d) * 4 + c];
        }
        (int, int, int)[] sides = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];
        for (int pass = 0; pass < 16 && open.Contains(false); pass++)
        {
            var next = (bool[])open.Clone();
            for (int z = 0; z < Z; z++) for (int y = 0; y < Y; y++) for (int x = 0; x < X; x++)
            {
                int p = x + X * (y + Y * z); if (open[p]) continue;
                var sum = new float[18]; int n = 0;
                foreach (var (dx, dy, dz) in sides)
                {
                    int qx = x + dx, qy = y + dy, qz = z + dz; if (qx < 0 || qy < 0 || qz < 0 || qx >= X || qy >= Y || qz >= Z) continue;
                    int q = qx + X * (qy + Y * qz); if (!open[q]) continue;
                    for (int k = 0; k < 18; k++) sum[k] += light[q * 18 + k]; n++;
                }
                if (n == 0) continue;
                for (int k = 0; k < 18; k++) light[p * 18 + k] = sum[k] / n; next[p] = true;
            }
            open = next;
        }
        return light;
    }
}
