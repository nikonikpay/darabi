using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Draws the garden with DirectX Raytracing (GardenRay.hlsl): a bottom-level acceleration structure per mesh (one geometry per material,
/// leaf cards non-opaque so the shader cuts them out), and a top-level one over every placed mesh, rebuilt each frame so the logo can move.
/// Instances made only of water, glass and glowing parts are left out of shadow rays (instance mask), so lanterns light through their glass.
/// </summary>
internal sealed unsafe class GardenRay : GardenRenderer
{
    [StructLayout(LayoutKind.Sequential, Size = 64)] private struct InstanceDesc { public fixed float Transform[12]; public uint IdAndMask, OffsetAndFlags; public ulong Blas; }

    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline;
    private readonly ID3D12Resource _tlas, _tlasScratch, _instances, _pixels, _constants; private readonly uint _pitch;
    private readonly ulong[] _blas; private readonly byte[] _masks; private readonly Matrix4x4[] _dequant;
    private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;
    private readonly ID3D12DescriptorHeap _srv;
    public int Bounces { get; } = 4;
    /// <summary>Camera rays a pixel: each with its own soft-shadow rays to every lamp and its own bounced-light ray.</summary>
    public int Samples { get; } = 4;

    public GardenRay(D3D12Session s, GardenGpu g, int width, int height, ID3D12Resource[] targets) : base(s, g, width, height, targets)
    {
        byte[] cs = D3D12Session.Shader("GardenRay");
        _root = s.Own(s.Device.CreateRootSignature(cs));
        _pipeline = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = cs }));
        _pitch = ((uint)width + 63) & ~63u;
        _pixels = s.UavBuffer((ulong)_pitch * (ulong)height * 4);
        _constants = s.Buffer(512, HeapType.Upload, ResourceStates.GenericRead);

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
            _masks[d.Mesh] = (byte)(1 | (g.CastsShadow(d) ? 2 : 0));
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

        _srv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible, 0)));
        s.Device.CreateShaderResourceView(g.Textures, g.TextureView, _srv.GetCPUDescriptorHandleForHeapStart());
    }

    private static ulong Align(ulong n) => (n + 255) & ~255ul;

    /// <summary>The TLAS instances: every one at build time, afterwards only the moving logo.</summary>
    private void WriteInstances(float time, bool all)
    {
        var span = _instances.Map<InstanceDesc>(0, G.Instances.Length);
        for (int i = 0; i < G.Instances.Length; i++)
        {
            if (!all && i != G.LogoInstance) continue;
            var inst = G.Instances[i];
            var m = _dequant[inst.Mesh] * G.World(i, time);
            var desc = new InstanceDesc { IdAndMask = (uint)i & 0xFFFFFF | (uint)_masks[inst.Mesh] << 24, Blas = _blas[inst.Mesh] };
            float[] rows = [m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43];
            for (int k = 0; k < 12; k++) desc.Transform[k] = rows[k];
            span[i] = desc;
        }
        _instances.Unmap(0);
    }

    private void Trace(ID3D12GraphicsCommandList4 l, float time)
    {
        var frame = GardenFrame.For(G, time, Width, Height); frame.Pitch = _pitch; frame.Bounces = (uint)Bounces; frame.Samples = (uint)Samples;
        MemoryMarshal.Write(_constants.Map<byte>(0, 512), in frame); _constants.Unmap(0);
        l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _tlasScratch.GPUVirtualAddress));
        l.ResourceBarrierUnorderedAccessView(_tlas);
        l.SetPipelineState(_pipeline); l.SetComputeRootSignature(_root); l.SetDescriptorHeaps(_srv);
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
        l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
        l.ResourceBarrierUnorderedAccessView(_pixels);
    }

    protected override void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target)
    {
        if (G.LogoInstance >= 0) WriteInstances(time, all: false);   // safe: the previous submission has finished (every Run waits for the GPU)
        Trace(l, time);
        var dest = Targets[target];
        l.ResourceBarrierTransition(_pixels, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        l.ResourceBarrierTransition(dest, ResourceStates.Present, ResourceStates.CopyDest);
        l.CopyTextureRegion(new TextureCopyLocation(dest, 0), 0, 0, 0,
            new TextureCopyLocation(_pixels, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, _pitch * 4) }));
        l.ResourceBarrierTransition(dest, ResourceStates.CopyDest, ResourceStates.Present);
        l.ResourceBarrierTransition(_pixels, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
    }
}
