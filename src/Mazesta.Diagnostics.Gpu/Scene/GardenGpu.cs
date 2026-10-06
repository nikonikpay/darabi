using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The garden on the GPU for one of its two scenes: every mesh in one vertex and one index buffer, the instances of that scene grouped by
/// mesh (so each mesh is one instanced draw per material), the materials, the lights, the textures (base colours and normal maps) as one
/// BC3 texture array, and the mountains round the horizon. What moves is placed here for both renderers, as a function of the time alone:
/// the logo (turning to the camera), the ray-traced scene's mirror sphere (gliding round the pool) and the fountain's water - droplets
/// this class adds to the scene, each on a flight of its own that ends where it meets the bowl or the pool (<see cref="Droplet"/>).
/// Both renderers build on it. The owner's Models\gpu-test.obj, when there is one, takes the logo's place over the pool.
/// </summary>
internal sealed unsafe class GardenGpu
{
    [StructLayout(LayoutKind.Sequential)] public struct Instance { public Vector4 Row0, Row1, Row2; public uint Mesh, Mask, Flags, Index; }
    [StructLayout(LayoutKind.Sequential)] public struct Light { public Vector3 Position; public uint Kind; public Vector3 Direction; public float Range; public Vector3 Color; public float CosOuter; public float CosInner, Radius, Shadow, Pad1; }
    [StructLayout(LayoutKind.Sequential)] public struct MeshInfo { public Vector3 Centre; public uint FirstSubmesh; public Vector3 Extent; public uint BaseVertex; }
    [StructLayout(LayoutKind.Sequential)] public struct SubmeshInfo { public uint IndexStart, Material, Opaque, Pad; }
    public readonly record struct Part(uint IndexStart, uint IndexCount, uint Material, GardenMaterialKind Kind);
    /// <summary>One mesh's draws: its instances are [FirstInstance, +InstanceCount) of <see cref="Instances"/>. Moving: some of them
    /// are placed anew every frame (the logo, the sphere, the droplets).</summary>
    public sealed record Draw(int Mesh, uint FirstInstance, uint InstanceCount, int BaseVertex, uint VertexCount, Vector3 Centre, Vector3 Extent, Part[] Parts, bool Moving);

    /// <summary>The fountain's droplets: those of the jet, and those that spill from the bowl's rim (so many from each of its twelve lobes).</summary>
    public const uint JetDroplets = 220, SpillDroplets = 12 * 14;
    /// <summary>The main pool's surface.</summary>
    public float WaterLevel { get; }
    public GardenFountain? Fountain { get; }
    /// <summary>The light bounced round the courtyard, when the scene has it (the rasteriser reads it; the ray tracer works its own out).</summary>
    public GardenLightVolume? LightVolume { get; }
    public GardenScene.Mode Mode { get; }
    public Instance[] Instances { get; }
    /// <summary>The instances that move from place to place (the logo, the sphere, the droplets), by their place in <see cref="Instances"/>.</summary>
    public int[] Moving { get; }
    /// <summary>The plants the wind bends where they stand (<see cref="GardenScene.Sway"/>).</summary>
    public int[] Swaying { get; }
    public Draw[] Draws { get; }
    public GardenMaterial[] Materials { get; }
    public Light[] PointLights { get; }
    /// <summary>How many of the lamps the rasteriser gives a shadow cube (<see cref="Light.Shadow"/>: which, from 1): its point
    /// lamps - the hall's and the lanterns' - up to this many. The ray tracer shadows every light with rays of its own.</summary>
    public int ShadowLamps { get; }
    public const int MostShadowLamps = 12;
    /// <summary>The nearest a lamp's shadow cube sees: inside a lantern, its own cage is a hand's breadth away.</summary>
    public const float LampNear = 0.03f;
    /// <summary>The key light: toward the sun (Direct3D) or the moon (ray traced), and its irradiance.</summary>
    public Vector3 SunDirection { get; } public Vector3 SunColor { get; }
    public long Triangles { get; }
    public int LogoInstance { get; } = -1;
    public string? ModelProblem { get; }
    public string ModelName { get; } = "Mazesta logo";

    public ID3D12Resource VertexBuffer { get; } public ID3D12Resource IndexBuffer { get; }
    public ID3D12Resource InstanceBuffer { get; } public ID3D12Resource MaterialBuffer { get; } public ID3D12Resource LightBuffer { get; }
    public ID3D12Resource MeshInfoBuffer { get; } public ID3D12Resource SubmeshInfoBuffer { get; }
    public ID3D12Resource Textures { get; }
    public int TextureCount { get; }
    /// <summary>The mountains round the horizon (a 4 x 4 stand-in when the scene has none) and, for the shaders, the range of heights
    /// it covers as tangents (low, high) and whether it is there (1 or 0).</summary>
    public ID3D12Resource Backdrop { get; } public Vector4 BackdropRange { get; }
    private readonly int _textureMips;

