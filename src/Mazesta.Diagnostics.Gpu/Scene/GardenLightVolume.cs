using System.IO.Compression; using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The light that reaches each part of the courtyard from everything but the sun and the lamps themselves: the sky where it can be
/// seen, and what the sunlit paving, the walls and the lit rooms give back. A rasteriser cannot work that out as it draws, so it is worked
/// out beforehand, by the app's own ray tracer (<see cref="GardenLightBaker"/>), at the points of a grid <see cref="Spacing"/> apart
/// that fills the courtyard and the hall - at each point for the six ways a surface there can face - and kept beside the scene in
/// garden.light, embedded in this assembly. The Direct3D garden reads it for every pixel (GardenRaster.hlsl's Bounced).
/// <para>Format (gzip, little-endian): "MZLT", version, the <see cref="GardenScene.Stamp"/> of the scene it was worked out for, the
/// grid's first point and spacing, its counts along x, y and z, then four bytes for each point's six directions (x fastest, then y,
/// then z; +x, -x, +y, -y, +z, -z): red, green and blue sharing one exponent.</para>
/// </summary>
public sealed class GardenLightVolume
{
    public const int Version = 1;
    public ulong SceneStamp { get; private init; }
    public Vector3 Origin { get; private init; }
    public float Spacing { get; private init; }
    public int X { get; private init; } public int Y { get; private init; } public int Z { get; private init; }
    /// <summary>Radiance (irradiance over pi, linear) for point <c>x + X * (y + Y * z)</c>, direction d: three floats at
    /// <c>((that) * 6 + d) * 3</c>.</summary>
    public float[] Light { get; private init; } = [];

    public GardenLightVolume() { }
    public GardenLightVolume(ulong sceneStamp, Vector3 origin, float spacing, int x, int y, int z, float[] light)
    {
        if (light.Length != x * y * z * 18) throw new ArgumentException("The light does not fill the grid.", nameof(light));
        SceneStamp = sceneStamp; Origin = origin; Spacing = spacing; X = x; Y = y; Z = z; Light = light;
    }

    /// <summary>The light a surface at <paramref name="p"/> facing <paramref name="n"/> gets, from the grid point nearest it (the
    /// shader blends the eight round it): for the tests.</summary>
    public Vector3 At(Vector3 p, Vector3 n)
    {
        var q = (p - Origin) / Spacing; int x = Math.Clamp((int)MathF.Round(q.X), 0, X - 1), y = Math.Clamp((int)MathF.Round(q.Y), 0, Y - 1), z = Math.Clamp((int)MathF.Round(q.Z), 0, Z - 1);
        Vector3 Face(int d) { int o = ((x + X * (y + Y * z)) * 6 + d) * 3; return new(Light[o], Light[o + 1], Light[o + 2]); }
        return n.X * n.X * Face(n.X < 0 ? 1 : 0) + n.Y * n.Y * Face(n.Y < 0 ? 3 : 2) + n.Z * n.Z * Face(n.Z < 0 ? 5 : 4);
    }

    /// <summary>The grid as the texture the shader reads: six blocks side by side along x, one for each direction, four half floats a texel.</summary>
    public Half[] Texels()
    {
        var t = new Half[X * 6 * Y * Z * 4];
        for (int z = 0; z < Z; z++) for (int y = 0; y < Y; y++) for (int x = 0; x < X; x++) for (int d = 0; d < 6; d++)
        {
            int from = ((x + X * (y + Y * z)) * 6 + d) * 3, to = ((z * Y + y) * X * 6 + d * X + x) * 4;
            t[to] = (Half)Light[from]; t[to + 1] = (Half)Light[from + 1]; t[to + 2] = (Half)Light[from + 2]; t[to + 3] = (Half)1f;
        }
        return t;
    }

    public void Write(Stream to)
    {
        using var gz = new GZipStream(to, CompressionLevel.SmallestSize, leaveOpen: true); using var w = new BinaryWriter(gz);
        w.Write("MZLT"u8); w.Write(Version); w.Write(SceneStamp); w.Write(Origin.X); w.Write(Origin.Y); w.Write(Origin.Z); w.Write(Spacing); w.Write(X); w.Write(Y); w.Write(Z);
        var packed = new byte[Light.Length / 3 * 4];
        for (int i = 0; i < Light.Length / 3; i++) Pack(new(Light[i * 3], Light[i * 3 + 1], Light[i * 3 + 2]), packed.AsSpan(i * 4, 4));
        // the four bytes of each texel in planes of their own (all the reds, then the greens...): gzip makes less of them that way
        for (int plane = 0; plane < 4; plane++) for (int i = plane; i < packed.Length; i += 4) w.Write(packed[i]);
    }

