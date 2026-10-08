using System.IO.Compression; using System.Numerics; using System.Reflection; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The Persian garden both visual GPU tests draw: a walled courtyard with a pool and its fountain, a columned hall with windcatchers and
/// furnished rooms behind its orsi, cypresses, blossom trees, planted beds, ivy, lanterns, a gate, and the Mazesta logo floating over the
/// water. Built in Blender (Mazesta-Art/courtyard-v12.blend: the owner's DFM_Courtyard_V12, whole) and written by tools/scene/export_garden.py
/// into garden.mzscene, embedded in this assembly; this reads it. The file holds both of the .blend's scenes, each thing marked with
/// the <see cref="Mode"/> it is of: the Direct3D scene has the day's sun, the ray-traced one the night's moon and lamps - both tests
/// go through the whole day with them (<see cref="GardenDay"/>) - and a mirror sphere, which only the ray-traced test shows.
/// <para>Format (gzip, little-endian): "MZSC", version, then counts of textures, materials, meshes, instances, lights and the texture size;
/// the textures (each its own size, whether it is a leaf card's or a normal map's, and its parts: JPEG pictures for the colour and for
/// each further channel, a leaf's opacity as one bit a texel, or one value for a channel that is the same all over - <see cref="Bc3"/>
/// makes the GPU's blocks and the smaller mips of them here); the materials
/// (<see cref="GardenMaterial"/>, 96 bytes); each mesh (vertex and index counts, quantisation centre and extent, submeshes, 16-byte vertices,
/// 32-bit indices); the instances (mesh, scene mask, flags, 3x4 world rows); the lights; one camera per scene; the backdrop
/// (<see cref="GardenBackdrop"/>: width, height, the range it covers and one BC3 image; width 0 when there is none); and seven floats:
/// the pool's water level and the fountain (<see cref="GardenFountain"/>; a rim radius of 0 when there is none).
/// Vertices, indices and instances are stored byte plane by byte plane (every record's first byte, then every second...), the indices
/// as differences from the one before: gzip makes far less of them that way. Axes are Direct3D's: y up, z forward.</para>
/// </summary>
public sealed class GardenScene
{
    public const int Version = 4;
    [Flags] public enum Mode : uint { Raster = 1, RayTraced = 2 }
    /// <summary>An instance's flags: the logo; the ray-traced scene's mirror sphere; a droplet of the fountain (those are not in the
    /// file: <see cref="GardenGpu"/> adds them).</summary>
    public const uint LogoFlag = 1, SphereFlag = 2, DropletFlag = 4;
    /// <summary>A light fitting (a chandelier): the lamps cast no shadow of it in the Direct3D scene, where a lamp is one point and the
    /// fitting's own bulbs are all round it.</summary>
    public const uint FittingFlag = 8;
    /// <summary>A small thing alive in the garden - a firefly, a butterfly's wing, a puff of steam (those are not in the file either:
    /// <see cref="GardenGpu"/> adds them, and says where each is at a moment).</summary>
    public const uint MoverFlag = 16;
    /// <summary>A body of the weather (rain, a splash, a leaf, a twig: <see cref="GardenWeather"/>): a mover too, placed by the processor every frame, but not part of
    /// what rays see - the ray tracer's top-level structure stops before them.</summary>
    public const uint WeatherFlag = 32;
    /// <summary>Bits 8 to 15 of an instance's flags: how far the wind bends it, in thousandths of a metre sideways for every metre above
    /// where it stands (a plant; 0 for everything else). <see cref="GardenGpu.Wind"/> says which way and when.</summary>
    public const int SwayShift = 8;
    public static float Sway(uint flags) => (flags >> SwayShift & 255) / 1000f;

