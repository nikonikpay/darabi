using System.Numerics; using System.Runtime.CompilerServices; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Render;

/// <summary>What a ray met: how far along it, which placed mesh, which of its triangles (<see cref="Prim"/> as the mesh numbers them,
/// <see cref="Slot"/> as its tree holds them) and where on the triangle. A shadow ray comes back with the colour the stained panes on its way gave the light.</summary>
internal struct RayHit { public float T, U, V; public int Instance, Prim, Slot; public Vector3 Tint; }

/// <summary>
/// The Persian garden as a renderer on the processor sees it, and the path tracer that draws it: the way V-Ray, Corona and Cycles work on a
/// CPU, in Mazesta's own code (no library). The scene is the Direct3D test's day scene as the file has it (<see cref="GardenScene"/>): every
/// mesh keeps one tree over its own triangles and the scene one over the placed meshes, so a tree that stands forty times in the garden is
/// held once (a ray is carried into the mesh's own space).
/// <para>A path leaves the camera through a lens (what is far from its focus is soft) and, at each surface, asks the sun and one of the
/// lamps directly - a shadow ray to a point of the sun's disc or the lamp's bulb, through the cut-out leaves, coloured by the stained panes
/// it passes - then goes on the way the surface sends light: scattered, or mirrored by its gloss (GGX, Fresnel), up to
/// <see cref="MaxDepth"/> surfaces, ended early by chance where little light is left. Surfaces are the materials' own: their pictures,
/// their relief (normal maps), the bricks' mortar, the earth's mottle. Water ripples, mirrors and bends light; glass mirrors and lets
/// through; the fountain's water is there as it stands at one moment; the sky has its clouds and the mountains round the horizon.</para>
/// <para>Left out, and said so where the result is shown: the garden's small life (butterflies, fireflies), the wind and the weather; a
/// glowing pane lights nothing by itself (the lamp inside it does). The same pixel and pass draw the same path on every thread.</para>
/// </summary>
internal sealed class RenderScene
{
    public const int MaxDepth = 8;
    private const float TMin = 0.002f;
    private const byte AlphaTest = 1, PassesLight = 2, Stains = 4;
    private const int Nearest = 0, SunShadow = 1, LampShadow = 2;
    /// <summary>The picture's own choices: where on its arc the sun stands (0 sunrise, 1 sunset), how far the lamps are lit by day, the
    /// lens (metres to what is sharp, and its radius), and the moment the water is caught at.</summary>
    private const float DayPoint = 0.12f, LampLevel = 0.3f, Focus = 22f, Lens = 0.01f, Moment = 1.3f;

    [StructLayout(LayoutKind.Sequential)] private struct Tri { public Vector3 V0, E1, E2; }
    private struct Placed { public int Mesh; public bool Fitting; public Vector3 I0, I1, I2, At, C0, C1, C2; }
    private struct Lamp { public Vector3 At, Aim, Colour; public float Power, Radius, CosOuter, CosInner; }
    private struct Surface { public Vector3 Albedo, Emission, N; public float Roughness, Metallic, Sheen; }
    private sealed class Mesh { public BvhNode[] Nodes = []; public Tri[] Tris = []; public int[] Prim = []; public byte[] Flags = []; public ushort[] Material = []; public Vector3[] Normals = []; public Vector2[] Uvs = []; public uint[] Indices = []; }
    private sealed class Texture(int width, int height, byte[] rgba) { public readonly int Width = width, Height = height; public readonly byte[] Rgba = rgba; }