    public GardenGpu(D3D12Session s, GardenScene scene, GardenScene.Mode mode, SceneModel? custom = null, string? customProblem = null)
    {
        Mode = mode; ModelProblem = customProblem; WaterLevel = scene.WaterLevel; Fountain = scene.Fountain; LightVolume = scene.Light;
        var meshes = scene.Meshes.ToList(); var materials = scene.Materials.ToList();
        var chosen = scene.Instances.Where(i => (i.Mask & (uint)mode) != 0).ToArray();
        if (Fountain is not null)
        {
            // the fountain's water: one small ball of white water, placed anew every frame for each droplet (their numbers follow below)
            materials.Add(new GardenMaterial { Kind = GardenMaterialKind.Flat, Texture = -1, NormalTexture = -1, Base = new(0.80f, 0.89f, 0.93f), Alpha = 1, Roughness = 0.12f });
            meshes.Add(Ball((uint)materials.Count - 1));
            var droplet = new GardenInstance { Mesh = (uint)meshes.Count - 1, Mask = (uint)mode, Flags = GardenScene.DropletFlag, Row0 = new(1, 0, 0, 0), Row1 = new(0, 1, 0, 0), Row2 = new(0, 0, 1, 0) };
            chosen = [.. chosen, .. Enumerable.Repeat(droplet, (int)(JetDroplets + SpillDroplets))];
        }
        if (custom is not null)
        {
            int logo = Array.FindIndex(chosen, i => (i.Flags & GardenScene.LogoFlag) != 0);
            if (logo >= 0) { chosen[logo].Mesh = (uint)meshes.Count; meshes.Add(FromModel(custom, meshes[(int)scene.Instances.First(i => (i.Flags & GardenScene.LogoFlag) != 0).Mesh])); ModelName = custom.Name; }
        }
        Materials = [.. materials];

        // every mesh's vertices and indices, end to end
        var baseVertex = new int[meshes.Count]; var baseIndex = new uint[meshes.Count]; long vbytes = 0, icount = 0;
        for (int m = 0; m < meshes.Count; m++) { baseVertex[m] = (int)(vbytes / 16); baseIndex[m] = (uint)icount; vbytes += meshes[m].Vertices.Length; icount += meshes[m].Indices.Length; }
        VertexBuffer = s.Buffer((ulong)vbytes, HeapType.Default, ResourceStates.CopyDest);
        s.Fill(VertexBuffer, d => { int at = 0; foreach (var m in meshes) { m.Vertices.CopyTo(d[at..]); at += m.Vertices.Length; } }, ResourceStates.VertexAndConstantBuffer | ResourceStates.NonPixelShaderResource);
        IndexBuffer = s.Buffer((ulong)icount * 4, HeapType.Default, ResourceStates.CopyDest);
        s.Fill(IndexBuffer, d => { int at = 0; foreach (var m in meshes) { MemoryMarshal.AsBytes(m.Indices.AsSpan()).CopyTo(d[at..]); at += m.Indices.Length * 4; } }, ResourceStates.IndexBuffer | ResourceStates.NonPixelShaderResource);

        // instances grouped by mesh, in the file's order within a mesh
        var order = chosen.Select((inst, k) => (inst, k)).OrderBy(x => x.inst.Mesh).ThenBy(x => x.k).Select(x => x.inst).ToArray();
        Instances = [.. order.Select(i => new Instance { Row0 = i.Row0, Row1 = i.Row1, Row2 = i.Row2, Mesh = i.Mesh, Mask = i.Mask, Flags = i.Flags })];
        for (uint k = 0, d = 0; k < Instances.Length; k++) if ((Instances[k].Flags & GardenScene.DropletFlag) != 0) Instances[k].Index = d++;
        LogoInstance = Array.FindIndex(Instances, i => (i.Flags & GardenScene.LogoFlag) != 0);
        Moving = [.. Enumerable.Range(0, Instances.Length).Where(k => (Instances[k].Flags & 7) != 0)];
        Swaying = [.. Enumerable.Range(0, Instances.Length).Where(k => GardenScene.Sway(Instances[k].Flags) > 0)];
        var draws = new List<Draw>();
        for (int k = 0; k < order.Length;)
        {
            int m = (int)order[k].Mesh, n = 0; while (k + n < order.Length && order[k + n].Mesh == m) n++;
            var mesh = meshes[m];
            draws.Add(new Draw(m, (uint)k, (uint)n, baseVertex[m], (uint)mesh.VertexCount, mesh.Centre, mesh.Extent,
                [.. mesh.Submeshes.Select(sub => new Part(baseIndex[m] + sub.IndexStart, sub.IndexCount, sub.Material, materials[(int)sub.Material].Kind))],
                order.Skip(k).Take(n).Any(i => (i.Flags & 7) != 0)));
            Triangles += (long)mesh.Indices.Length / 3 * n; k += n;
        }
        Draws = [.. draws];
        InstanceBuffer = s.Upload(Instances, ResourceStates.NonPixelShaderResource);
        MaterialBuffer = s.Upload(Materials, ResourceStates.NonPixelShaderResource | ResourceStates.PixelShaderResource);

        // lights: Blender's watts become the intensity the shaders divide by distance squared
        var points = new List<Light>();
        foreach (var l in scene.Lights.Where(l => (l.Mask & (uint)mode) != 0))
        {
            if (l.Kind == GardenLightKind.Sun) { SunDirection = Vector3.Normalize(-l.Direction); SunColor = l.Color * l.Energy; continue; }
            bool area = l.Kind == GardenLightKind.Spot && l.Blend >= 0.999f && l.Cone > 2.7f;   // the exporter's stand-in for an area light
            var intensity = l.Color * l.Energy / (area ? MathF.PI * 2 : 4 * MathF.PI);
            float range = Math.Clamp(MathF.Sqrt(Math.Max(intensity.X, Math.Max(intensity.Y, intensity.Z)) / 0.04f), 1.5f, 28f);
            float half = l.Cone / 2;
            bool shadowed = mode == GardenScene.Mode.Raster && l.Kind == GardenLightKind.Point && ShadowLamps < MostShadowLamps;
            points.Add(new Light
            {
                Position = l.Position, Kind = (uint)l.Kind, Direction = Vector3.Normalize(l.Direction), Range = range, Color = intensity, Radius = Math.Max(0.05f, l.Radius), Shadow = shadowed ? ++ShadowLamps : 0,
                CosOuter = l.Kind == GardenLightKind.Spot ? MathF.Cos(half) : -2, CosInner = l.Kind == GardenLightKind.Spot ? MathF.Cos(half * (1 - Math.Clamp(l.Blend, 0.02f, 1f))) : -1,
            });
        }
        PointLights = [.. points];
        LightBuffer = s.Upload(PointLights.Length > 0 ? PointLights : [new Light()], ResourceStates.NonPixelShaderResource | ResourceStates.PixelShaderResource);

        // for the ray tracer: where each mesh's vertices and each submesh's indices start
        var infos = new List<MeshInfo>(); var subs = new List<SubmeshInfo>();
        for (int m = 0; m < meshes.Count; m++)
        {
            infos.Add(new MeshInfo { Centre = meshes[m].Centre, Extent = meshes[m].Extent, FirstSubmesh = (uint)subs.Count, BaseVertex = (uint)baseVertex[m] });
            foreach (var sub in meshes[m].Submeshes) subs.Add(new SubmeshInfo { IndexStart = baseIndex[m] + sub.IndexStart, Material = sub.Material, Opaque = materials[(int)sub.Material].Kind == GardenMaterialKind.Cutout ? 0u : 1u });
        }
        MeshInfoBuffer = s.Upload(infos.ToArray(), ResourceStates.NonPixelShaderResource);
        SubmeshInfoBuffer = s.Upload(subs.ToArray(), ResourceStates.NonPixelShaderResource);

        TextureCount = Math.Max(1, scene.Textures.Count); _textureMips = scene.TextureMips;
        Textures = UploadTextures(s, scene);
        var sky = scene.Backdrop;
        Backdrop = UploadImage(s, sky?.Width ?? 4, sky?.Height ?? 4, sky?.Bc3 ?? new byte[16]);
        BackdropRange = sky is null ? Vector4.Zero : new(sky.TanLow, sky.TanHigh, 1, 0);
    }

