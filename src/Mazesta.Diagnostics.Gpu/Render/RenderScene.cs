using System.Numerics; using System.Runtime.CompilerServices; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Render;

/// <summary>What a ray met: how far along it, which placed mesh and which of its triangles, and where on the triangle.</summary>
internal struct RayHit { public float T, U, V; public int Instance, Prim; }

/// <summary>
/// The Persian garden as a renderer on the processor sees it, and the path tracer that draws it: the way V-Ray, Corona and Cycles work on a
/// CPU, in Mazesta's own code (no library). The scene is the Direct3D test's day scene as the file has it (<see cref="GardenScene"/>): every
/// mesh keeps one tree over its own triangles and the scene one over the placed meshes, so a tree that stands forty times in the garden is
/// held once (a ray is carried into the mesh's own space). A path leaves the camera, and at each surface asks the sun directly (a shadow
/// ray, through the cut-out leaves) and goes on in a direction the surface would scatter light from - up to <see cref="MaxDepth"/> surfaces,
/// ended early by chance where little light is left (Russian roulette). Water and glass mirror and let through by Fresnel's share, metal mirrors.
/// <para>Left simple on purpose, and said so where the result is shown: the hall's lamps are not asked for light (only what glows is seen),
/// stained glass does not colour the sun, there are no normal maps, the fountain's water and the weather are not there. What is measured is
/// the processor's speed at this work, not the picture's beauty.</para>
/// </summary>
internal sealed class RenderScene
{
    public const int MaxDepth = 6;
    private const float TMin = 0.002f;
    private const byte AlphaTest = 1, PassesLight = 2;

    [StructLayout(LayoutKind.Sequential)] private struct Tri { public Vector3 V0, E1, E2; }
    private struct Placed { public int Mesh; public Vector3 I0, I1, I2, At; }
    private sealed class Mesh { public BvhNode[] Nodes = []; public Tri[] Tris = []; public int[] Prim = []; public byte[] Flags = []; public ushort[] Material = []; public Vector3[] Normals = []; public Vector2[] Uvs = []; public uint[] Indices = []; }
    private sealed class Texture(int size, byte[] rgba) { public readonly int Size = size; public readonly byte[] Rgba = rgba; }