    public int TextureSize { get; private init; }
    /// <summary>Mip levels of the texture array: <see cref="TextureSize"/> down to 4.</summary>
    public int TextureMips => int.Log2(Math.Max(4, TextureSize)) - 1;
    /// <summary>Each texture's BC3 mip chain, largest first, from its own size (<see cref="TextureSize"/> or a power of two under it:
    /// the array's larger levels are then that one doubled, <see cref="TextureLevel"/>) down to 4.</summary>
    public IReadOnlyList<byte[][]> Textures { get; private init; } = [];
    /// <summary>Texture <paramref name="texture"/> at level <paramref name="mip"/> of the array (0: <see cref="TextureSize"/> across).</summary>
    public byte[] TextureLevel(int texture, int mip)
    {
        var chain = Textures[texture]; int own = 4 << (chain.Length - 1), wanted = TextureSize >> mip;
        if (wanted <= own) return chain[int.Log2(own) - int.Log2(wanted)];
        var level = chain[0]; for (int s = own; s < wanted; s *= 2) level = Bc3Doubled(level, s / 4);
        return level;
    }
    public GardenMaterial[] Materials { get; private init; } = [];
    public IReadOnlyList<GardenMesh> Meshes { get; private init; } = [];
    public GardenInstance[] Instances { get; private init; } = [];
    public GardenLight[] Lights { get; private init; } = [];
    /// <summary>The Blender camera of each scene: where the hero shot stands and looks, and its vertical field of view.</summary>
    public (Vector3 Eye, Vector3 Target, float FovY) RasterCamera { get; private init; }
    public (Vector3 Eye, Vector3 Target, float FovY) RayCamera { get; private init; }
    /// <summary>The mountains round the horizon, when the scene has them.</summary>
    public GardenBackdrop? Backdrop { get; private init; }
    /// <summary>The height of the main pool's surface.</summary>
    public float WaterLevel { get; private init; }
    public GardenFountain? Fountain { get; private init; }
    /// <summary>A number that names this scene file (a hash of its bytes): what was worked out for one scene is not used with another.</summary>
    public ulong Stamp { get; private init; }
    /// <summary>The light bounced round the courtyard, for the Direct3D test - when it is embedded and was worked out for this very scene.</summary>
    public GardenLightVolume? Light { get; private set; }

    public long UniqueTriangles => Meshes.Sum(m => (long)m.Indices.Length / 3);
    public long Triangles(Mode mode) => Instances.Where(i => (i.Mask & (uint)mode) != 0).Sum(i => (long)Meshes[(int)i.Mesh].Indices.Length / 3);

    private static GardenScene? _embedded;
    /// <summary>The garden embedded in this assembly, read once.</summary>
    public static GardenScene Embedded
    {
        get
        {
            if (_embedded is not null) return _embedded;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene.garden.mzscene")
                ?? throw new InvalidOperationException("The garden scene is not embedded.");
            var scene = Read(s);
            using var light = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene.garden.light");
            if (light is not null && LightIn(light) is { } volume && volume.SceneStamp == scene.Stamp) scene.Light = volume;
            return _embedded = scene;
        }
    }
    /// <summary>A light file of an earlier build's making is as good as none: the garden is drawn without, until it is worked out again.</summary>
    private static GardenLightVolume? LightIn(Stream file) { try { return GardenLightVolume.Read(file); } catch (InvalidDataException) { return null; } }