    /// <summary>Instance <paramref name="i"/>'s bounds as it was placed: the centre of a ball that holds it, and its radius.</summary>
    public (Vector3 Centre, float Radius) Bounds(Draw d, int i)
    {
        var inst = Instances[i]; var c = d.Centre; var e = d.Extent;
        var centre = new Vector3(inst.Row0.X * c.X + inst.Row0.Y * c.Y + inst.Row0.Z * c.Z + inst.Row0.W, inst.Row1.X * c.X + inst.Row1.Y * c.Y + inst.Row1.Z * c.Z + inst.Row1.W, inst.Row2.X * c.X + inst.Row2.Y * c.Y + inst.Row2.Z * c.Z + inst.Row2.W);
        float scale = MathF.Sqrt(MathF.Max(new Vector3(inst.Row0.X, inst.Row1.X, inst.Row2.X).LengthSquared(), MathF.Max(new Vector3(inst.Row0.Y, inst.Row1.Y, inst.Row2.Y).LengthSquared(), new Vector3(inst.Row0.Z, inst.Row1.Z, inst.Row2.Z).LengthSquared())));
        return (centre, e.Length() * scale);
    }

    /// <summary>Whether a mesh blocks light: one made only of water, glass and glowing parts does not (a lantern's glass would otherwise hide its own lamp).</summary>
    public bool CastsShadow(Draw d) => d.Parts.Any(p => p.Kind is GardenMaterialKind.Flat or GardenMaterialKind.Cutout or GardenMaterialKind.Brick);

