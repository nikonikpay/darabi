using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Draws the garden with DirectX Raytracing (GardenRay.hlsl): a bottom-level acceleration structure per mesh (one geometry per material,
/// leaf cards non-opaque so the shader cuts them out), and a top-level one over every placed mesh, rebuilt each frame so the logo, the
/// mirror sphere and the fountain's droplets can move and the plants lean with the wind.
/// The garden's day goes by as the walk does (<see cref="GardenDay"/>): the sun's shadows cross the paving, its light comes in at
/// the hall's stained windows, the lamps are lit at dusk and the moon takes over.
/// Instances made only of water, glass and glowing parts are left out of shadow rays (instance mask), so lanterns light through their
/// glass; the stained panes have a mask bit of their own, by which the shader finds the colour light takes on passing through them.
/// A few rays a pixel leave speckle, which two filters take out: across the frames being shown (each frame's light is added to what
/// the frames before gathered for the same surface, found again through the last frame's view) and then across each frame's own pixels.
/// A frame drawn on its own - a check frame - has only the second, and is the same bits whenever it is drawn.
/// </summary>
internal sealed unsafe class GardenRay : GardenRenderer
{
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Pass(uint Step, uint Last, uint Stage = 0, uint Keep = 0);
    [StructLayout(LayoutKind.Sequential, Size = 64)] private struct InstanceDesc { public fixed float Transform[12]; public uint IdAndMask, OffsetAndFlags; public ulong Blas; }

    /// <summary>What a pass of the light volume's baker changes in the frame's constants: which light shines.</summary>
    public delegate void FrameSetup(ref GardenFrame frame);
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline, _denoise, _finish, _gather; private ID3D12PipelineState? _bake;
    private readonly ID3D12Resource _tlas, _tlasScratch, _instances, _pixels, _constants, _ping, _pong, _guide, _prevGuide, _prevLight; private readonly uint _pitch;
    private Matrix4x4 _prevViewProj; private Vector3 _prevEye; private bool _hasPrev;
    private readonly ulong[] _blas; private readonly byte[] _masks; private readonly Matrix4x4[] _dequant;
    private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;
    private readonly ID3D12DescriptorHeap _srv;
    public int Bounces { get; } = 4;
    /// <summary>Camera rays a pixel: each with its own soft-shadow rays to every lamp and its own bounced-light ray.</summary>
    public int Samples { get; }
    /// <summary>The denoiser's passes (taps 1, 2, 4, 8 pixels apart): an edge-aware filter over this frame's light alone. An even
    /// number: the last one leaves the frame's light in the first of the two buffers, where the lens's passes look for it.</summary>
    public int DenoisePasses { get; } = 4;

    public GardenRay(D3D12Session s, GardenGpu g, int width, int height, ID3D12Resource[] targets, int samples = 4) : base(s, g, width, height, targets)
    {
        Samples = samples;
        byte[] cs = D3D12Session.Shader("GardenRay");
        _root = s.Own(s.Device.CreateRootSignature(cs));
        _pipeline = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = cs }));
        _denoise = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = D3D12Session.Shader("GardenDenoise") }));
        _gather = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = D3D12Session.Shader("GardenGather") }));
        _finish = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = D3D12Session.Shader("GardenFinish") }));
        _pitch = ((uint)width + 63) & ~63u;
        _pixels = s.UavBuffer((ulong)_pitch * (ulong)height * 4);
        ulong px = (ulong)width * (ulong)height * 16;
        _ping = s.UavBuffer(px); _pong = s.UavBuffer(px); _guide = s.UavBuffer(px * 2);
        _prevGuide = s.UavBuffer(px); _prevLight = s.UavBuffer(px);   // the frame shown before: what each pixel met, and the light gathered for it so far
        _constants = s.Buffer(GardenFrame.Bytes, HeapType.Upload, ResourceStates.GenericRead);

        // one BLAS per mesh drawn, built together: results and scratch each packed into one buffer
        int meshCount = g.Draws.Max(d => d.Mesh) + 1;
        _blas = new ulong[meshCount]; _masks = new byte[meshCount]; _dequant = new Matrix4x4[meshCount];
        var inputs = new List<(GardenGpu.Draw Draw, BuildRaytracingAccelerationStructureInputs Inputs, ulong Result, ulong Scratch)>();
        ulong resultBytes = 0, scratchBytes = 0;
        foreach (var d in g.Draws)
        {
            var geometries = d.Parts.Select(p => new RaytracingGeometryDescription(
                new RaytracingGeometryTrianglesDescription(new GpuVirtualAddressAndStride(g.VertexBuffer.GPUVirtualAddress + (ulong)d.BaseVertex * 16, 16), Format.R16G16B16A16_SNorm, d.VertexCount,
                    0, g.IndexBuffer.GPUVirtualAddress + (ulong)p.IndexStart * 4, Format.R32_UInt, p.IndexCount),
                p.Kind == GardenMaterialKind.Cutout ? RaytracingGeometryFlags.None : RaytracingGeometryFlags.Opaque)).ToArray();
            var bi = new BuildRaytracingAccelerationStructureInputs
            {
                Type = RaytracingAccelerationStructureType.BottomLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace, Layout = ElementsLayout.Array,
                DescriptorsCount = (uint)geometries.Length, GeometryDescriptions = geometries
            };
            var sizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(bi);
            inputs.Add((d, bi, resultBytes, scratchBytes));
            resultBytes += Align(sizes.ResultDataMaxSizeInBytes); scratchBytes += Align(sizes.ScratchDataSizeInBytes);
            _masks[d.Mesh] = (byte)(1 | (g.CastsShadow(d) ? 2 : 0) | (d.Parts.Any(p => g.Materials[p.Material].LightTint > 0) ? 4 : 0));
            // the BLAS holds the quantised positions: world x dequantise (centre + extent * q)
            _dequant[d.Mesh] = Matrix4x4.CreateScale(d.Extent) * Matrix4x4.CreateTranslation(d.Centre);
        }
        var results = s.Buffer(resultBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
        using (s.Scope())
        {
            var scratch = s.UavBuffer(scratchBytes);
            s.Run(l =>
            {
                foreach (var (d, bi, result, scr) in inputs)
                    l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(results.GPUVirtualAddress + result, bi, 0, scratch.GPUVirtualAddress + scr));
                l.ResourceBarrierUnorderedAccessView(results);
            });
        }
        foreach (var (d, _, result, _) in inputs) _blas[d.Mesh] = results.GPUVirtualAddress + result;

        _instances = s.Buffer((ulong)g.Instances.Length * 64, HeapType.Upload, ResourceStates.GenericRead);
        WriteInstances(0, all: true);
        _tlasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.TopLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace, Layout = ElementsLayout.Array,
            DescriptorsCount = (uint)g.Instances.Length, InstanceDescriptions = _instances.GPUVirtualAddress
        };
        var tsizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(_tlasInputs);
        _tlas = s.Buffer(tsizes.ResultDataMaxSizeInBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
        _tlasScratch = s.UavBuffer(tsizes.ScratchDataSizeInBytes);

        _srv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 2, DescriptorHeapFlags.ShaderVisible, 0)));
        s.Device.CreateShaderResourceView(g.Textures, g.TextureView, _srv.GetCPUDescriptorHandleForHeapStart());
        s.Device.CreateShaderResourceView(g.Backdrop, null, _srv.GetCPUDescriptorHandleForHeapStart().Offset(1, s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView)));
    }

    private static ulong Align(ulong n) => (n + 255) & ~255ul;

    /// <summary>How many entries of the light volume's list one row of a baking dispatch does (GardenRay.hlsl's BakeRow): a renderer
    /// made this wide, and high enough, has room for the list in its buffers.</summary>
    public const int BakeRow = 1024;

    /// <summary>For <see cref="GardenLightBaker"/>: the light at each point of a grid, for each of six directions, as four floats an
    /// entry (red, green, blue, and the share of its rays that met the back of a surface). The scene is this renderer's own, as it
    /// stands at time 0, lit as <paramref name="light"/> leaves the frame's constants; <paramref name="rays"/> rays an entry, a few
    /// rows of entries a submission so no one of them holds the GPU long.</summary>
    public float[] BakeLight(Vector4 grid, Vector3 points, int count, uint rays, FrameSetup light)
    {
        if (Width != BakeRow || (long)Width * Height < count) throw new InvalidOperationException("The renderer is not the size the light volume's list needs.");
        var bake = _bake ??= S.Own(S.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = D3D12Session.Shader("GardenBake") }));
        var frame = GardenFrame.For(G, 0, Width, Height); frame.Pitch = _pitch; frame.Bounces = (uint)Bounces; frame.Samples = 1;
        light(ref frame); frame.Grid = grid; frame.Grid2 = new(points, 0);
        MemoryMarshal.Write(_constants.Map<byte>(0, GardenFrame.Bytes), in frame); _constants.Unmap(0);
        WriteInstances(0, all: true);
        int rows = (count + BakeRow - 1) / BakeRow;
        for (int row = 0; row < rows; row += 8)
            S.Run(l =>
            {
                if (row == 0) { l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _tlasScratch.GPUVirtualAddress)); l.ResourceBarrierUnorderedAccessView(_tlas); }
                Bind(l); l.SetPipelineState(bake);
                l.SetComputeRoot32BitConstants(14, new Pass((uint)row, rays), 0);
                l.Dispatch(BakeRow / 8, 1, 1);
            });
        var raw = S.Read(count * 4, (l, readback) =>
        {
            l.ResourceBarrierTransition(_ping, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
            l.CopyBufferRegion(readback, 0, _ping, 0, (ulong)count * 16);
            l.ResourceBarrierTransition(_ping, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
        });
        return MemoryMarshal.Cast<uint, float>(raw).ToArray();
    }

    private void Bind(ID3D12GraphicsCommandList4 l)
    {
        l.SetComputeRootSignature(_root); l.SetDescriptorHeaps(_srv);
        l.SetComputeRootConstantBufferView(0, _constants.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(1, _tlas.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(2, G.InstanceBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(3, G.MaterialBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(4, G.LightBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(5, G.MeshInfoBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(6, G.SubmeshInfoBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(7, G.VertexBuffer.GPUVirtualAddress);
        l.SetComputeRootShaderResourceView(8, G.IndexBuffer.GPUVirtualAddress);
        l.SetComputeRootDescriptorTable(9, _srv.GetGPUDescriptorHandleForHeapStart());
        l.SetComputeRootUnorderedAccessView(10, _pixels.GPUVirtualAddress);
        l.SetComputeRootUnorderedAccessView(11, _ping.GPUVirtualAddress);
        l.SetComputeRootUnorderedAccessView(12, _guide.GPUVirtualAddress);
        l.SetComputeRootUnorderedAccessView(13, _pong.GPUVirtualAddress);
        l.SetComputeRootUnorderedAccessView(15, _prevGuide.GPUVirtualAddress);
        l.SetComputeRootUnorderedAccessView(16, _prevLight.GPUVirtualAddress);
    }

    /// <summary>The TLAS instances: every one at build time, afterwards only those that move.</summary>
    private void WriteInstances(float time, bool all)
    {
        var span = _instances.Map<InstanceDesc>(0, G.Instances.Length);
        foreach (int i in all ? Enumerable.Range(0, G.Instances.Length) : G.Moving.Concat(G.Swaying))
        {
            var inst = G.Instances[i];
            var m = _dequant[inst.Mesh] * G.World(i, time);
            var desc = new InstanceDesc { IdAndMask = (uint)i & 0xFFFFFF | (uint)_masks[inst.Mesh] << 24, Blas = _blas[inst.Mesh] };
            float[] rows = [m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43];
            for (int k = 0; k < 12; k++) desc.Transform[k] = rows[k];
            span[i] = desc;
        }
        _instances.Unmap(0);
    }

    private void Trace(ID3D12GraphicsCommandList4 l, float time, bool live)
    {
        var frame = GardenFrame.For(G, time, Width, Height); frame.Pitch = _pitch; frame.Bounces = (uint)Bounces; frame.Samples = (uint)Samples;
        frame.PrevViewProj = _prevViewProj; frame.PrevEye = new(_prevEye, live && _hasPrev ? 1 : 0);
        if (live) { _prevViewProj = frame.ViewProj; _prevEye = frame.Eye; _hasPrev = true; }
        MemoryMarshal.Write(_constants.Map<byte>(0, GardenFrame.Bytes), in frame); _constants.Unmap(0);
        l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _tlasScratch.GPUVirtualAddress));
        l.ResourceBarrierUnorderedAccessView(_tlas);
        l.SetPipelineState(_pipeline); Bind(l);
        l.SetComputeRoot32BitConstants(14, new Pass(0, 0), 0);
        l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
        l.ResourceBarrierUnorderedAccessView(_ping); l.ResourceBarrierUnorderedAccessView(_guide);
        if (live)
        {   // this frame's light added to what the frames before gathered for the same surfaces
            l.SetPipelineState(_gather); l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
            l.ResourceBarrierUnorderedAccessView(_ping);
        }
        l.SetPipelineState(_denoise);
        for (int k = 0; k < DenoisePasses; k++)   // Ping -> Pong -> Ping ...; the last pass leaves the frame's light, surface colour and all
        {
            l.SetComputeRoot32BitConstants(14, new Pass((uint)(1 << k) | (k % 2 == 1 ? 1u << 16 : 0), k == DenoisePasses - 1 ? 1u : 0, 0, live && k == 0 ? 1u : 0), 0);   // the first pass of a shown frame also keeps it for the next
            l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
            l.ResourceBarrierUnorderedAccessView(_ping); l.ResourceBarrierUnorderedAccessView(_pong);
            if (live && k == 0) { l.ResourceBarrierUnorderedAccessView(_prevGuide); l.ResourceBarrierUnorderedAccessView(_prevLight); }
        }
        // the lens: the glow at a quarter of the size (its bright part, then three blurs across and down), then the picture
        l.SetPipelineState(_finish);
        for (uint stage = 0; stage <= 7; stage++)
        {
            l.SetComputeRoot32BitConstants(14, new Pass(0, 0, stage), 0);
            if (stage < 7) l.Dispatch((((uint)Width + 3) / 4 + 7) / 8, (((uint)Height + 3) / 4 + 7) / 8, 1); else l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
            l.ResourceBarrierUnorderedAccessView(_pong);
        }
        l.ResourceBarrierUnorderedAccessView(_pixels);
    }

    protected override void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target, bool live)
    {
        if (G.Moving.Length + G.Swaying.Length > 0) WriteInstances(time, all: false);   // safe: the previous submission has finished (every Run waits for the GPU)
        Trace(l, time, live);
        var dest = Targets[target];
        l.ResourceBarrierTransition(_pixels, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        l.ResourceBarrierTransition(dest, ResourceStates.Present, ResourceStates.CopyDest);
        l.CopyTextureRegion(new TextureCopyLocation(dest, 0), 0, 0, 0,
            new TextureCopyLocation(_pixels, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, _pitch * 4) }));
        l.ResourceBarrierTransition(dest, ResourceStates.CopyDest, ResourceStates.Present);
        l.ResourceBarrierTransition(_pixels, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
    }
}