    private readonly Mesh?[] _meshes; private readonly Placed[] _placed; private readonly BvhNode[] _top;
    private readonly GardenMaterial[] _materials; private readonly byte[] _materialFlags; private readonly Vector3[] _stain; private readonly Texture?[] _textures;
    private readonly Lamp[] _lamps; private readonly Texture? _mountains; private readonly float _tanLow, _tanHigh, _noon, _waterLevel; private readonly GardenFountain? _fountain;
    private readonly Vector3 _sun, _sunColor, _zenith, _horizon, _ground, _haze;
    private readonly Vector3 _eye, _forward, _right, _up; private readonly float _tanHalf;
    private static readonly float[] Linear = [.. Enumerable.Range(0, 256).Select(i => { float x = i / 255f; return x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f); })];

    /// <summary>Triangles a ray can meet (every placed mesh's), and those held in memory (each mesh's once).</summary>
    public long Triangles { get; } public long UniqueTriangles { get; }
    /// <summary>What the picture's light is multiplied by before it is shown (the garden's own exposure by day).</summary>
    public float Exposure { get; }
    public int Lamps => _lamps.Length;

    public RenderScene(GardenScene scene)
    {
        const uint day = (uint)GardenScene.Mode.Raster;
        var materials = scene.Materials.ToList(); var sources = scene.Meshes.ToList();
        var shown = scene.Instances.Where(i => (i.Mask & day) != 0 && scene.Meshes[(int)i.Mesh].Indices.Length >= 3).ToList();
        _fountain = scene.Fountain; _waterLevel = scene.WaterLevel;
        if (_fountain is { } fountain)
        {
            // the fountain's water as it stands at one moment: its droplets (GardenGpu's own, each a small ball of white water) and the column they leave
            var still = new GardenInstance { Mask = day, Row0 = new(1, 0, 0, 0), Row1 = new(0, 1, 0, 0), Row2 = new(0, 0, 1, 0) };
            materials.Add(new GardenMaterial { Kind = GardenMaterialKind.Flat, Texture = -1, NormalTexture = -1, Base = new(0.80f, 0.89f, 0.93f), Alpha = 1, Roughness = 0.12f });
            sources.Add(GardenGpu.Ball((uint)materials.Count - 1));
            for (uint id = 0; id < GardenGpu.JetDroplets + GardenGpu.SpillDroplets; id++)
            {
                var (pos, vel, size) = GardenGpu.Droplet(fountain, scene.WaterLevel, id, Moment); if (size <= 0) continue;
                var w = GardenGpu.DropletWorld(pos, vel, size);
                shown.Add(still with { Mesh = (uint)sources.Count - 1, Row0 = new(w.M11, w.M21, w.M31, w.M41), Row1 = new(w.M12, w.M22, w.M32, w.M42), Row2 = new(w.M13, w.M23, w.M33, w.M43) });
            }
            int water = materials.FindIndex(m => m.Kind == GardenMaterialKind.Water);
            if (water >= 0) { sources.Add(GardenGpu.Tube(GardenGpu.JetColumn(fountain), (uint)water)); shown.Add(still with { Mesh = (uint)sources.Count - 1 }); }
        }
        _materials = [.. materials];
        _materialFlags = [.. _materials.Select(m => (byte)((m.Kind == GardenMaterialKind.Cutout && m.Texture >= 0 ? AlphaTest : 0)
            | (m.Kind is GardenMaterialKind.Water or GardenMaterialKind.Glass or GardenMaterialKind.Emissive or GardenMaterialKind.Smoke ? PassesLight : 0)
            | (m.Kind == GardenMaterialKind.Glass && m.LightTint > 0 ? Stains : 0)))];
        // what a stained pane passes: its own hue at full strength (Garden.hlsli StainTint)
        _stain = [.. _materials.Select(m => m.Base / MathF.Max(MathF.Max(m.Base.X, m.Base.Y), MathF.Max(m.Base.Z, 1e-3f)) * 0.9f)];
        _textures = new Texture?[scene.Textures.Count];
        Parallel.ForEach(_materials.SelectMany(m => new[] { m.Texture, m.NormalTexture }).Where(t => t >= 0).Distinct(), t =>
        {
            // at 1024 texels across or its own size if that is less
            var chain = scene.Textures[t]; int own = 4 << (chain.Length - 1), level = Math.Max(0, int.Log2(own) - 10);
            _textures[t] = Decode(chain[level], own >> level, own >> level);
        });
        if (scene.Backdrop is { } back) { _mountains = Decode(back.Bc3, back.Width, back.Height); _tanLow = back.TanLow; _tanHigh = back.TanHigh; }

        _meshes = new Mesh?[sources.Count];
        Parallel.ForEach(shown.Select(i => (int)i.Mesh).Distinct(), m => _meshes[m] = Build(sources[m]));

        var placed = new List<Placed>(); var min = new List<Vector3>(); var max = new List<Vector3>();
        foreach (var i in shown)
        {
            Vector3 c0 = new(i.Row0.X, i.Row1.X, i.Row2.X), c1 = new(i.Row0.Y, i.Row1.Y, i.Row2.Y), c2 = new(i.Row0.Z, i.Row1.Z, i.Row2.Z);
            float det = Vector3.Dot(c0, Vector3.Cross(c1, c2)); if (MathF.Abs(det) < 1e-20f) continue;   // squashed flat: nothing of it can be met
            var root = _meshes[i.Mesh]!.Nodes[0]; Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int k = 0; k < 8; k++) { var p = i.Apply(new((k & 1) == 0 ? root.Min.X : root.Max.X, (k & 2) == 0 ? root.Min.Y : root.Max.Y, (k & 4) == 0 ? root.Min.Z : root.Max.Z)); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            placed.Add(new Placed { Mesh = (int)i.Mesh, Fitting = (i.Flags & GardenScene.FittingFlag) != 0, I0 = Vector3.Cross(c1, c2) / det, I1 = Vector3.Cross(c2, c0) / det, I2 = Vector3.Cross(c0, c1) / det, At = new(i.Row0.W, i.Row1.W, i.Row2.W), C0 = c0, C1 = c1, C2 = c2 });
            min.Add(lo); max.Add(hi); Triangles += _meshes[i.Mesh]!.Tris.Length;
        }
        (_top, var order) = Bvh.Build([.. min], [.. max], 1);
        _placed = [.. order.Select(k => placed[k])];
        UniqueTriangles = _meshes.Sum(m => (long)(m?.Tris.Length ?? 0));

        // the light of a moment of the garden's own day (GardenDay), under the scene's own sun
        var own = scene.Lights.First(l => l.Kind == GardenLightKind.Sun && (l.Mask & day) != 0);
        var light = GardenDay.At(GardenDay.Sunrise + DayPoint * (GardenDay.Sunset - GardenDay.Sunrise), Vector3.Normalize(-own.Direction), own.Color * own.Energy, Vector3.UnitY, Vector3.Zero);
        _sun = light.Key; _sunColor = light.KeyColor; _zenith = light.Zenith; _horizon = light.Horizon; _ground = light.Ground; _noon = light.Noon; Exposure = light.Exposure;
        _haze = Vector3.Lerp(_horizon, _zenith, 0.25f) * 0.8f;
        // the lamps of the .blend's day scene; Blender's watts become the intensity that is divided by distance squared (as GardenGpu has it)
        _lamps = [.. scene.Lights.Where(l => l.Kind != GardenLightKind.Sun && (l.Mask & day) != 0).Select(l =>
        {
            bool area = l.Kind == GardenLightKind.Spot && l.Blend >= 0.999f && l.Cone > 2.7f; float half = l.Cone / 2;   // the exporter's stand-in for an area light
            var colour = l.Color * l.Energy / (area ? MathF.PI * 2 : 4 * MathF.PI) * LampLevel;
            return new Lamp
            {
                At = l.Position, Aim = Vector3.Normalize(l.Direction), Colour = colour, Power = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z)), Radius = MathF.Max(0.05f, l.Radius),
                CosOuter = l.Kind == GardenLightKind.Spot ? MathF.Cos(half) : -2, CosInner = l.Kind == GardenLightKind.Spot ? MathF.Cos(half * (1 - Math.Clamp(l.Blend, 0.02f, 1f))) : -1,
            };
        }).Where(l => l.Power > 0)];

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

    /// <summary>A picture from its BC3 blocks: four bytes a texel, the rows as the file has them.</summary>
    private static Texture Decode(byte[] bc, int width, int height)
    {
        int across = width / 4; var rgba = new byte[width * height * 4];
        Span<int> alpha = stackalloc int[8]; Span<int> colour = stackalloc int[12];
        for (int by = 0; by < height / 4; by++)
            for (int bx = 0; bx < across; bx++)
            {
                var b = bc.AsSpan((by * across + bx) * 16, 16);
                alpha[0] = b[0]; alpha[1] = b[1]; for (int p = 2; p < 8; p++) alpha[p] = ((8 - p) * alpha[0] + (p - 1) * alpha[1]) / 7;
                ulong steps = BitConverter.ToUInt64(b) >> 16; uint picks = BitConverter.ToUInt32(b[12..]);
                int c0 = b[8] | b[9] << 8, c1 = b[10] | b[11] << 8;
                colour[0] = (c0 >> 11 & 31) * 255 / 31; colour[1] = (c0 >> 5 & 63) * 255 / 63; colour[2] = (c0 & 31) * 255 / 31;
                colour[3] = (c1 >> 11 & 31) * 255 / 31; colour[4] = (c1 >> 5 & 63) * 255 / 63; colour[5] = (c1 & 31) * 255 / 31;
                for (int c = 0; c < 3; c++) { colour[6 + c] = (2 * colour[c] + colour[3 + c]) / 3; colour[9 + c] = (colour[c] + 2 * colour[3 + c]) / 3; }
                for (int k = 0; k < 16; k++)
                {
                    int o = ((by * 4 + k / 4) * width + bx * 4 + k % 4) * 4, pick = (int)(picks >> k * 2 & 3) * 3;
                    rgba[o] = (byte)colour[pick]; rgba[o + 1] = (byte)colour[pick + 1]; rgba[o + 2] = (byte)colour[pick + 2]; rgba[o + 3] = (byte)alpha[(int)(steps >> k * 3 & 7)];
                }
            }
        return new Texture(width, height, rgba);
    }

    // ——— rays ———

    /// <summary>The nearest thing a ray meets within <paramref name="tMax"/>; or, for a shadow ray (to the sun, or to a lamp: its own fitting
    /// does not shade it), whether anything that stops light is in the way at all.</summary>
    private bool Trace(Vector3 origin, Vector3 dir, float tMax, int kind, out RayHit hit)
    {
        hit = new RayHit { T = tMax, Instance = -1, Tint = Vector3.One };
        Vector3 inv = Bvh.Inverse(dir); Span<int> stack = stackalloc int[64]; int sp = 0, at = 0;
        if (!Bvh.Enters(_top[0], origin, inv, hit.T, out _)) return false;
        while (true)
        {
            ref readonly var node = ref _top[at];
            if (node.Count > 0)
            {
                for (int i = node.First, end = i + node.Count; i < end; i++)
                {
                    ref readonly var p = ref _placed[i]; if (kind == LampShadow && p.Fitting) continue;
                    var o = origin - p.At;
                    if (Intersect(_meshes[p.Mesh]!, new(Vector3.Dot(p.I0, o), Vector3.Dot(p.I1, o), Vector3.Dot(p.I2, o)), new(Vector3.Dot(p.I0, dir), Vector3.Dot(p.I1, dir), Vector3.Dot(p.I2, dir)), ref hit, kind != Nearest))
                    { hit.Instance = i; if (kind != Nearest) return true; }
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
                    if (shadow && (flags & PassesLight) != 0) { if ((flags & Stains) != 0) hit.Tint *= _stain[m.Material[m.Prim[i]]]; continue; }
                    if ((flags & AlphaTest) != 0 && !Opaque(m, m.Prim[i], u, v)) continue;
                    hit.T = dist; hit.U = u; hit.V = v; hit.Prim = m.Prim[i]; hit.Slot = i; found = true;
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
        return Texel(_textures[mat.Texture]!, uv, false).W * mat.Alpha >= 0.5f;
    }

    private static Vector2 Uv(Mesh m, int prim, float u, float v)
    {
        Vector2 a = m.Uvs[m.Indices[prim * 3]], b = m.Uvs[m.Indices[prim * 3 + 1]], c = m.Uvs[m.Indices[prim * 3 + 2]];
        return a + (b - a) * u + (c - a) * v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 At(Texture t, int x, int y)
    {
        int o = ((y & (t.Height - 1)) * t.Width + (x & (t.Width - 1))) * 4;
        return new(Linear[t.Rgba[o]], Linear[t.Rgba[o + 1]], Linear[t.Rgba[o + 2]], t.Rgba[o + 3] / 255f);
    }

    /// <summary>A picture that repeats, at a place of it: colour as light (linear), alpha 0 to 1. <paramref name="smooth"/>: blended from the
    /// four texels round the place (what the eye sees directly), else the one it falls in.</summary>
    private static Vector4 Texel(Texture t, Vector2 uv, bool smooth)
    {
        float fx = uv.X * t.Width, fy = uv.Y * t.Height;
        if (!smooth) return At(t, (int)MathF.Floor(fx), (int)MathF.Floor(fy));
        fx -= 0.5f; fy -= 0.5f; int x = (int)MathF.Floor(fx), y = (int)MathF.Floor(fy); float ax = fx - x, ay = fy - y;
        return Vector4.Lerp(Vector4.Lerp(At(t, x, y), At(t, x + 1, y), ax), Vector4.Lerp(At(t, x, y + 1), At(t, x + 1, y + 1), ax), ay);
    }

    // ——— surfaces (Garden.hlsli's MaterialSurface and Relief, on the processor) ———

    private static float Frac(float x) => x - MathF.Floor(x);
    private static float Smooth(float from, float to, float x) { x = Math.Clamp((x - from) / (to - from), 0, 1); return x * x * (3 - 2 * x); }
    private static float Pow5(float x) { float x2 = x * x; return x2 * x2 * x; }
    private static float Most(Vector3 c) => MathF.Max(c.X, MathF.Max(c.Y, c.Z));
    private static float Hash3(Vector3 p) { p = new Vector3(Frac(p.X * 0.3183099f + 0.1f), Frac(p.Y * 0.3183099f + 0.1f), Frac(p.Z * 0.3183099f + 0.1f)) * 17; return Frac(p.X * p.Y * p.Z * (p.X + p.Y + p.Z)); }
    private static float Noise3(Vector3 x)
    {
        var i = new Vector3(MathF.Floor(x.X), MathF.Floor(x.Y), MathF.Floor(x.Z)); var f = x - i; f = f * f * (new Vector3(3) - 2 * f);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(L(L(Hash3(i), Hash3(i + new Vector3(1, 0, 0)), f.X), L(Hash3(i + new Vector3(0, 1, 0)), Hash3(i + new Vector3(1, 1, 0)), f.X), f.Y),
                 L(L(Hash3(i + new Vector3(0, 0, 1)), Hash3(i + new Vector3(1, 0, 1)), f.X), L(Hash3(i + new Vector3(0, 1, 1)), Hash3(i + new Vector3(1, 1, 1)), f.X), f.Y), f.Z);
    }
    private static float Fbm(Vector3 p) { float a = 0.5f, f = 0; for (int k = 0; k < 5; k++) { f += a * Noise3(p); p = p * 2.03f + new Vector3(17.1f); a *= 0.5f; } return f; }
    private static float BrickNoise(uint n)
    {
        n = (n + 1013) & 0x7fffffff; n = (n >> 13) ^ n;
        return 0.5f * (((n * (n * n * 60493 + 19990303) + 1376312589) & 0x7fffffff) / 1073741824f);
    }

    private static Vector2 FaceCoords(Vector3 p, Vector3 n)
    {
        var a = Vector3.Abs(n);
        return a.Y > 0.6f ? new(p.X, p.Z) : a.X > 0.6f ? new(p.Z, p.Y) : new(p.X, p.Y);
    }

    /// <summary>The surface at a hit: the material's colour, gloss and metal there, and the way it faces once its relief is counted.
    /// <paramref name="n"/>: the triangle's own smooth normal, toward where the ray came from (<paramref name="v"/>).</summary>
    private Surface SurfaceAt(in Placed placed, Mesh mesh, in GardenMaterial m, in RayHit hit, Vector3 p, Vector3 n, Vector3 v, bool smooth)
    {
        var s = new Surface { Albedo = m.Base, Emission = m.Emission, N = n, Roughness = m.Roughness, Metallic = m.Metallic, Sheen = m.Kind == GardenMaterialKind.Cutout ? 0.12f : 1 };
        bool plain = m.Kind <= GardenMaterialKind.Cutout, world = plain && m.Pattern.Z > 0;
        if (m.Texture >= 0 || m.NormalTexture >= 0)
        {
            var uv = world ? FaceCoords(p, n) * m.Pattern.Z : Uv(mesh, hit.Prim, hit.U, hit.V) * (plain && m.Pattern.X > 0 ? new Vector2(m.Pattern.X, m.Pattern.Y) : Vector2.One);
            if (m.Texture >= 0)
            {
                var texel = Texel(_textures[m.Texture]!, uv, smooth); s.Albedo *= new Vector3(texel.X, texel.Y, texel.Z);
                if (m.Kind != GardenMaterialKind.Cutout) s.Roughness = texel.W;
            }
            if (m.NormalTexture >= 0)
            {
                // the way the picture's u and v run over the surface: along the world's axes where it is laid by position, else from the triangle's own corners
                Vector3 tu = default, tv = default;
                if (world) { var a = Vector3.Abs(n); tu = a.Y > 0.6f || a.X <= 0.6f ? Vector3.UnitX : Vector3.UnitZ; tv = a.Y > 0.6f ? Vector3.UnitZ : Vector3.UnitY; }
                else
                {
                    Vector2 ua = mesh.Uvs[mesh.Indices[hit.Prim * 3]], d1 = mesh.Uvs[mesh.Indices[hit.Prim * 3 + 1]] - ua, d2 = mesh.Uvs[mesh.Indices[hit.Prim * 3 + 2]] - ua; float det = d1.X * d2.Y - d2.X * d1.Y;
                    if (MathF.Abs(det) > 1e-12f)
                    {
                        ref readonly var tri = ref mesh.Tris[hit.Slot];
                        Vector3 e1 = placed.C0 * tri.E1.X + placed.C1 * tri.E1.Y + placed.C2 * tri.E1.Z, e2 = placed.C0 * tri.E2.X + placed.C1 * tri.E2.Y + placed.C2 * tri.E2.Z;
                        tu = (e1 * d2.Y - e2 * d1.Y) / det; tv = (e2 * d1.X - e1 * d2.X) / det;
                    }
                }
                var texel = Texel(_textures[m.NormalTexture]!, uv, smooth); s.Metallic = texel.Z;   // (x in alpha, y in green; metalness in blue)
                float x = texel.W * 2 - 1, y = texel.Y * 2 - 1;
                tu -= n * Vector3.Dot(n, tu); tv -= n * Vector3.Dot(n, tv); float lu = tu.LengthSquared(), lv = tv.LengthSquared();
                if (lu > 1e-12f && lv > 1e-12f)
                {
                    var bumped = Vector3.Normalize(tu / MathF.Sqrt(lu) * x + tv / MathF.Sqrt(lv) * y + n * MathF.Sqrt(Math.Clamp(1 - x * x - y * y, 0, 1)));
                    if (Vector3.Dot(bumped, v) > 0.02f) s.N = bumped;   // relief that would face away from the eye is not followed
                }
            }
        }
        if (m.Kind == GardenMaterialKind.Brick && m.Pattern.Z > 0 && m.Pattern.W > 0)
        {
            // Blender's brick texture (every second row half a brick along), laid on each face from world coordinates
            var f = FaceCoords(p, n) * m.Pattern.X; float width = m.Pattern.Z, height = m.Pattern.W;
            int row = (int)MathF.Floor(f.Y / height); float along = f.X + (row % 2 != 0 ? 0 : width * 0.5f); int brick = (int)MathF.Floor(along / width);
            float x = along - width * brick, y = f.Y - height * row; bool mortar = MathF.Min(MathF.Min(x, y), MathF.Min(width - x, height - y)) < m.Pattern.Y;
            s.Albedo = mortar ? m.Mortar : Vector3.Lerp(m.Base, m.Color2, Math.Clamp(BrickNoise((uint)((row << 16) + (brick & 0xFFFF))), 0, 1));
            s.Albedo *= 0.9375f + 0.125f * Noise3(p * 6);
            if (mortar) s.Roughness = MathF.Min(1, s.Roughness + 0.2f);
        }
        else if (m.Kind == GardenMaterialKind.Flat && m.Texture < 0 && m.Metallic < 0.5f)
        {
            // earth, turf and bare stone are never one colour: broad patches, a finer mottle, and grain at the scale of a pebble
            float mottle = 0.55f * Noise3(p * 2.3f) + 0.30f * Noise3(p * 9.1f) + 0.15f * Noise3(p * 37), share = m.Pattern.W > 0 ? m.Pattern.W : m.Roughness > 0.5f ? 0.5f : 0.2f;
            s.Albedo *= 1 + (mottle - 0.5f) * share;
        }
        return s;
    }

    /// <summary>The light leaving a surface toward <paramref name="v"/> when irradiance <paramref name="e"/> falls on it from <paramref name="l"/>:
    /// a GGX highlight (Smith's shadowing, Schlick's Fresnel) over Lambert's diffuse for what it does not mirror (Garden.hlsli Brdf).</summary>
    private static Vector3 Brdf(in Surface s, Vector3 v, Vector3 l, Vector3 e)
    {
        float nl = Vector3.Dot(s.N, l); if (nl <= 0) return Vector3.Zero;
        var h = Vector3.Normalize(l + v); float nh = Math.Clamp(Vector3.Dot(s.N, h), 0, 1), nv = MathF.Max(MathF.Abs(Vector3.Dot(s.N, v)), 1e-3f);
        float a = MathF.Max(0.045f, s.Roughness * s.Roughness), a2 = a * a, k = nh * nh * (a2 - 1) + 1;
        float d = a2 / (MathF.PI * k * k), vis = 0.5f / (nl * MathF.Sqrt(nv * nv * (1 - a2) + a2) + nv * MathF.Sqrt(nl * nl * (1 - a2) + a2));
        var f0 = Vector3.Lerp(new(0.04f), s.Albedo, s.Metallic); var f = (f0 + (Vector3.One - f0) * Pow5(1 - Math.Clamp(Vector3.Dot(h, v), 0, 1))) * s.Sheen;
        return e * nl * ((Vector3.One - f) * (1 - s.Metallic) * s.Albedo / MathF.PI + f * MathF.Min(d * vis, 60));
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

    /// <summary>The sky along a direction: its gradient and the glow round the sun, the clouds, the mountains round the horizon (Garden.hlsli
    /// Sky, at one moment). <paramref name="disc"/>: the sun itself too (a path that was not already given its light directly).</summary>
    private Vector3 Sky(Vector3 dir, bool disc)
    {
        float h = 1 - Math.Clamp(dir.Y, 0, 1), toSun = MathF.Max(0, Vector3.Dot(dir, _sun)), sun6 = toSun * toSun; sun6 *= sun6 * sun6;
        var c = Vector3.Lerp(_zenith, _horizon, h * h * h) + _sunColor * (0.05f * sun6 + 0.16f * MathF.Pow(toSun, 64) + (disc ? 2 * Smooth(0.99988f, 0.99996f, toSun) : 0));
        if (dir.Y < 0) c = Vector3.Lerp(c, _ground, Math.Clamp(-dir.Y * 4, 0, 1));
        if (dir.Y > 0.01f)
        {
            // clouds: a few octaves of noise on a plane high above, thinning toward the horizon, lit on the sun's side
            float qx = dir.X / (dir.Y + 0.12f) * 1.6f, qz = dir.Z / (dir.Y + 0.12f) * 1.6f;
            float cover = Smooth(0.42f, 0.72f, Fbm(new(qx, qz, 3.7f))) * Smooth(0.01f, 0.18f, dir.Y);
            if (cover > 0)
            {
                float thick = Smooth(0.5f, 0.85f, Fbm(new(qx * 1.7f, qz * 1.7f, 9.1f))), sun8 = toSun * toSun; sun8 *= sun8; sun8 *= sun8;
                var lit = (_horizon.X + _horizon.Y + _horizon.Z) * 0.333f * new Vector3(1.55f, 1.42f, 1.3f) + _sunColor * (0.026f + 0.08f * sun8);
                c = Vector3.Lerp(c, lit * (1 - 0.38f * thick), cover * 0.92f);
            }
        }
        if (_mountains is null) return c;
        float t = dir.Y / MathF.Max(MathF.Sqrt(dir.X * dir.X + dir.Z * dir.Z), 1e-4f), span = _tanHigh - _tanLow;
        float w = (1 - Smooth(_tanLow + span * 0.55f, _tanLow + span * 0.95f, t)) * Math.Clamp(1 + (t - _tanLow) * 25, 0, 1);   // under the horizon the ground's haze takes over
        if (w <= 0) return c;
        var texel = Texel(_mountains, new(MathF.Atan2(dir.X, dir.Z) / (2 * MathF.PI) + 0.5f, Math.Clamp((t - _tanLow) / span, 0.004f, 0.996f)), true);
        var m = new Vector3(texel.X, texel.Y, texel.Z); float grey = Vector3.Dot(m, new(0.3f, 0.59f, 0.11f));
        return Vector3.Lerp(c, Vector3.Lerp(m * 0.76f, Vector3.Lerp(new(grey), m, 0.4f) * new Vector3(0.66f, 0.76f, 0.95f), _noon), w);   // the picture is the range at sunset: with the sun high it is taken toward the haze's blue
    }

    /// <summary>The pool's surface at a place, as its ripples stand at the picture's moment (Garden.hlsli WaterNormal), with the rings that run out from where the fountain's water falls.</summary>
    private Vector3 WaterNormal(Vector3 p)
    {
        const float t = Moment; var q = new Vector2(p.X, p.Z);
        var g = new Vector2(0.030f * MathF.Cos(q.X * 1.7f + t * 1.3f), 0.025f * MathF.Cos(q.Y * 2.1f - t * 1.1f));
        g += 0.012f * MathF.Cos(Vector2.Dot(q, new(3.1f, 2.3f)) + t * 2.2f) * new Vector2(3.1f, 2.3f) * 0.3f;
        g += 0.008f * MathF.Cos(Vector2.Dot(q, new(-4.7f, 5.3f)) - t * 2.9f) * new Vector2(-4.7f, 5.3f) * 0.2f;
        if (_fountain is { } f)
        {
            var d = q - new Vector2(f.Nozzle.X, f.Nozzle.Z); float r = MathF.Max(d.Length(), 1e-3f), from = p.Y > _waterLevel + 0.3f ? 0 : f.RimRadius;
            g += d / r * (0.045f * MathF.Cos((r - from) * 11 - t * 7) * MathF.Exp(-MathF.Abs(r - from) * 0.6f));
        }
        return Vector3.Normalize(new(-g.X * 0.5f, 1, -g.Y * 0.5f));   // (half the Direct3D test's height: a still picture shows every ripple that a moving one blurs)
    }

    /// <summary>The light that comes to the camera along one path through pixel (<paramref name="x"/>, <paramref name="y"/>) of a picture of
    /// <paramref name="width"/> by <paramref name="height"/>; <paramref name="rays"/> counts every ray it sent.</summary>
    public Vector3 Sample(int x, int y, int width, int height, ref uint seed, ref long rays)
    {
        // where in the pixel: nearer its middle more often (a tent two pixels wide), so edges come out smooth without being soft
        static float Tent(float u) => u < 0.5f ? MathF.Sqrt(2 * u) - 1 : 1 - MathF.Sqrt(2 - 2 * u);
        float sx = ((x + 0.5f + Tent(Rand(ref seed))) / width * 2 - 1) * _tanHalf * width / height, sy = (1 - (y + 0.5f + Tent(Rand(ref seed))) / height * 2) * _tanHalf;
        // through a point of the lens toward where the pixel's line meets the plane that is in focus
        float turn = Rand(ref seed) * 2 * MathF.PI, off = Lens * MathF.Sqrt(Rand(ref seed));
        Vector3 origin = _eye + (_right * MathF.Cos(turn) + _up * MathF.Sin(turn)) * off, dir = Vector3.Normalize(_eye + (_forward + _right * sx + _up * sy) * Focus - origin);
        Vector3 light = Vector3.Zero, through = Vector3.One; float haze = 0;
        // disc: the sun itself would be seen along this path; glow: what glows too (the eye's own line, mirrored or through glass);
        // soft: the path has been scattered, and one lucky find on it must not leave a white speck for the rest of the run
        bool disc = true, glow = true, soft = false;
        Span<float> reach = stackalloc float[_lamps.Length];
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            rays++;
            if (!Trace(origin, dir, float.MaxValue, Nearest, out var hit)) { light += through * Sky(dir, disc); break; }
            ref readonly var placed = ref _placed[hit.Instance]; var mesh = _meshes[placed.Mesh]!;
            ref readonly var m = ref _materials[mesh.Material[hit.Prim]];
            Vector3 p = origin + dir * hit.T, v = -dir;
            Vector3 na = mesh.Normals[mesh.Indices[hit.Prim * 3]], nb = mesh.Normals[mesh.Indices[hit.Prim * 3 + 1]], nc = mesh.Normals[mesh.Indices[hit.Prim * 3 + 2]], nl = na + (nb - na) * hit.U + (nc - na) * hit.V;
            var faces = Vector3.Normalize(placed.I0 * nl.X + placed.I1 * nl.Y + placed.I2 * nl.Z); var n = Vector3.Dot(faces, dir) > 0 ? -faces : faces;
            if (depth == 0) haze = 1 - MathF.Exp(-hit.T * 0.0012f);   // the air between the eye and a far surface takes on the low sky's colour
            var s = SurfaceAt(placed, mesh, m, hit, p, n, v, !soft);
            if (glow) light += through * s.Emission;

            if (m.Kind == GardenMaterialKind.Water && MathF.Abs(faces.Y) <= 0.7f)
            {
                // falling water is full of air: part of the light that falls on it comes back white, the rest goes straight on
                if (Rand(ref seed) >= 0.45f) { through *= new Vector3(0.93f, 0.98f, 0.98f); origin = p; continue; }
                s.Albedo = new(0.82f, 0.92f, 0.96f); s.Roughness = 0.5f; s.Metallic = 0; s.Sheen = 0; s.N = n;
            }
            else if (m.Kind == GardenMaterialKind.Water)
            {
                var wn = WaterNormal(p); bool entering = Vector3.Dot(wn, dir) < 0; if (!entering) wn = -wn;
                float fresnel = 0.02f + 0.98f * Pow5(1 - MathF.Min(1, -Vector3.Dot(dir, wn)));
                if (Rand(ref seed) < fresnel) dir = Vector3.Reflect(dir, wn);
                else { dir = Refract(dir, wn, entering ? 1 / 1.33f : 1.33f); if (entering) through *= Vector3.Lerp(Vector3.One, s.Albedo, 0.5f); }
                origin = p; continue;
            }
            else if (m.Kind == GardenMaterialKind.Glass)
            {
                float fresnel = 0.04f + 0.96f * Pow5(1 - MathF.Min(1, Vector3.Dot(v, n)));
                if (Rand(ref seed) < fresnel) dir = Vector3.Reflect(dir, n);
                else through *= m.LightTint > 0 ? _stain[mesh.Material[hit.Prim]] : Vector3.Lerp(Vector3.One, s.Albedo, 0.3f);   // a pane is thin: the path goes straight on, coloured
                origin = p; continue;
            }

            // the sun, asked directly: a point of its disc (half a degree across), through whatever leaves and panes are in the way
            var toSun = Vector3.Normalize(_sun + OnSphere(ref seed) * 0.0045f);
            if (Vector3.Dot(s.N, toSun) > 0 && Vector3.Dot(n, toSun) > 0) { rays++; if (!Trace(p, toSun, float.MaxValue, SunShadow, out var shade)) light += through * Brdf(s, v, toSun, _sunColor) * shade.Tint; }
            // and one lamp, picked by how much each could give here: a point of its bulb
            float total = 0;
            for (int k = 0; k < _lamps.Length; k++)
            {
                ref readonly var lamp = ref _lamps[k]; var d = lamp.At - p; float d2 = d.LengthSquared(), share = 0;
                if (Vector3.Dot(s.N, d) > 0)
                {
                    share = lamp.Power / MathF.Max(d2, lamp.Radius * lamp.Radius + 0.01f);
                    if (lamp.CosOuter > -1) share *= Smooth(lamp.CosOuter, lamp.CosInner, -Vector3.Dot(d, lamp.Aim) / MathF.Sqrt(d2));
                }
                reach[k] = total += share;
            }
            if (total > 0)
            {
                float pick = Rand(ref seed) * total; int k = 0; while (k < _lamps.Length - 1 && reach[k] <= pick) k++;
                ref readonly var lamp = ref _lamps[k];
                var q = lamp.At + OnSphere(ref seed) * (lamp.Radius * MathF.Cbrt(Rand(ref seed))) - p; float far = q.Length();
                rays++;
                if (far > 0.06f && !Trace(p, q / far, far - 0.05f, LampShadow, out var shade))
                {
                    var lit = through * Brdf(s, v, Vector3.Normalize(lamp.At - p), lamp.Colour * (total / lamp.Power)) * shade.Tint;
                    light += soft ? Vector3.Min(lit, new(4)) : lit;
                }
            }

            // on, the way the surface sends light: mirrored by its gloss, or scattered
            float nv = MathF.Max(Vector3.Dot(s.N, v), 1e-3f); var f0 = Vector3.Lerp(new(0.04f), s.Albedo, s.Metallic);
            var gloss = (f0 + (Vector3.One - f0) * Pow5(1 - nv)) * s.Sheen; var matt = (Vector3.One - gloss) * (1 - s.Metallic) * s.Albedo;
            float chance = Most(gloss) / (Most(gloss) + Most(matt) + 1e-6f);
            if (Rand(ref seed) < chance)
            {
                // a facet of the surface, picked as its roughness spreads them (GGX), mirrors the path
                float a = MathF.Max(0.045f, s.Roughness * s.Roughness), a2 = a * a, u = Rand(ref seed), cos2 = (1 - u) / (1 + (a2 - 1) * u), sin = MathF.Sqrt(MathF.Max(0, 1 - cos2)), round = Rand(ref seed) * 2 * MathF.PI;
                var t = Vector3.Normalize(MathF.Abs(s.N.Y) < 0.99f ? Vector3.Cross(s.N, Vector3.UnitY) : Vector3.Cross(s.N, Vector3.UnitX)); var b = Vector3.Cross(s.N, t);
                var facet = t * (sin * MathF.Cos(round)) + b * (sin * MathF.Sin(round)) + s.N * MathF.Sqrt(cos2);
                float vh = Vector3.Dot(v, facet); if (vh <= 0) break;
                dir = facet * (2 * vh) - v; float ln = Vector3.Dot(s.N, dir); if (ln <= 0 || Vector3.Dot(n, dir) <= 0) break;
                float vis = 0.5f / (ln * MathF.Sqrt(nv * nv * (1 - a2) + a2) + nv * MathF.Sqrt(ln * ln * (1 - a2) + a2));
                var f = (f0 + (Vector3.One - f0) * Pow5(1 - vh)) * s.Sheen;
                through *= Vector3.Min(f * (4 * vis * ln * vh / MathF.Max(MathF.Sqrt(cos2), 1e-4f) / chance), new(2));
                glow = a < 0.1f; soft |= !glow;
            }
            else
            {
                dir = Vector3.Normalize(s.N + OnSphere(ref seed) * 0.999f); if (Vector3.Dot(n, dir) <= 0) break;
                through *= matt / (1 - chance); glow = false; soft = true;
            }
            disc = false; origin = p;
            if (depth >= 3)
            {
                float keep = Math.Clamp(Most(through), 0.05f, 0.95f);
                if (Rand(ref seed) > keep) break;
                through /= keep;
            }
        }
        light = Vector3.Min(Vector3.Lerp(light, _haze, haze), new(60));
        return float.IsFinite(light.X + light.Y + light.Z) ? light : Vector3.Zero;
    }

    private static Vector3 Refract(Vector3 dir, Vector3 n, float eta)
    {
        float cos = -Vector3.Dot(dir, n), k = 1 - eta * eta * (1 - cos * cos);
        return k < 0 ? Vector3.Reflect(dir, n) : Vector3.Normalize(dir * eta + n * (eta * cos - MathF.Sqrt(k)));
    }
}
