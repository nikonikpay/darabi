using System.IO.Compression; using System.Numerics; using System.Reflection; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The Persian garden both visual GPU tests draw: a walled courtyard with a pool, a columned hall, cypresses, flower beds, lanterns and the
/// Mazesta logo floating over the water. Built in Blender (Mazesta-Art/persian-garden.blend) and written by tools/scene/export_garden.py
/// into garden.mzscene, embedded in this assembly; this reads it. The file holds both of the .blend's scenes: what only the Direct3D test
/// shows (the golden-hour sun) or only the ray-traced one (its blue-hour light rig, glass orbs, a mirror sphere) is marked with <see cref="Mode"/>.
/// <para>Format (gzip, little-endian): "MZSC", version, then counts of textures, materials, meshes, instances, lights and the texture size;
/// the textures' BC3 mip chains; the materials (<see cref="GardenMaterial"/>, 96 bytes); each mesh (vertex and index counts, submeshes,
/// quantisation centre and extent, 16-byte vertices, 32-bit indices); the instances (mesh, scene mask, flags, 3x4 world rows);
/// the lights; and one camera per scene. Axes are Direct3D's: y up, z forward.</para>
/// </summary>
public sealed class GardenScene
{
    public const int Version = 1, TextureMips = 7;
    [Flags] public enum Mode : uint { Raster = 1, RayTraced = 2 }
    public const uint LogoFlag = 1;

    public int TextureSize { get; private init; }
    /// <summary>Each texture's BC3 mip chain (TextureSize down to 4), largest first.</summary>
    public IReadOnlyList<byte[][]> Textures { get; private init; } = [];
    public GardenMaterial[] Materials { get; private init; } = [];
    public IReadOnlyList<GardenMesh> Meshes { get; private init; } = [];
    public GardenInstance[] Instances { get; private init; } = [];
    public GardenLight[] Lights { get; private init; } = [];
    /// <summary>The Blender camera of each scene: where the hero shot stands and looks, and its vertical field of view.</summary>
    public (Vector3 Eye, Vector3 Target, float FovY) RasterCamera { get; private init; }
    public (Vector3 Eye, Vector3 Target, float FovY) RayCamera { get; private init; }

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
            return _embedded = Read(s);
        }
    }

    public static GardenScene Read(Stream compressed)
    {
        using var gz = new GZipStream(compressed, CompressionMode.Decompress);
        using var ms = new MemoryStream(); gz.CopyTo(ms);
        var r = new Reader(ms.GetBuffer().AsMemory(0, (int)ms.Length));
        if (r.Bytes(4).Span is not [(byte)'M', (byte)'Z', (byte)'S', (byte)'C']) throw new InvalidDataException("Not a Mazesta scene file.");
        int version = r.I32(); if (version != Version) throw new InvalidDataException($"Scene file version {version}; this build reads {Version}.");
        int nTex = r.I32(), nMat = r.I32(), nMesh = r.I32(), nInst = r.I32(), nLight = r.I32(), size = r.I32();
        var textures = new List<byte[][]>(nTex);
        for (int t = 0; t < nTex; t++)
        {
            var mips = new byte[TextureMips][];
            for (int m = 0, s = size; m < TextureMips; m++, s /= 2) mips[m] = r.Bytes(Math.Max(1, s / 4) * Math.Max(1, s / 4) * 16).ToArray();
            textures.Add(mips);
        }
        var materials = MemoryMarshal.Cast<byte, GardenMaterial>(r.Bytes(nMat * Marshal.SizeOf<GardenMaterial>()).Span).ToArray();
        var meshes = new List<GardenMesh>(nMesh);
        for (int i = 0; i < nMesh; i++)
        {
            int vc = r.I32(), ic = r.I32(), sc = r.I32();
            var centre = new Vector3(r.F32(), r.F32(), r.F32()); var extent = new Vector3(r.F32(), r.F32(), r.F32());
            var subs = MemoryMarshal.Cast<byte, GardenSubmesh>(r.Bytes(sc * 12).Span).ToArray();
            var vertices = r.Bytes(vc * 16).ToArray();
            var indices = MemoryMarshal.Cast<byte, uint>(r.Bytes(ic * 4).Span).ToArray();
            meshes.Add(new GardenMesh(centre, extent, vertices, indices, subs));
        }
        var instances = MemoryMarshal.Cast<byte, GardenInstance>(r.Bytes(nInst * Marshal.SizeOf<GardenInstance>()).Span).ToArray();
        var lights = MemoryMarshal.Cast<byte, GardenLight>(r.Bytes(nLight * Marshal.SizeOf<GardenLight>()).Span).ToArray();
        (Vector3, Vector3, float) Camera() => (new(r.F32(), r.F32(), r.F32()), new(r.F32(), r.F32(), r.F32()), r.F32());
        var rasterCam = Camera(); var rayCam = Camera();
        if (!r.AtEnd) throw new InvalidDataException("The scene file has trailing data.");
        foreach (var inst in instances) if (inst.Mesh >= meshes.Count) throw new InvalidDataException($"An instance refers to mesh {inst.Mesh} of {meshes.Count}.");
        foreach (var mesh in meshes)
        {
            uint vc = (uint)(mesh.Vertices.Length / 16);
            foreach (var sub in mesh.Submeshes)
                if (sub.Material >= materials.Length || sub.IndexStart + sub.IndexCount > mesh.Indices.Length) throw new InvalidDataException("A submesh is out of range.");
            foreach (uint ix in mesh.Indices) if (ix >= vc) throw new InvalidDataException("An index is out of range.");
        }
        foreach (var m in materials) if (m.Texture >= nTex) throw new InvalidDataException($"A material refers to texture {m.Texture} of {nTex}.");
        return new GardenScene { TextureSize = size, Textures = textures, Materials = materials, Meshes = meshes, Instances = instances, Lights = lights, RasterCamera = rasterCam, RayCamera = rayCam };
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

public enum GardenMaterialKind : uint { Flat, Cutout, Brick, Water, Glass, Emissive }

/// <summary>A material as the shaders read it (six float4s). Colours are linear. Brick is Blender's brick texture, laid on each face from
/// world coordinates as the .blend's own shader does: <see cref="Pattern"/> is its scale, mortar size, brick width and row height.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GardenMaterial
{
    public GardenMaterialKind Kind; public int Texture; public float Pad0, Pad1;
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

public enum GardenLightKind : uint { Sun, Point, Spot }

/// <summary>A light as Blender had it: position, aim, colour, power in watts (for a sun, its strength), spot cone and blend, radius, a sun's angle.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GardenLight
{
    public GardenLightKind Kind; public uint Mask;
    public Vector3 Position, Direction, Color;
    public float Energy, Cone, Blend, Radius, AngleDegrees;
}