    public static GardenLightVolume Read(Stream compressed)
    {
        using var gz = new GZipStream(compressed, CompressionMode.Decompress); using var ms = new MemoryStream(); gz.CopyTo(ms);
        var d = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        if (d.Length < 44 || !d[..4].SequenceEqual("MZLT"u8)) throw new InvalidDataException("Not a Mazesta light file.");
        int version = BitConverter.ToInt32(d[4..]); if (version != Version) throw new InvalidDataException($"Light file version {version}; this build reads {Version}.");
        ulong stamp = BitConverter.ToUInt64(d[8..]); var f = MemoryMarshal.Cast<byte, float>(d.Slice(16, 16)); var n = MemoryMarshal.Cast<byte, int>(d.Slice(32, 12));
        int x = n[0], y = n[1], z = n[2];
        if (x is < 2 or > 512 || y is < 2 or > 512 || z is < 2 or > 512 || !(f[3] > 0) || d.Length != 44 + (long)x * y * z * 24) throw new InvalidDataException($"A light grid of {x} x {y} x {z} in {d.Length} bytes.");
        int count = x * y * z * 6; var planes = d[44..]; var light = new float[count * 3]; Span<byte> texel = stackalloc byte[4];
        for (int i = 0; i < count; i++)
        {
            for (int plane = 0; plane < 4; plane++) texel[plane] = planes[plane * count + i];
            var c = Unpack(texel); light[i * 3] = c.X; light[i * 3 + 1] = c.Y; light[i * 3 + 2] = c.Z;
        }
        return new GardenLightVolume(stamp, new(f[0], f[1], f[2]), f[3], x, y, z, light);
    }

    /// <summary>A colour as three bytes sharing one power of two (Ward's RGBE): light from the hall's shade to the sunlit wall, in four bytes.</summary>
    internal static void Pack(Vector3 c, Span<byte> to)
    {
        float most = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (!(most > 1e-20f)) { to.Clear(); return; }
        int e = (int)MathF.Floor(MathF.Log2(most)) + 1; float scale = MathF.ScaleB(256, -e);   // most / 2^e is in [0.5, 1)
        to[0] = (byte)Math.Clamp(c.X * scale, 0, 255); to[1] = (byte)Math.Clamp(c.Y * scale, 0, 255); to[2] = (byte)Math.Clamp(c.Z * scale, 0, 255); to[3] = (byte)Math.Clamp(e + 128, 1, 255);
    }
    internal static Vector3 Unpack(ReadOnlySpan<byte> from) => from[3] == 0 ? Vector3.Zero : new Vector3(from[0] + 0.5f, from[1] + 0.5f, from[2] + 0.5f) * MathF.ScaleB(1f / 256, from[3] - 128);
}

/// <summary>
/// Works the garden's <see cref="GardenLightVolume"/> out with the ray tracer (GardenRay.hlsl's Bake), on a GPU that has ray-tracing
/// hardware: the Direct3D scene (its sun, its lamps, its sky) is given to the ray tracer, which from each grid point sends rays out
/// round each of the six directions and takes the mean of the light they find - the surfaces they meet lit by the sun and the lamps
/// with shadow rays, and by a bounce of their own. Run after the scene is exported, by the hardware test that writes garden.light
/// (tools/scene/export_garden.py says how); the app itself never bakes.
/// </summary>
internal static class GardenLightBaker
{
    /// <summary>The grid: from just inside the outer walls' feet to over the hall's roof, a point every 0.75 m.</summary>
    public static readonly Vector3 Origin = new(-14.25f, 0.2f, -33.75f);
    public const float Spacing = 0.75f; public const int X = 39, Y = 13, Z = 57;
    /// <summary>How much of the sky's own brightness the volume holds: the picture's exposure is set for sunlit stone, and by day the
    /// sky's light is a fraction of the sun's (Garden.hlsli's Ambient keeps the same share for what has no volume).</summary>
    public const float SkyGain = 0.34f;

    public static GardenLightVolume Bake(D3D12Session s, GardenScene scene, uint rays = 384)
    {
        int count = X * Y * Z * 6;
        var g = new GardenGpu(s, scene, GardenScene.Mode.Raster);
        using var ray = new GardenRay(s, g, GardenRay.BakeRow, (count + GardenRay.BakeRow - 1) / GardenRay.BakeRow / 8 * 8 + 8, []);
        var raw = ray.BakeLight(new(Origin, Spacing), new(X, Y, Z), count, rays, SkyGain);
        // A point inside a wall or under the floor sees the backs of surfaces: its light means nothing. It takes the mean of its
        // neighbours that do stand in the open, and so on inward, so nothing a surface reads is black for no reason.
        int points = X * Y * Z; var light = new float[count * 3]; var open = new bool[points];
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
        return new GardenLightVolume(scene.Stamp, Origin, Spacing, X, Y, Z, light);
    }
}