    /// <summary>Instance <paramref name="i"/>'s world transform at <paramref name="time"/>: the logo turns to the camera and floats
    /// (Garden.hlsli's LogoMove is its twin), the mirror sphere glides round the pool, a droplet is on its flight.</summary>
    public Matrix4x4 World(int i, float time)
    {
        var inst = Instances[i];
        var m = new Matrix4x4(inst.Row0.X, inst.Row1.X, inst.Row2.X, 0, inst.Row0.Y, inst.Row1.Y, inst.Row2.Y, 0, inst.Row0.Z, inst.Row1.Z, inst.Row2.Z, 0, inst.Row0.W, inst.Row1.W, inst.Row2.W, 1);
        if ((inst.Flags & GardenScene.DropletFlag) != 0) { var (pos, vel, size) = Droplet(inst.Index, time); return DropletWorld(pos, vel, size); }
        if ((inst.Flags & GardenScene.SphereFlag) != 0) return m * Matrix4x4.CreateTranslation(SphereDrift(time));
        if (GardenScene.Sway(inst.Flags) is > 0 and float sway)
        {
            // leaning with the wind: every point pushed sideways by its height above where the plant stands (Garden.hlsli's Swayed is its twin)
            var root = new Vector3(inst.Row0.W, inst.Row1.W, inst.Row2.W); var w = Wind(root, time) * sway;
            return m * new Matrix4x4(1, 0, 0, 0, w.X, 1, w.Y, 0, 0, 0, 1, 0, -w.X * root.Y, 0, -w.Y * root.Y, 1);
        }
        if ((inst.Flags & GardenScene.LogoFlag) == 0) return m;
        var pivot = new Vector3(inst.Row0.W, inst.Row1.W, inst.Row2.W);
        var (c, s, lift) = LogoTurn(pivot, time);
        return m * Matrix4x4.CreateTranslation(-pivot) * LogoMotion(c, s, lift) * Matrix4x4.CreateTranslation(pivot);
    }

    /// <summary>The wind at a plant standing at <paramref name="root"/>, at <paramref name="time"/>: which way (x, z) and how hard it
    /// pushes, in units of the plant's own sway. Gusts roll across the garden - neighbours lean nearly together - with a quicker
    /// flutter over them and a swirl across. A pure function of the time (Garden.hlsli's Wind is its twin): the same frame is the same picture.</summary>
    public static Vector2 Wind(Vector3 root, float time)
    {
        float phase = root.X * 0.35f + root.Z * 0.21f;
        float gust = MathF.Sin(time * 0.9f + phase) + 0.5f * MathF.Sin(time * 2.3f + phase * 1.7f) + 0.25f * MathF.Sin(time * 5.1f + phase * 3.1f);
        float swirl = 0.4f * MathF.Sin(time * 1.7f + phase * 2.3f);
        return new(0.8f * gust - 0.6f * swirl, 0.6f * gust + 0.8f * swirl);
    }

    /// <summary>How far the mirror sphere is from where the scene placed it (beside the fountain, over the pool's right half) at
    /// <paramref name="time"/>: once round the fountain in <see cref="SphereLap"/> seconds, along the pool and back over the water,
    /// rising and sinking a little as it goes. It keeps clear of the fountain, the pool's edge and the logo above.</summary>
    public static Vector3 SphereDrift(float time)
    {
        float a = time / SphereLap * 2 * MathF.PI;
        return new(SphereReach.X * (MathF.Cos(a) - 1), 0.2f * MathF.Sin(a * 3), SphereReach.Y * MathF.Sin(a));
    }
    public const float SphereLap = 32f;
    /// <summary>The sphere's path: an ellipse this far across the pool and along it from the fountain.</summary>
    public static readonly Vector2 SphereReach = new(2.5f, 5.4f);

