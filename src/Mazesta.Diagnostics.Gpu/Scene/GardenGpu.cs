using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The garden on the GPU for one of its two scenes: every mesh in one vertex and one index buffer, the instances of that scene grouped by
/// mesh (so each mesh is one instanced draw per material), the materials, the lights, the textures (base colours and normal maps) as one
/// BC3 texture array, and the mountains round the horizon.
/// Both renderers build on it. The owner's Models\gpu-test.obj, when there is one, takes the logo's place over the pool.
/// </summary>
internal sealed unsafe class GardenGpu
{
    [StructLayout(LayoutKind.Sequential)] public struct Instance { public Vector4 Row0, Row1, Row2; public uint Mesh, Mask, Flags, Pad; }
    [StructLayout(LayoutKind.Sequential)] public struct Light { public Vector3 Position; public uint Kind; public Vector3 Direction; public float Range; public Vector3 Color; public float CosOuter; public float CosInner, Radius, Pad0, Pad1; }
    [StructLayout(LayoutKind.Sequential)] public struct MeshInfo { public Vector3 Centre; public uint FirstSubmesh; public Vector3 Extent; public uint BaseVertex; }
    [StructLayout(LayoutKind.Sequential)] public struct SubmeshInfo { public uint IndexStart, Material, Opaque, Pad; }
    public readonly record struct Part(uint IndexStart, uint IndexCount, uint Material, GardenMaterialKind Kind);
    /// <summary>One mesh's draws: its instances are [FirstInstance, +InstanceCount) of <see cref="Instances"/>.</summary>
    public sealed record Draw(int Mesh, uint FirstInstance, uint InstanceCount, int BaseVertex, uint VertexCount, Vector3 Centre, Vector3 Extent, Part[] Parts);

    public const float WaterLevel = -0.04f;   // the main pool's surface (courtyard-v4.blend: z -0.04)
    public GardenScene.Mode Mode { get; }
    public Instance[] Instances { get; }
    public Draw[] Draws { get; }
    public GardenMaterial[] Materials { get; }
    public Light[] PointLights { get; }
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
        Mode = mode; ModelProblem = customProblem;
        var meshes = scene.Meshes.ToList(); var materials = scene.Materials.ToList();
        var chosen = scene.Instances.Where(i => (i.Mask & (uint)mode) != 0).ToArray();
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
        LogoInstance = Array.FindIndex(Instances, i => (i.Flags & GardenScene.LogoFlag) != 0);
        var draws = new List<Draw>();
        for (int k = 0; k < order.Length;)
        {
            int m = (int)order[k].Mesh, n = 0; while (k + n < order.Length && order[k + n].Mesh == m) n++;
            var mesh = meshes[m];
            draws.Add(new Draw(m, (uint)k, (uint)n, baseVertex[m], (uint)mesh.VertexCount, mesh.Centre, mesh.Extent,
                [.. mesh.Submeshes.Select(sub => new Part(baseIndex[m] + sub.IndexStart, sub.IndexCount, sub.Material, materials[(int)sub.Material].Kind))]));
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
            points.Add(new Light
            {
                Position = l.Position, Kind = (uint)l.Kind, Direction = Vector3.Normalize(l.Direction), Range = range, Color = intensity, Radius = Math.Max(0.05f, l.Radius),
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

    /// <summary>Whether a mesh blocks light: one made only of water, glass and glowing parts does not (a lantern's glass would otherwise hide its own lamp).</summary>
    public bool CastsShadow(Draw d) => d.Parts.Any(p => p.Kind is GardenMaterialKind.Flat or GardenMaterialKind.Cutout or GardenMaterialKind.Brick);

    /// <summary>Instance <paramref name="i"/>'s world transform at <paramref name="time"/>: the logo turns to the camera and floats (Garden.hlsli's LogoMove is its twin).</summary>
    public Matrix4x4 World(int i, float time)
    {
        var inst = Instances[i];
        var m = new Matrix4x4(inst.Row0.X, inst.Row1.X, inst.Row2.X, 0, inst.Row0.Y, inst.Row1.Y, inst.Row2.Y, 0, inst.Row0.Z, inst.Row1.Z, inst.Row2.Z, 0, inst.Row0.W, inst.Row1.W, inst.Row2.W, 1);
        if ((inst.Flags & GardenScene.LogoFlag) == 0) return m;
        var pivot = new Vector3(inst.Row0.W, inst.Row1.W, inst.Row2.W);
        var (c, s, lift) = LogoTurn(pivot, time);
        return m * Matrix4x4.CreateTranslation(-pivot) * LogoMotion(c, s, lift) * Matrix4x4.CreateTranslation(pivot);
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
                byte[] data = t < scene.Textures.Count ? scene.Textures[t][m] : new byte[blocks * blocks * 16];
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

    /// <summary>The camera, sky and key light of <paramref name="g"/>'s scene at <paramref name="time"/>, seen at <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static GardenFrame For(GardenGpu g, float time, int width, int height)
    {
        var (eye, target) = GardenCamera.At(time);
        float fovY = 1.0f, aspect = width / (float)height;
        var forward = Vector3.Normalize(target - eye); var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward)); var up = Vector3.Cross(forward, right);
        var view = Matrix4x4.CreateLookAtLeftHanded(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(fovY, aspect, 0.08f, 250f);
        bool raster = g.Mode == GardenScene.Mode.Raster;
        var f = new GardenFrame
        {
            ViewProj = view * proj, Eye = eye, Time = time, SunDir = g.SunDirection, SunOn = g.SunColor.LengthSquared() > 0 ? 1 : 0, SunColor = g.SunColor,
            LightCount = (uint)g.PointLights.Length, WaterLevel = GardenGpu.WaterLevel,
            // golden hour for Direct3D, blue hour for the ray tracer: the .blend's two skies, as simple gradients
            SkyZenith = raster ? new(0.20f, 0.34f, 0.70f) : new(0.012f, 0.022f, 0.070f),
            SkyHorizon = raster ? new(0.95f, 0.70f, 0.48f) : new(0.10f, 0.085f, 0.17f),
            GroundColor = raster ? new(0.30f, 0.24f, 0.18f) : new(0.020f, 0.018f, 0.025f),
            Exposure = raster ? 1.05f : 1.6f,
            ViewSize = new(width, height), CamRight = right, CamUp = up, CamForward = forward, TanHalfFovY = MathF.Tan(fovY / 2), Aspect = aspect,
            Bounces = 4, Width = (uint)width, Height = (uint)height, Mode = raster ? 1u : 2u, Backdrop = g.BackdropRange
        };
        var (c, s, lift) = GardenGpu.LogoTurn(g.LogoPivot, time); f.Logo = new(c, s, lift, 0);
        return f;
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