    public static GardenScene Read(Stream compressed)
    {
        using var file = new MemoryStream(); compressed.CopyTo(file); file.Position = 0;
        ulong stamp = 1469598103934665603UL; foreach (byte b in file.GetBuffer().AsSpan(0, (int)file.Length)) stamp = (stamp ^ b) * 1099511628211UL;
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var ms = new MemoryStream(); gz.CopyTo(ms);
        var r = new Reader(ms.GetBuffer().AsMemory(0, (int)ms.Length));
        if (r.Bytes(4).Span is not [(byte)'M', (byte)'Z', (byte)'S', (byte)'C']) throw new InvalidDataException("Not a Mazesta scene file.");
        int version = r.I32(); if (version != Version) throw new InvalidDataException($"Scene file version {version}; this build reads {Version}.");
        int nTex = r.I32(), nMat = r.I32(), nMesh = r.I32(), nInst = r.I32(), nLight = r.I32(), size = r.I32();
        if (size < 4 || size > 4096 || !int.IsPow2(size)) throw new InvalidDataException($"Texture size {size}.");
        var stored = new List<(int Size, uint Flags, (uint Channels, ReadOnlyMemory<byte> Data)[] Parts)>(nTex);
        for (int t = 0; t < nTex; t++)
        {
            int own = r.I32(); uint flags = (uint)r.I32(); int parts = r.I32();
            if (own < 4 || own > size || !int.IsPow2(own) || parts is < 1 or > 4) throw new InvalidDataException($"A texture of size {own} in {parts} parts, in a scene of {size}.");
            var list = new (uint, ReadOnlyMemory<byte>)[parts];
            for (int k = 0; k < parts; k++) { uint channels = (uint)r.I32(); list[k] = (channels, r.Bytes(r.I32())); }
            stored.Add((own, flags, list));
        }
        var textures = new byte[nTex][][];
        Parallel.For(0, nTex, t => textures[t] = Bc3.MipChain(Texels(stored[t].Size, stored[t].Parts), stored[t].Size, cutout: (stored[t].Flags & 1) != 0, data: (stored[t].Flags & 2) != 0));
        var materials = MemoryMarshal.Cast<byte, GardenMaterial>(r.Bytes(nMat * Marshal.SizeOf<GardenMaterial>()).Span).ToArray();
        var meshes = new List<GardenMesh>(nMesh);
        for (int i = 0; i < nMesh; i++)
        {
            int vc = r.I32(), ic = r.I32(), sc = r.I32();
            var centre = new Vector3(r.F32(), r.F32(), r.F32()); var extent = new Vector3(r.F32(), r.F32(), r.F32());
            var subs = MemoryMarshal.Cast<byte, GardenSubmesh>(r.Bytes(sc * 12).Span).ToArray();
            var vertices = Records(r.Bytes(checked(vc * 16)).Span, 16);
            var indices = MemoryMarshal.Cast<byte, uint>(Records(r.Bytes(checked(ic * 4)).Span, 4)).ToArray();
            for (int k = 1; k < indices.Length; k++) indices[k] += indices[k - 1];
            meshes.Add(new GardenMesh(centre, extent, vertices, indices, subs));
        }
        var instances = MemoryMarshal.Cast<byte, GardenInstance>(Records(r.Bytes(checked(nInst * Marshal.SizeOf<GardenInstance>())).Span, Marshal.SizeOf<GardenInstance>())).ToArray();
        var lights = MemoryMarshal.Cast<byte, GardenLight>(r.Bytes(nLight * Marshal.SizeOf<GardenLight>()).Span).ToArray();
        (Vector3, Vector3, float) Camera() => (new(r.F32(), r.F32(), r.F32()), new(r.F32(), r.F32(), r.F32()), r.F32());
        var rasterCam = Camera(); var rayCam = Camera();
        int skyW = r.I32(), skyH = r.I32(); float skyLow = r.F32(), skyHigh = r.F32(); GardenBackdrop? backdrop = null;
        if (skyW != 0)
        {
            if (skyW < 4 || skyH < 4 || skyW > 16384 || skyH > 16384 || skyW % 4 != 0 || skyH % 4 != 0 || !(skyHigh > skyLow)) throw new InvalidDataException($"A backdrop of {skyW} x {skyH}.");
            backdrop = new GardenBackdrop(skyW, skyH, skyLow, skyHigh, r.Bytes(skyW / 4 * (skyH / 4) * 16).ToArray());
        }
        float waterLevel = r.F32(); var nozzle = new Vector3(r.F32(), r.F32(), r.F32()); float bowlRadius = r.F32(), bowlLevel = r.F32(), rimRadius = r.F32();
        var fountain = rimRadius > 0 ? new GardenFountain(nozzle, bowlRadius, bowlLevel, rimRadius) : null;
        if (fountain is not null && !(bowlLevel > waterLevel && nozzle.Y > bowlLevel && bowlRadius > 0 && rimRadius >= bowlRadius)) throw new InvalidDataException("The fountain's bowl is not over the pool and under its nozzle.");
        if (!r.AtEnd) throw new InvalidDataException("The scene file has trailing data.");
        foreach (var inst in instances) if (inst.Mesh >= meshes.Count) throw new InvalidDataException($"An instance refers to mesh {inst.Mesh} of {meshes.Count}.");
        foreach (var mesh in meshes)
        {
            uint vc = (uint)(mesh.Vertices.Length / 16);
            foreach (var sub in mesh.Submeshes)
                if (sub.Material >= materials.Length || sub.IndexStart + sub.IndexCount > mesh.Indices.Length) throw new InvalidDataException("A submesh is out of range.");
            foreach (uint ix in mesh.Indices) if (ix >= vc) throw new InvalidDataException("An index is out of range.");
        }
        foreach (var m in materials) if (m.Texture >= nTex || m.NormalTexture >= nTex) throw new InvalidDataException($"A material refers to texture {Math.Max(m.Texture, m.NormalTexture)} of {nTex}.");
        return new GardenScene { TextureSize = size, Textures = textures, Materials = materials, Meshes = meshes, Instances = instances, Lights = lights, RasterCamera = rasterCam, RayCamera = rayCam, Backdrop = backdrop, WaterLevel = waterLevel, Fountain = fountain, Stamp = stamp };
    }