    /// <summary>Where the fountain's droplet <paramref name="id"/> is at <paramref name="time"/>, how fast it is going and how big it is
    /// (size 0: it is spent, in the water until its next turn). Water leaves the nozzle as droplets, each on its own ballistic flight;
    /// a flight ends where it meets something: the water in the bowl, from which the droplet splashes up again, slower and scattered,
    /// or - once it has cleared the rim - the pool. The droplets after the first <see cref="JetDroplets"/> are the bowl running over:
    /// they leave the rim's twelve lobes and fall to the pool. A pure function, the twin of Garden.hlsli's Droplet (which places them
    /// for the rasteriser; this one places them in the ray tracer's acceleration structure).</summary>
    public (Vector3 Position, Vector3 Velocity, float Size) Droplet(uint id, float time) => Fountain is { } f ? Droplet(f, WaterLevel, id, time) : (default, default, 0);

    /// <summary>The same, for a fountain and a pool given.</summary>
    public static (Vector3 Position, Vector3 Velocity, float Size) Droplet(GardenFountain f, float waterLevel, uint id, float time)
    {
        const float gravity = 9.81f; float level = f.BowlLevel, rim = f.RimRadius;
        static float H(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return (x >> 8) * (1f / 16777216f); }
        static float Frac(float x) => x - MathF.Floor(x);
        float h1 = H(id * 16 + 1), h2 = H(id * 16 + 2), h3 = H(id * 16 + 3), h4 = H(id * 16 + 4), t; Vector3 p, v;
        if (id < JetDroplets)
        {
            t = Frac(time / 2.3f + h1) * 2.3f;
            float a = h3 * 2 * MathF.PI, u = 0.10f + 0.50f * h2 * h2;
            p = f.Nozzle; v = new(MathF.Cos(a) * u, 4.0f + 0.7f * h4, MathF.Sin(a) * u);
        }
        else
        {
            t = Frac(time / 0.8f + h1) * 0.8f;
            float a = ((id - JetDroplets) % 12 + 0.5f + (h3 - 0.5f) * 0.4f) / 12 * 2 * MathF.PI;
            p = new(f.Nozzle.X + MathF.Cos(a) * rim, level + 0.03f, f.Nozzle.Z + MathF.Sin(a) * rim);
            v = new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (0.30f + 0.25f * h2); v.Y = -0.2f;
        }
        float size = 0.016f + 0.012f * h4;
        for (uint k = 0; k < 3; k++)
        {
            float toBowl = (v.Y + MathF.Sqrt(MathF.Max(v.Y * v.Y + 2 * gravity * (p.Y - level), 0))) / gravity;
            var q = new Vector2(p.X + v.X * toBowl - f.Nozzle.X, p.Z + v.Z * toBowl - f.Nozzle.Z); bool bowl = q.LengthSquared() < rim * rim;
            float end = bowl ? toBowl : (v.Y + MathF.Sqrt(MathF.Max(v.Y * v.Y + 2 * gravity * (p.Y - waterLevel), 0))) / gravity;
            if (t <= end) return (p + v * t - new Vector3(0, 0.5f * gravity * t * t, 0), v - new Vector3(0, gravity * t, 0), size);
            if (!bowl || k == 2) break;
            p = new(p.X + v.X * end, level + 0.002f, p.Z + v.Z * end); t -= end; size *= 0.75f;
            float sa = H(id * 16 + 5 + k) * 2 * MathF.PI, su = 0.5f + 0.9f * H(id * 16 + 8 + k);
            v = new(v.X * 0.3f + MathF.Cos(sa) * su, 1.1f + 0.9f * H(id * 16 + 11 + k), v.Z * 0.3f + MathF.Sin(sa) * su);
        }
        return (new(0, -100, 0), default, 0);
    }

    /// <summary>A droplet's ball as it is drawn: of its size, drawn out along its flight (Garden.hlsli's DropletPlace is its twin).
    /// A spent one is a speck far under the ground.</summary>
    public static Matrix4x4 DropletWorld(Vector3 pos, Vector3 vel, float size)
    {
        if (size <= 0) return Matrix4x4.CreateScale(1e-4f) * Matrix4x4.CreateTranslation(0, -100, 0);
        float speed = vel.Length(), k = MathF.Min(speed * 0.22f, 1.6f); var d = speed > 1e-4f ? vel / speed : Vector3.UnitY;
        return new(size * (1 + k * d.X * d.X), size * k * d.X * d.Y, size * k * d.X * d.Z, 0, size * k * d.Y * d.X, size * (1 + k * d.Y * d.Y), size * k * d.Y * d.Z, 0,
                   size * k * d.Z * d.X, size * k * d.Z * d.Y, size * (1 + k * d.Z * d.Z), 0, pos.X, pos.Y, pos.Z, 1);
    }

