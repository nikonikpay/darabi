using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The light volume's baker on the GPU (GardenBake.hlsl, a compute shader sending rays through <see cref="GardenAccel"/>): for
/// <see cref="GardenLightBaker"/>, which says where and under which light. Never used by the app's own tests.
/// </summary>
internal sealed class GardenLightTracer
{
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Pass(uint Step, uint Last, uint Stage = 0, uint Keep = 0);
    /// <summary>What a pass of the baker changes in the frame's constants: which light shines.</summary>
    public delegate void FrameSetup(ref GardenFrame frame);
    /// <summary>How many entries of the light volume's list one row of a dispatch does (GardenBake.hlsl's BakeRow).</summary>
    public const int BakeRow = 1024;

    private readonly D3D12Session _s; private readonly GardenGpu _g; private readonly GardenAccel _accel; private readonly int _rows;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _bake; private readonly ID3D12Resource _found, _constants; private readonly ID3D12DescriptorHeap _srv;

    /// <summary>A baker with room for <paramref name="entries"/> entries (a grid point's direction each).</summary>
    public GardenLightTracer(D3D12Session s, GardenGpu g, int entries)
    {
        _s = s; _g = g; _accel = new GardenAccel(s, g); _rows = (entries + BakeRow - 1) / BakeRow / 8 * 8 + 8;
        byte[] cs = D3D12Session.Shader("GardenBake");
        _root = s.Own(s.Device.CreateRootSignature(cs));
        _bake = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = cs }));
        _found = s.UavBuffer((ulong)BakeRow * (ulong)_rows * 16);
        _constants = s.Buffer(GardenFrame.Bytes, HeapType.Upload, ResourceStates.GenericRead);
        _srv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 2, DescriptorHeapFlags.ShaderVisible, 0)));
        s.Device.CreateShaderResourceView(g.Textures, g.TextureView, _srv.GetCPUDescriptorHandleForHeapStart());
        s.Device.CreateShaderResourceView(g.Backdrop, null, _srv.GetCPUDescriptorHandleForHeapStart().Offset(1, s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView)));
    }

    /// <summary>The light at each point of a grid, for each of six directions, as four floats an entry (red, green, blue, and the
    /// share of its rays that met the back of a surface). The scene as it stands at time 0, lit as <paramref name="light"/> leaves
    /// the frame's constants; <paramref name="rays"/> rays an entry, a few rows of entries a submission so no one of them holds the GPU long.</summary>
    public float[] BakeLight(Vector4 grid, Vector3 points, int count, uint rays, FrameSetup light)
    {
        if ((long)BakeRow * _rows < count) throw new InvalidOperationException("The baker has no room for the light volume's list.");
        var frame = GardenFrame.For(_g, 0, BakeRow, _rows); frame.Pitch = BakeRow; frame.Mode = 2;
        light(ref frame); frame.Grid = grid; frame.Grid2 = new(points, 0);
        MemoryMarshal.Write(_constants.Map<byte>(0, GardenFrame.Bytes), in frame); _constants.Unmap(0);
        int rows = (count + BakeRow - 1) / BakeRow;
        for (int row = 0; row < rows; row += 8)
            _s.Run(l =>
            {
                if (row == 0) _accel.Build(l, 0);
                l.SetComputeRootSignature(_root); l.SetDescriptorHeaps(_srv); l.SetPipelineState(_bake);
                l.SetComputeRootConstantBufferView(0, _constants.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(1, _accel.Address);
                l.SetComputeRootShaderResourceView(2, _g.InstanceBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(3, _g.MaterialBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(4, _g.LightBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(5, _g.MeshInfoBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(6, _g.SubmeshInfoBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(7, _g.VertexBuffer.GPUVirtualAddress);
                l.SetComputeRootShaderResourceView(8, _g.IndexBuffer.GPUVirtualAddress);
                l.SetComputeRootDescriptorTable(9, _srv.GetGPUDescriptorHandleForHeapStart());
                l.SetComputeRootUnorderedAccessView(10, _found.GPUVirtualAddress);
                l.SetComputeRoot32BitConstants(11, new Pass((uint)row, rays), 0);
                l.Dispatch(BakeRow / 8, 1, 1);
            });
        var raw = _s.Read(count * 4, (l, readback) =>
        {
            l.ResourceBarrierTransition(_found, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
            l.CopyBufferRegion(readback, 0, _found, 0, (ulong)count * 16);
            l.ResourceBarrierTransition(_found, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
        });
        return MemoryMarshal.Cast<uint, float>(raw).ToArray();
    }
}