    /// <summary>A texture's RGBA texels from its stored parts, bottom row first (as the exporter had them). Each part fills the
    /// channels its low four bits name; bits 8 and up say how it is stored: 0 one value, 1 a JPEG picture, 2 one bit a texel (set: 255).</summary>
    private static byte[] Texels(int size, (uint Channels, ReadOnlyMemory<byte> Data)[] parts)
    {
        var rgba = new byte[size * size * 4];
        foreach (var (channels, data) in parts)
        {
            var d = data.Span; uint kind = channels >> 8;
            if (kind > 2 || (channels & 0xFF) is 0 or > 15) throw new InvalidDataException("A texture part of a kind this build does not read.");
            if (kind == 0 && d.Length != 1 || kind == 2 && d.Length != size * size / 8) throw new InvalidDataException("A texture part of the wrong length.");
            byte[]? picture = null;
            if (kind == 1) { picture = Jpeg.Decode(d, out int w, out int h); if (w != size || h != size) throw new InvalidDataException($"A picture of {w} x {h} in a texture of {size}."); }
            bool colour = (channels & 7) == 7;
            for (int y = 0; y < size; y++)
                for (int x = 0, from = (size - 1 - y) * size; x < size; x++, from++)   // the stored rows run top to bottom
                    for (int c = 0; c < 4; c++)
                        if ((channels >> c & 1) != 0)
                            rgba[(y * size + x) * 4 + c] = kind switch { 0 => d[0], 1 => picture![from * 3 + (colour ? c : 0)], _ => (byte)((d[from >> 3] >> (7 - (from & 7)) & 1) * 255) };
        }
        return rgba;
    }

    /// <summary>Records of <paramref name="stride"/> bytes back from their byte planes.</summary>
    private static byte[] Records(ReadOnlySpan<byte> planes, int stride)
    {
        int n = planes.Length / stride; var records = new byte[planes.Length];
        for (int p = 0; p < stride; p++) { var plane = planes.Slice(p * n, n); for (int i = 0; i < n; i++) records[i * stride + p] = plane[i]; }
        return records;
    }

    /// <summary>A BC3 image of <paramref name="blocks"/> x blocks 4x4 blocks at twice the size, each texel doubled: every block becomes four
    /// that keep its end colours and alphas and repeat a quarter of its picks, so nothing is decoded or lost.</summary>
    internal static byte[] Bc3Doubled(byte[] source, int blocks)
    {
        var doubled = new byte[source.Length * 4];
        for (int by = 0; by < blocks * 2; by++)
            for (int bx = 0; bx < blocks * 2; bx++)
            {
                var from = source.AsSpan((by / 2 * blocks + bx / 2) * 16, 16); var to = doubled.AsSpan((by * blocks * 2 + bx) * 16, 16);
                from[..2].CopyTo(to); from.Slice(8, 4).CopyTo(to[8..]);
                ulong alphaFrom = BitConverter.ToUInt64(from) >> 16, alphaTo = 0; uint colorFrom = BitConverter.ToUInt32(from[12..]), colorTo = 0;
                for (int k = 0; k < 16; k++)
                {
                    int texel = (by % 2 * 2 + k / 4 / 2) * 4 + bx % 2 * 2 + k % 4 / 2;   // the source texel under this one
                    alphaTo |= (alphaFrom >> texel * 3 & 7) << k * 3; colorTo |= (colorFrom >> texel * 2 & 3) << k * 2;
                }
                for (int k = 0; k < 6; k++) to[2 + k] = (byte)(alphaTo >> k * 8);
                BitConverter.TryWriteBytes(to[12..], colorTo);
            }
        return doubled;
    }

    private sealed class Reader(ReadOnlyMemory<byte> data)
    {
        private int _at;
        public bool AtEnd => _at == data.Length;
        public ReadOnlyMemory<byte> Bytes(int n)
        {
            if (n < 0 || _at + n > data.Length) throw new InvalidDataException("The scene file is cut short.");
            var m = data.Slice(_at, n); _at += n; return m;
        }
        public int I32() => BitConverter.ToInt32(Bytes(4).Span);
        public float F32() => BitConverter.ToSingle(Bytes(4).Span);
    }
}