    /// <summary>A ball of twenty faces, one unit in radius: a droplet's mesh.</summary>
    private static GardenMesh Ball(uint material)
    {
        float phi = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] v = [new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0), new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi), new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1)];
        uint[] indices = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
        var bytes = new byte[v.Length * 16];
        for (int i = 0; i < v.Length; i++)
        {
            var n = Vector3.Normalize(v[i]); var span = bytes.AsSpan(i * 16);
            MemoryMarshal.Write(span, (short)MathF.Round(n.X * 32767)); MemoryMarshal.Write(span[2..], (short)MathF.Round(n.Y * 32767)); MemoryMarshal.Write(span[4..], (short)MathF.Round(n.Z * 32767));
            span[8] = (byte)(sbyte)MathF.Round(n.X * 127); span[9] = (byte)(sbyte)MathF.Round(n.Y * 127); span[10] = (byte)(sbyte)MathF.Round(n.Z * 127);
        }
        return new GardenMesh(Vector3.Zero, Vector3.One, bytes, indices, [new GardenSubmesh(0, (uint)indices.Length, material)]);
    }

    /// <summary>Where the logo has turned at <paramref name="time"/>: its face (which looks down the garden, -z) toward the camera's eye,
    /// as a cosine and sine about the vertical, and how far it has floated up.</summary>
    public static (float Cos, float Sin, float Lift) LogoTurn(Vector3 pivot, float time)
    {
        var (eye, _) = GardenCamera.At(time);
        var d = new Vector2(eye.X - pivot.X, eye.Z - pivot.Z);
        if (d.LengthSquared() < 1e-6f) d = new Vector2(0, -1);
        d = Vector2.Normalize(d);
        // the face (0, 0, -1) turned is (-sin, 0, -cos): make it point along d
        return (-d.Y, -d.X, 0.12f * MathF.Sin(time * 0.8f));
    }

    /// <summary>The logo's turn and float as a row-vector transform about its pivot (HLSL: x' = c x + s z, z' = -s x + c z, y' = y + lift).</summary>
    public static Matrix4x4 LogoMotion(float c, float s, float lift) => new(c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, lift, 0, 1);

    /// <summary>The logo's pivot (the centre of its placement), for the frame constants; the origin when the scene has no logo.</summary>
    public Vector3 LogoPivot => LogoInstance < 0 ? Vector3.Zero : new(Instances[LogoInstance].Row0.W, Instances[LogoInstance].Row1.W, Instances[LogoInstance].Row2.W);

    private ID3D12Resource UploadTextures(D3D12Session s, GardenScene scene)
    {
        int size = Math.Max(4, scene.TextureSize), count = TextureCount, mips = _textureMips;
        var tex = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.BC3_UNorm_SRgb, (uint)size, (uint)size, (ushort)count, (ushort)mips), ResourceStates.CopyDest));
        // one staging buffer: every subresource at a 512-byte boundary, rows 256-byte aligned, as buffer-to-texture copies require
        var places = new List<(ulong Offset, uint Pitch, int Rows, int Width, byte[] Data)>(); ulong total = 0;
        for (int t = 0; t < count; t++)
            for (int m = 0, w = size; m < mips; m++, w = Math.Max(4, w / 2))
            {
                int blocks = Math.Max(1, w / 4); uint pitch = (uint)((blocks * 16 + 255) & ~255);
                byte[] data = t < scene.Textures.Count ? scene.TextureLevel(t, m) : new byte[blocks * blocks * 16];
                total = (total + 511) & ~511ul; places.Add((total, pitch, blocks, w, data)); total += pitch * (ulong)blocks;
            }
        using var staging = s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(total), ResourceStates.GenericRead);
        var dst = staging.Map<byte>(0, (int)total);
        foreach (var p in places)
            for (int r = 0; r < p.Rows; r++) p.Data.AsSpan(r * Math.Max(1, p.Width / 4) * 16, Math.Max(1, p.Width / 4) * 16).CopyTo(dst[(int)(p.Offset + (ulong)r * p.Pitch)..]);
        staging.Unmap(0);
        s.Run(l =>
        {
            for (int k = 0; k < places.Count; k++)
            {
                var p = places[k];
                var footprint = new PlacedSubresourceFootPrint { Offset = p.Offset, Footprint = new SubresourceFootPrint(Format.BC3_UNorm_SRgb, (uint)p.Width, (uint)p.Width, 1, p.Pitch) };
                l.CopyTextureRegion(new TextureCopyLocation(tex, (uint)k), 0, 0, 0, new TextureCopyLocation(staging, footprint));
            }
            l.ResourceBarrierTransition(tex, ResourceStates.CopyDest, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
        });
        return tex;
    }

    /// <summary>One BC3 image as a texture of a single mip.</summary>
    private static ID3D12Resource UploadImage(D3D12Session s, int width, int height, byte[] bc3)
    {
        var tex = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.BC3_UNorm_SRgb, (uint)width, (uint)height, 1, 1), ResourceStates.CopyDest));
        int across = width / 4, rows = height / 4; uint pitch = (uint)((across * 16 + 255) & ~255);
        using var staging = s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(pitch * (ulong)rows), ResourceStates.GenericRead);
        var dst = staging.Map<byte>(0, (int)(pitch * rows));
        for (int r = 0; r < rows; r++) bc3.AsSpan(r * across * 16, across * 16).CopyTo(dst[(int)(r * pitch)..]);
        staging.Unmap(0);
        s.Run(l =>
        {
            var footprint = new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.BC3_UNorm_SRgb, (uint)width, (uint)height, 1, pitch) };
            l.CopyTextureRegion(new TextureCopyLocation(tex, 0), 0, 0, 0, new TextureCopyLocation(staging, footprint));
            l.ResourceBarrierTransition(tex, ResourceStates.CopyDest, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
        });
        return tex;
    }

    public ShaderResourceViewDescription TextureView => new()
    {
        Format = Format.BC3_UNorm_SRgb, ViewDimension = ShaderResourceViewDimension.Texture2DArray, Shader4ComponentMapping = ShaderComponentMapping.Default,
        Texture2DArray = new Texture2DArrayShaderResourceView { MipLevels = (uint)_textureMips, ArraySize = (uint)TextureCount }
    };

    /// <summary>The owner's model in the logo's place and size: turned to face down the garden as the logo does, as wide as it.</summary>
    private static GardenMesh FromModel(SceneModel model, GardenMesh logo)
    {
        float scale = logo.Extent.X * 2 / SceneModel.Width; var mat = logo.Submeshes[0].Material;
        var pos = model.Vertices.Select(v => new Vector3(-v.Position.X, v.Position.Y, -v.Position.Z) * scale + logo.Centre).ToArray();
        Vector3 lo = pos.Aggregate(new Vector3(float.MaxValue), Vector3.Min), hi = pos.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        Vector3 centre = (lo + hi) / 2, extent = Vector3.Max((hi - lo) / 2, new Vector3(1e-4f));
        var bytes = new byte[pos.Length * 16];
        for (int i = 0; i < pos.Length; i++)
        {
            var q = (pos[i] - centre) / extent * 32767; var n = model.Vertices[i].Normal; n = new Vector3(-n.X, n.Y, -n.Z);
            var span = bytes.AsSpan(i * 16);
            MemoryMarshal.Write(span, (short)Math.Round(q.X)); MemoryMarshal.Write(span[2..], (short)Math.Round(q.Y)); MemoryMarshal.Write(span[4..], (short)Math.Round(q.Z));
            span[8] = (byte)(sbyte)Math.Round(n.X * 127); span[9] = (byte)(sbyte)Math.Round(n.Y * 127); span[10] = (byte)(sbyte)Math.Round(n.Z * 127);
        }
        var indices = Enumerable.Range(0, pos.Length).Select(i => (uint)i).ToArray();
        return new GardenMesh(centre, extent, bytes, indices, [new GardenSubmesh(0, (uint)indices.Length, mat)]);
    }
}