    private readonly Mesh?[] _meshes; private readonly Placed[] _placed; private readonly BvhNode[] _top;
    private readonly GardenMaterial[] _materials; private readonly byte[] _materialFlags; private readonly Texture?[] _textures;
    private readonly Vector3 _sun, _sunColor, _zenith, _horizon, _ground;
    private readonly Vector3 _eye, _forward, _right, _up; private readonly float _tanHalf;
    private static readonly float[] Linear = [.. Enumerable.Range(0, 256).Select(i => { float x = i / 255f; return x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f); })];

    /// <summary>Triangles a ray can meet (every placed mesh's), and those held in memory (each mesh's once).</summary>
    public long Triangles { get; } public long UniqueTriangles { get; }
    /// <summary>What the picture's light is multiplied by before it is shown (the garden's own exposure by day).</summary>
    public float Exposure { get; }

    public RenderScene(GardenScene scene)
    {
        _materials = scene.Materials;
        _materialFlags = [.. _materials.Select(m => (byte)((m.Kind == GardenMaterialKind.Cutout && m.Texture >= 0 ? AlphaTest : 0)
            | (m.Kind is GardenMaterialKind.Water or GardenMaterialKind.Glass or GardenMaterialKind.Emissive or GardenMaterialKind.Smoke ? PassesLight : 0)))];
        _textures = new Texture?[scene.Textures.Count];
        foreach (int t in _materials.Where(m => m.Texture >= 0).Select(m => m.Texture).Distinct()) _textures[t] = Decode(scene.Textures[t]);

        var shown = scene.Instances.Where(i => (i.Mask & (uint)GardenScene.Mode.Raster) != 0 && scene.Meshes[(int)i.Mesh].Indices.Length >= 3).ToArray();
        _meshes = new Mesh?[scene.Meshes.Count];
        Parallel.ForEach(shown.Select(i => (int)i.Mesh).Distinct(), m => _meshes[m] = Build(scene.Meshes[m]));

        var placed = new List<Placed>(); var min = new List<Vector3>(); var max = new List<Vector3>();
        foreach (var i in shown)
        {
            Vector3 c0 = new(i.Row0.X, i.Row1.X, i.Row2.X), c1 = new(i.Row0.Y, i.Row1.Y, i.Row2.Y), c2 = new(i.Row0.Z, i.Row1.Z, i.Row2.Z);
            float det = Vector3.Dot(c0, Vector3.Cross(c1, c2)); if (MathF.Abs(det) < 1e-20f) continue;   // squashed flat: nothing of it can be met
            var root = _meshes[i.Mesh]!.Nodes[0]; Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int k = 0; k < 8; k++) { var p = i.Apply(new((k & 1) == 0 ? root.Min.X : root.Max.X, (k & 2) == 0 ? root.Min.Y : root.Max.Y, (k & 4) == 0 ? root.Min.Z : root.Max.Z)); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            placed.Add(new Placed { Mesh = (int)i.Mesh, I0 = Vector3.Cross(c1, c2) / det, I1 = Vector3.Cross(c2, c0) / det, I2 = Vector3.Cross(c0, c1) / det, At = new(i.Row0.W, i.Row1.W, i.Row2.W) });
            min.Add(lo); max.Add(hi); Triangles += _meshes[i.Mesh]!.Tris.Length;
        }
        (_top, var order) = Bvh.Build([.. min], [.. max], 1);
        _placed = [.. order.Select(k => placed[k])];
        UniqueTriangles = _meshes.Sum(m => (long)(m?.Tris.Length ?? 0));

        // the light of a morning of the garden's own day (GardenDay: the sun as high as it gets), under the scene's own sun
        var own = scene.Lights.First(l => l.Kind == GardenLightKind.Sun && (l.Mask & (uint)GardenScene.Mode.Raster) != 0);
        var day = GardenDay.At(GardenDay.Sunrise + 0.25f * (GardenDay.Sunset - GardenDay.Sunrise), Vector3.Normalize(-own.Direction), own.Color * own.Energy, Vector3.UnitY, Vector3.Zero);
        _sun = day.Key; _sunColor = day.KeyColor; _zenith = day.Zenith; _horizon = day.Horizon; _ground = day.Ground; Exposure = day.Exposure;

        var cam = scene.RasterCamera;
        _eye = cam.Eye; _forward = Vector3.Normalize(cam.Target - cam.Eye); _right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, _forward)); _up = Vector3.Cross(_forward, _right);
        _tanHalf = MathF.Tan(cam.FovY / 2);
    }

    private Mesh Build(GardenMesh source)
    {
        int vc = source.VertexCount, n = source.Indices.Length / 3;
        var positions = new Vector3[vc]; var normals = new Vector3[vc]; var uvs = new Vector2[vc];
        for (int i = 0; i < vc; i++)
        {
            positions[i] = source.Position(i); var v = source.Vertices.AsSpan(i * 16, 16);
            normals[i] = Vector3.Normalize(Vector3.Max(new Vector3((sbyte)v[8], (sbyte)v[9], (sbyte)v[10]) / 127f, new(-1)) + new Vector3(1e-6f));
            uvs[i] = new((float)BitConverter.ToHalf(v[12..]), (float)BitConverter.ToHalf(v[14..]));
        }
        var material = new ushort[n];
        foreach (var s in source.Submeshes) for (uint p = s.IndexStart / 3, end = (s.IndexStart + s.IndexCount) / 3; p < end; p++) material[p] = (ushort)s.Material;
        var min = new Vector3[n]; var max = new Vector3[n];
        for (int p = 0; p < n; p++)
        {
            Vector3 a = positions[source.Indices[p * 3]], b = positions[source.Indices[p * 3 + 1]], c = positions[source.Indices[p * 3 + 2]];
            min[p] = Vector3.Min(a, Vector3.Min(b, c)); max[p] = Vector3.Max(a, Vector3.Max(b, c));
        }
        var (nodes, order) = Bvh.Build(min, max, 4);
        var tris = new Tri[n]; var flags = new byte[n];
        for (int k = 0; k < n; k++)
        {
            int p = order[k]; Vector3 a = positions[source.Indices[p * 3]];
            tris[k] = new Tri { V0 = a, E1 = positions[source.Indices[p * 3 + 1]] - a, E2 = positions[source.Indices[p * 3 + 2]] - a }; flags[k] = _materialFlags[material[p]];
        }
        return new Mesh { Nodes = nodes, Tris = tris, Prim = order, Flags = flags, Material = material, Normals = normals, Uvs = uvs, Indices = source.Indices };
    }

    /// <summary>A texture's picture from its BC3 blocks, at 512 texels across or its own size if that is less: four bytes a texel, the bottom row first.</summary>
    private static Texture Decode(byte[][] chain)
    {
        int own = 4 << (chain.Length - 1), level = Math.Max(0, int.Log2(own) - 9), size = own >> level, blocks = size / 4;
        var bc = chain[level]; var rgba = new byte[size * size * 4];
        Span<int> alpha = stackalloc int[8]; Span<int> colour = stackalloc int[12];
        for (int by = 0; by < blocks; by++)
            for (int bx = 0; bx < blocks; bx++)
            {
                var b = bc.AsSpan((by * blocks + bx) * 16, 16);
                alpha[0] = b[0]; alpha[1] = b[1]; for (int p = 2; p < 8; p++) alpha[p] = ((8 - p) * alpha[0] + (p - 1) * alpha[1]) / 7;
                ulong steps = BitConverter.ToUInt64(b) >> 16; uint picks = BitConverter.ToUInt32(b[12..]);
                int c0 = b[8] | b[9] << 8, c1 = b[10] | b[11] << 8;
                colour[0] = (c0 >> 11 & 31) * 255 / 31; colour[1] = (c0 >> 5 & 63) * 255 / 63; colour[2] = (c0 & 31) * 255 / 31;
                colour[3] = (c1 >> 11 & 31) * 255 / 31; colour[4] = (c1 >> 5 & 63) * 255 / 63; colour[5] = (c1 & 31) * 255 / 31;
                for (int c = 0; c < 3; c++) { colour[6 + c] = (2 * colour[c] + colour[3 + c]) / 3; colour[9 + c] = (colour[c] + 2 * colour[3 + c]) / 3; }
                for (int k = 0; k < 16; k++)
                {
                    int o = ((by * 4 + k / 4) * size + bx * 4 + k % 4) * 4, pick = (int)(picks >> k * 2 & 3) * 3;
                    rgba[o] = (byte)colour[pick]; rgba[o + 1] = (byte)colour[pick + 1]; rgba[o + 2] = (byte)colour[pick + 2]; rgba[o + 3] = (byte)alpha[(int)(steps >> k * 3 & 7)];
                }
            }
        return new Texture(size, rgba);
    }

    // ——— rays ———

    /// <summary>The nearest thing a ray meets within <paramref name="tMax"/>; or, for a shadow ray, whether anything that stops light is in the way at all.</summary>
    public bool Trace(Vector3 origin, Vector3 dir, float tMax, bool shadow, out RayHit hit)
    {
        hit = new RayHit { T = tMax, Instance = -1 };
        Vector3 inv = Bvh.Inverse(dir); Span<int> stack = stackalloc int[64]; int sp = 0, at = 0;
        if (!Bvh.Enters(_top[0], origin, inv, hit.T, out _)) return false;
        while (true)
        {
            ref readonly var node = ref _top[at];
            if (node.Count > 0)
            {
                for (int i = node.First, end = i + node.Count; i < end; i++)
                {
                    ref readonly var p = ref _placed[i]; var o = origin - p.At;
                    if (Intersect(_meshes[p.Mesh]!, new(Vector3.Dot(p.I0, o), Vector3.Dot(p.I1, o), Vector3.Dot(p.I2, o)), new(Vector3.Dot(p.I0, dir), Vector3.Dot(p.I1, dir), Vector3.Dot(p.I2, dir)), ref hit, shadow))
                    { hit.Instance = i; if (shadow) return true; }
                }
            }
            else
            {
                int a = node.First, b = a + 1;
                bool ha = Bvh.Enters(_top[a], origin, inv, hit.T, out float na), hb = Bvh.Enters(_top[b], origin, inv, hit.T, out float nb);
                if (ha && hb) { if (na > nb) (a, b) = (b, a); stack[sp++] = b; at = a; continue; }
                if (ha) { at = a; continue; }
                if (hb) { at = b; continue; }
            }
            if (sp == 0) break;
            at = stack[--sp];
        }
        return hit.Instance >= 0;
    }

    /// <summary>A ray in a mesh's own space against its triangles (Möller and Trumbore's test): true when it met one nearer than <paramref name="hit"/> had.</summary>
    private bool Intersect(Mesh m, Vector3 origin, Vector3 dir, ref RayHit hit, bool shadow)
    {
        var nodes = m.Nodes; Vector3 inv = Bvh.Inverse(dir); Span<int> stack = stackalloc int[64]; int sp = 0, at = 0; bool found = false;
        if (!Bvh.Enters(nodes[0], origin, inv, hit.T, out _)) return false;
        while (true)
        {
            ref readonly var node = ref nodes[at];
            if (node.Count > 0)
            {
                for (int i = node.First, end = i + node.Count; i < end; i++)
                {
                    ref readonly var t = ref m.Tris[i];
                    var p = Vector3.Cross(dir, t.E2); float det = Vector3.Dot(t.E1, p); if (det == 0) continue;
                    float invDet = 1 / det; var s = origin - t.V0;
                    float u = Vector3.Dot(s, p) * invDet; if (!(u >= 0 && u <= 1)) continue;
                    var q = Vector3.Cross(s, t.E1); float v = Vector3.Dot(dir, q) * invDet; if (!(v >= 0 && u + v <= 1)) continue;
                    float dist = Vector3.Dot(t.E2, q) * invDet; if (!(dist > TMin && dist < hit.T)) continue;
                    byte flags = m.Flags[i];
                    if (shadow && (flags & PassesLight) != 0) continue;
                    if ((flags & AlphaTest) != 0 && !Opaque(m, m.Prim[i], u, v)) continue;
                    hit.T = dist; hit.U = u; hit.V = v; hit.Prim = m.Prim[i]; found = true;
                    if (shadow) return true;
                }
            }
            else
            {
                int a = node.First, b = a + 1;
                bool ha = Bvh.Enters(nodes[a], origin, inv, hit.T, out float na), hb = Bvh.Enters(nodes[b], origin, inv, hit.T, out float nb);
                if (ha && hb) { if (na > nb) (a, b) = (b, a); stack[sp++] = b; at = a; continue; }
                if (ha) { at = a; continue; }
                if (hb) { at = b; continue; }
            }
            if (sp == 0) break;
            at = stack[--sp];
        }
        return found;
    }

    /// <summary>A leaf card is there only where its picture is: the cut-out test, during the ray's search.</summary>
    private bool Opaque(Mesh m, int prim, float u, float v)
    {
        ref readonly var mat = ref _materials[m.Material[prim]];
        var uv = Uv(m, prim, u, v); if (mat.Pattern.X > 0) uv *= new Vector2(mat.Pattern.X, mat.Pattern.Y);
        return Texel(_textures[mat.Texture]!, uv).W * mat.Alpha >= 0.5f;
    }

    private static Vector2 Uv(Mesh m, int prim, float u, float v)
    {
        Vector2 a = m.Uvs[m.Indices[prim * 3]], b = m.Uvs[m.Indices[prim * 3 + 1]], c = m.Uvs[m.Indices[prim * 3 + 2]];
        return a + (b - a) * u + (c - a) * v;
    }

    /// <summary>The texel at a place of a picture that repeats: colour as light (linear), alpha 0 to 1.</summary>
    private static Vector4 Texel(Texture t, Vector2 uv)
    {
        int x = (int)MathF.Floor(uv.X * t.Size) & (t.Size - 1), y = (int)MathF.Floor(uv.Y * t.Size) & (t.Size - 1), o = (y * t.Size + x) * 4;
        return new(Linear[t.Rgba[o]], Linear[t.Rgba[o + 1]], Linear[t.Rgba[o + 2]], t.Rgba[o + 3] / 255f);
    }

    // ——— light ———

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Rand(ref uint seed) { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed >> 8) * (1f / 16777216f); }
    /// <summary>The start of a pixel's sample's own run of chances: the same pixel and pass draw the same path on every machine and thread.</summary>
    public static uint Seed(int pixel, int pass) { uint h = (uint)pixel * 0x9E3779B1u ^ (uint)pass * 0x85EBCA77u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15; return h | 1; }

    private static Vector3 OnSphere(ref uint seed)
    {
        float z = Rand(ref seed) * 2 - 1, a = Rand(ref seed) * 2 * MathF.PI, r = MathF.Sqrt(MathF.Max(0, 1 - z * z));
        return new(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    private static Vector2 FaceCoords(Vector3 p, Vector3 n)
    {
        var a = Vector3.Abs(n);
        return a.Y > 0.6f ? new(p.X, p.Z) : a.X > 0.6f ? new(p.Z, p.Y) : new(p.X, p.Y);
    }

    private Vector3 Sky(Vector3 dir, bool seen)
    {
        if (dir.Y < 0) return _ground;
        float h = 1 - dir.Y; var c = Vector3.Lerp(_zenith, _horizon, h * h * h);
        if (!seen) return c;   // a scattered path was already given the sun's light directly
        float toSun = MathF.Max(0, Vector3.Dot(dir, _sun)), disc = Math.Clamp((toSun - 0.99988f) / 0.00008f, 0, 1);
        return c + _sunColor * (0.05f * MathF.Pow(toSun, 6) + 0.16f * MathF.Pow(toSun, 64) + 2 * disc * disc * (3 - 2 * disc));
    }

    /// <summary>The light that comes to the camera along one path through pixel (<paramref name="x"/>, <paramref name="y"/>) of a picture of
    /// <paramref name="width"/> by <paramref name="height"/>; <paramref name="rays"/> counts every ray it sent.</summary>
    public Vector3 Sample(int x, int y, int width, int height, ref uint seed, ref long rays)
    {
        float sx = ((x + Rand(ref seed)) / width * 2 - 1) * _tanHalf * width / height, sy = (1 - (y + Rand(ref seed)) / height * 2) * _tanHalf;
        Vector3 origin = _eye, dir = Vector3.Normalize(_forward + _right * sx + _up * sy), light = Vector3.Zero, through = Vector3.One; bool seen = true;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            rays++;
            if (!Trace(origin, dir, float.MaxValue, false, out var hit)) { light += through * Sky(dir, seen); break; }
            ref readonly var placed = ref _placed[hit.Instance]; var mesh = _meshes[placed.Mesh]!;
            ref readonly var m = ref _materials[mesh.Material[hit.Prim]];
            var p = origin + dir * hit.T;
            Vector3 na = mesh.Normals[mesh.Indices[hit.Prim * 3]], nb = mesh.Normals[mesh.Indices[hit.Prim * 3 + 1]], nc = mesh.Normals[mesh.Indices[hit.Prim * 3 + 2]], nl = na + (nb - na) * hit.U + (nc - na) * hit.V;
            var n = Vector3.Normalize(placed.I0 * nl.X + placed.I1 * nl.Y + placed.I2 * nl.Z); if (Vector3.Dot(n, dir) > 0) n = -n;

            Vector3 albedo = m.Base; float roughness = m.Roughness;
            if (m.Texture >= 0)
            {
                var uv = m.Kind <= GardenMaterialKind.Cutout && m.Pattern.Z > 0 ? FaceCoords(p, n) * m.Pattern.Z : Uv(mesh, hit.Prim, hit.U, hit.V) * (m.Kind <= GardenMaterialKind.Cutout && m.Pattern.X > 0 ? new Vector2(m.Pattern.X, m.Pattern.Y) : Vector2.One);
                var texel = Texel(_textures[m.Texture]!, uv); albedo *= new Vector3(texel.X, texel.Y, texel.Z);
                if (m.Kind != GardenMaterialKind.Cutout) roughness = texel.W;
            }
            else if (m.Kind == GardenMaterialKind.Brick && m.Pattern.Z > 0 && m.Pattern.W > 0)
            {
                // ponytail: each brick one of the two colours by its place, no mortar lines; the shaders' Brick() if the wall should look as in the Direct3D test
                var f = FaceCoords(p, n) * m.Pattern.X; float row = MathF.Floor(f.Y / m.Pattern.W), col = MathF.Floor((f.X + ((int)row & 1) * 0.5f * m.Pattern.Z) / m.Pattern.Z);
                uint cell = Seed((int)row * 7919 + (int)col, 3); albedo = Vector3.Lerp(m.Base, m.Color2, Rand(ref cell));
            }
            light += through * m.Emission;

            if (m.Kind is GardenMaterialKind.Water or GardenMaterialKind.Glass)
            {
                float cos = MathF.Min(1, -Vector3.Dot(dir, n)), f0 = m.Kind == GardenMaterialKind.Water ? 0.02f : 0.04f, k = 1 - cos, fresnel = f0 + (1 - f0) * k * k * k * k * k;
                if (Rand(ref seed) < fresnel) dir = Vector3.Reflect(dir, n);
                else if (m.Kind == GardenMaterialKind.Water) { dir = Refract(dir, n, 1 / 1.33f); through *= Vector3.Lerp(Vector3.One, albedo, 0.5f); }
                else through *= m.LightTint > 0 ? albedo / MathF.Max(MathF.Max(albedo.X, albedo.Y), MathF.Max(albedo.Z, 1e-3f)) * 0.9f : Vector3.Lerp(Vector3.One, albedo, 0.3f);   // a pane is thin: the path goes straight on, coloured
                origin = p; seen = true; continue;
            }
            if (m.Metallic > 0.5f)
            {
                dir = Vector3.Normalize(Vector3.Reflect(dir, n) + OnSphere(ref seed) * (roughness * roughness));
                if (Vector3.Dot(dir, n) <= 0) break;
                through *= albedo; origin = p; seen = true; continue;
            }

            // the sun, asked directly: a point of its disc (half a degree across), through whatever leaves are in the way
            var toSun = Vector3.Normalize(_sun + OnSphere(ref seed) * 0.0045f); float facing = Vector3.Dot(n, toSun);
            if (facing > 0) { rays++; if (!Trace(p, toSun, float.MaxValue, true, out _)) light += through * albedo * _sunColor * (facing / MathF.PI); }

            dir = Vector3.Normalize(n + OnSphere(ref seed) * 0.999f); through *= albedo; origin = p; seen = false;
            if (depth >= 2)
            {
                float keep = Math.Clamp(MathF.Max(through.X, MathF.Max(through.Y, through.Z)), 0.05f, 0.95f);
                if (Rand(ref seed) > keep) break;
                through /= keep;
            }
        }
        // one path that happened on a lamp must not leave a white speck for the rest of the run: its light is capped, as production renderers do
        light = Vector3.Min(light, new(30));
        return float.IsFinite(light.X + light.Y + light.Z) ? light : Vector3.Zero;
    }

    private static Vector3 Refract(Vector3 dir, Vector3 n, float eta)
    {
        float cos = -Vector3.Dot(dir, n), k = 1 - eta * eta * (1 - cos * cos);
        return k < 0 ? Vector3.Reflect(dir, n) : Vector3.Normalize(dir * eta + n * (eta * cos - MathF.Sqrt(k)));
    }
}