/// <summary>Smoke: a puff of steam, thickest through its middle (the renderers' own; no scene file has it).</summary>
public enum GardenMaterialKind : uint { Flat, Cutout, Brick, Water, Glass, Emissive, Smoke }

/// <summary>A material as the shaders read it (six float4s): the metal-roughness model of Blender's Principled shader. Colours are linear.
/// <see cref="Texture"/> holds the base colour and, in alpha, a leaf card's opacity or any other surface's roughness;
/// <see cref="NormalTexture"/> (-1: none) a tangent-space normal map, x in alpha and y in green, with the material's ambient occlusion in
/// red and its metalness in blue. <see cref="Pattern"/>: for a brick (Blender's brick texture, laid on each face from world coordinates as
/// the .blend's own shader does) its scale, mortar size, brick width and row height; for any other textured surface how many times the
/// texture repeats across the mesh's coordinates (x, y) and, when z is not 0, that it is laid by world position instead, z repeats a metre;
/// for a flat surface without a texture, in w, how strongly the shaders mottle it (0: as they see fit).
/// <see cref="LightTint"/> 1: light passing through takes the material's colour (the orsi's stained panes, in the ray tracer).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GardenMaterial
{
    public GardenMaterialKind Kind; public int Texture; public float LightTint; public int NormalTexture;
    public Vector3 Base; public float Alpha;
    public Vector3 Color2; public float Roughness;
    public Vector3 Mortar; public float Metallic;
    public Vector3 Emission; public float Transmission;
    public Vector4 Pattern;
}

/// <summary>A run of a mesh's indices drawn with one material.</summary>
[StructLayout(LayoutKind.Sequential)] public readonly record struct GardenSubmesh(uint IndexStart, uint IndexCount, uint Material);

/// <summary>One mesh: vertices of 16 bytes (position as snorm16x4 within ±<see cref="Extent"/> of <see cref="Centre"/>, normal snorm8x4,
/// uv float16x2) - the layout the vertex buffer and the ray tracer read directly - and 32-bit indices.</summary>
public sealed record GardenMesh(Vector3 Centre, Vector3 Extent, byte[] Vertices, uint[] Indices, GardenSubmesh[] Submeshes)
{
    public int VertexCount => Vertices.Length / 16;
    /// <summary>Vertex <paramref name="i"/>'s position in the mesh's own space.</summary>
    public Vector3 Position(int i)
    {
        var v = MemoryMarshal.Cast<byte, short>(Vertices.AsSpan(i * 16, 6));
        return Centre + Extent * new Vector3(v[0], v[1], v[2]) / 32767f;
    }
}

/// <summary>A placed mesh: which scene(s) show it, flags (<see cref="GardenScene.LogoFlag"/>) and its world transform as three rows of a 3x4 matrix.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GardenInstance
{
    public uint Mesh, Mask, Flags;
    public Vector4 Row0, Row1, Row2;
    public readonly Vector3 Apply(Vector3 p) => new(Row0.X * p.X + Row0.Y * p.Y + Row0.Z * p.Z + Row0.W, Row1.X * p.X + Row1.Y * p.Y + Row1.Z * p.Z + Row1.W, Row2.X * p.X + Row2.Y * p.Y + Row2.Z * p.Z + Row2.W);
}

/// <summary>The fountain in the pool: the top of its nozzle, the radius and level of the water in its bowl, the radius of the bowl's rim.
/// The renderers draw its jet from these (<see cref="GardenGpu.Droplet"/>).</summary>
public sealed record GardenFountain(Vector3 Nozzle, float BowlRadius, float BowlLevel, float RimRadius);

/// <summary>What stands round the horizon behind the courtyard: one BC3 (sRGB) image, <see cref="Width"/> texels all the way round
/// (a direction's column is atan2(x, z) / 2 pi + 1/2), its rows the tangent of the height above the horizon from <see cref="TanLow"/>
/// (first row) to <see cref="TanHigh"/>. Above it the sky is the shaders' own.</summary>
public sealed record GardenBackdrop(int Width, int Height, float TanLow, float TanHigh, byte[] Bc3);

public enum GardenLightKind : uint { Sun, Point, Spot }

/// <summary>A light as Blender had it: position, aim, colour, power in watts (for a sun, its strength), spot cone and blend, radius, a sun's angle.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GardenLight
{
    public GardenLightKind Kind; public uint Mask;
    public Vector3 Position, Direction, Color;
    public float Energy, Cone, Blend, Radius, AngleDegrees;
}