/// <summary>The per-pass constants both renderers bind at b1 (Garden.hlsli's Frame).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GardenFrame
{
    public Matrix4x4 ViewProj, ShadowViewProj;
    public Vector3 Eye; public float Time;
    public Vector3 SunDir; public float SunOn;
    public Vector3 SunColor; public uint LightCount;
    public Vector3 SkyZenith; public float WaterLevel;
    public Vector3 SkyHorizon; public float Exposure;
    public Vector3 GroundColor; public uint Flags;
    public Vector2 ViewSize; public float ShadowTexel; public uint ShadowTaps;
    public Vector3 CamRight; public float TanHalfFovY;
    public Vector3 CamUp; public float Aspect;
    public Vector3 CamForward; public uint Bounces;
    public uint Width, Height, Pitch, Mode;
    public Vector4 Logo;
    public uint Samples, Pad0, Pad1, Pad2;
    public Vector4 Backdrop;
    public Matrix4x4 SkyViewProj;
    public Vector4 Fountain, Fountain2, Ambience, Lens, Post, Grid, Grid2;
    public Vector4 Round0, Round0Low, Round0High, Round1, Round1Low, Round1High;

    /// <summary>The near plane. Depth is reversed and the far plane infinitely far: 1 here, falling to 0 with distance, which a
    /// floating-point depth buffer keeps apart to the millimetre across the whole garden.</summary>
    public const float Near = 0.1f;

    /// <summary>The camera, sky and key light of <paramref name="g"/>'s scene at <paramref name="time"/>, seen at <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static GardenFrame For(GardenGpu g, float time, int width, int height)
    {
        var (eye, target) = GardenCamera.At(time);
        float fovY = 1.0f, aspect = width / (float)height;
        var forward = Vector3.Normalize(target - eye); var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward)); var up = Vector3.Cross(forward, right);
        var view = Matrix4x4.CreateLookAtLeftHanded(eye, target, Vector3.UnitY);
        float h = 1 / MathF.Tan(fovY / 2);
        var proj = new Matrix4x4(h / aspect, 0, 0, 0, 0, h, 0, 0, 0, 0, 0, 1, 0, 0, Near, 0);   // depth = Near / distance
        bool raster = g.Mode == GardenScene.Mode.Raster;
        var f = new GardenFrame
        {
            ViewProj = view * proj, Eye = eye, Time = time, SunDir = raster ? g.SunDirection : MoonAt(g.SunDirection, time), SunOn = g.SunColor.LengthSquared() > 0 ? 1 : 0, SunColor = g.SunColor,
            LightCount = (uint)g.PointLights.Length, WaterLevel = g.WaterLevel,
            // a low golden sun for Direct3D, nightfall for the ray tracer: two skies, as simple gradients
            // (by day the exposure is set for the sunlit stone, and the sky's own light is a fraction of the sun's, as it is with the
            // sun this low: what it does not reach stays in real shade)
            SkyZenith = raster ? new(0.13f, 0.22f, 0.46f) : new(0.012f, 0.022f, 0.070f),
            SkyHorizon = raster ? new(0.62f, 0.46f, 0.32f) : new(0.10f, 0.085f, 0.17f),
            GroundColor = raster ? new(0.20f, 0.16f, 0.12f) : new(0.020f, 0.018f, 0.025f),
            Exposure = raster ? 1.9f : 1.45f,
            ViewSize = new(width, height), CamRight = right, CamUp = up, CamForward = forward, TanHalfFovY = MathF.Tan(fovY / 2), Aspect = aspect,
            Bounces = 4, Width = (uint)width, Height = (uint)height, Mode = raster ? 1u : 2u, Backdrop = g.BackdropRange
        };
        if (g.Fountain is { } fountain) { f.Fountain = new(fountain.Nozzle, fountain.BowlRadius); f.Fountain2 = new(fountain.BowlLevel, fountain.RimRadius, GardenGpu.JetDroplets, 1); }
        f.Ambience = new(0, 0, Near, 0);
        // The lens is focused on what the walk is looking at, and wide enough open that the far end of the garden and the hills go
        // soft from the terrace, and a door's leaf an arm's length away from the middle of a room: blur in pixels = 2.2 % of the
        // frame's height for each dioptre out of focus, and never more than 0.7 % of it.
        f.Lens = new(Math.Clamp(Vector3.Distance(eye, target), 2.5f, 14f), 0.022f * height, 0.007f * height, 16);
        f.Post = new(raster ? 0.11f : 0.55f, 0, 1, 0);   // the rasteriser's glow is five levels summed, the ray tracer's one; the wind blows
        var (c, s, lift) = GardenGpu.LogoTurn(g.LogoPivot, time); f.Logo = new(c, s, lift, raster ? 0.3f : 0.45f);   // the logo is a lit sign: it glows of itself, by day a little, at night more
        return f;
    }

    /// <summary>Toward the moon at <paramref name="time"/>: it swings slowly across the sky about where the scene has it, there and back
    /// in one walk of the garden, so the ray-traced shadows of the cypresses, the columns and the lattices creep over the paving.</summary>
    public static Vector3 MoonAt(Vector3 placed, float time)
    {
        float a = 0.42f * MathF.Sin(time / GardenCamera.Loop * 2 * MathF.PI), c = MathF.Cos(a), s = MathF.Sin(a);
        return Vector3.Normalize(new(c * placed.X + s * placed.Z, placed.Y * (1 + 0.25f * MathF.Cos(time / GardenCamera.Loop * 2 * MathF.PI)), -s * placed.X + c * placed.Z));
    }

    /// <summary>The same frame seen in the pool: the camera mirrored in the water's plane, drawing only what is above it.</summary>
    public readonly GardenFrame Mirrored()
    {
        var f = this; float h = WaterLevel;
        var mirror = new Matrix4x4(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 2 * h, 0, 1);
        f.ViewProj = mirror * ViewProj; f.Eye = new(Eye.X, 2 * h - Eye.Y, Eye.Z);
        f.CamForward = new(CamForward.X, -CamForward.Y, CamForward.Z); f.CamUp = new(CamUp.X, -CamUp.Y, CamUp.Z); f.CamRight = new(CamRight.X, -CamRight.Y, CamRight.Z);
        f.Flags |= 2; f.Flags &= ~1u;
        return f;
    }
}
