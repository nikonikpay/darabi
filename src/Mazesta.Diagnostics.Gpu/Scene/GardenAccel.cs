using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The garden as the GPU's ray-tracing hardware sees it (DirectX Raytracing): a bottom-level acceleration structure per mesh (one
/// geometry per material, leaf cards non-opaque so the shader cuts them out), and a top-level one over every placed mesh, built anew
/// each frame so the logo, the fountain's droplets and the garden's small life can move and the plants lean with the wind.
/// Instances made only of water, glass and glowing parts are left out of shadow rays (instance mask), so lanterns light through their
/// glass; the stained panes have a mask bit of their own, by which a shader finds the colour light takes on passing through them.
/// Whoever sends rays builds on it: the Direct3D test with ray tracing switched on (<see cref="GardenRaster"/>) and the light
/// volume's baker (<see cref="GardenLightTracer"/>). GardenTrace.hlsli is its shader side.
/// </summary>
internal sealed unsafe class GardenAccel
{
    [StructLayout(LayoutKind.Sequential, Size = 64)] private struct InstanceDesc { public fixed float Transform[12]; public uint IdAndMask, OffsetAndFlags; public ulong Blas; }

    private readonly GardenGpu _g; private readonly ID3D12Resource _tlas, _tlasScratch, _instances;
    private readonly ulong[] _blas; private readonly byte[] _masks; private readonly Matrix4x4[] _dequant;
    private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;

    public GardenAccel(D3D12Session s, GardenGpu g)
    {
        _g = g;
        // one BLAS per mesh drawn, built together: results and scratch each packed into one buffer
        int meshCount = g.Draws.Max(d => d.Mesh) + 1;
        _blas = new ulong[meshCount]; _masks = new byte[meshCount]; _dequant = new Matrix4x4[meshCount];
        var inputs = new List<(GardenGpu.Draw Draw, BuildRaytracingAccelerationStructureInputs Inputs, ulong Result, ulong Scratch)>();
        ulong resultBytes = 0, scratchBytes = 0;
        foreach (var d in g.Draws.Where(d => !d.Weather))
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
            DescriptorsCount = (uint)g.RayInstances, InstanceDescriptions = _instances.GPUVirtualAddress
        };
        var tsizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(_tlasInputs);
        _tlas = s.Buffer(tsizes.ResultDataMaxSizeInBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
        _tlasScratch = s.UavBuffer(tsizes.ScratchDataSizeInBytes);
    }

    private static ulong Align(ulong n) => (n + 255) & ~255ul;

    /// <summary>Where a shader finds the scene: the top-level structure's address, bound as a root view.</summary>
    public ulong Address => _tlas.GPUVirtualAddress;

    /// <summary>The scene as it stands at <paramref name="time"/>: what moves is placed anew (the frame before has been drawn by
    /// then: every submission is waited for) and the top-level structure built again.</summary>
    public void Build(ID3D12GraphicsCommandList4 l, float time)
    {
        if (_g.Moving.Length + _g.Swaying.Length > 0) WriteInstances(time, all: false);
        l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _tlasScratch.GPUVirtualAddress));
        l.ResourceBarrierUnorderedAccessView(_tlas);
    }

    /// <summary>The TLAS instances: every one at first, afterwards only those that move.</summary>
    private void WriteInstances(float time, bool all)
    {
        var span = _instances.Map<InstanceDesc>(0, _g.Instances.Length);
        int[] ids = all ? [.. Enumerable.Range(0, _g.RayInstances)] : _moving ??= [.. _g.Moving.Concat(_g.Swaying)];
        fixed (InstanceDesc* first = span)
        {
            nint at = (nint)first;   // (a pointer cannot be captured: the instances are placed on every core, a thousand and more plants a frame)
            Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, ids.Length, 512), range =>
            {
                var to = (InstanceDesc*)at;
                for (int n = range.Item1; n < range.Item2; n++)
                {
                    int i = ids[n]; var inst = _g.Instances[i];
                    var m = _dequant[inst.Mesh] * _g.World(i, time);
                    ref var desc = ref to[i]; desc = new InstanceDesc { IdAndMask = (uint)i & 0xFFFFFF | (uint)_masks[inst.Mesh] << 24, Blas = _blas[inst.Mesh] };
                    desc.Transform[0] = m.M11; desc.Transform[1] = m.M21; desc.Transform[2] = m.M31; desc.Transform[3] = m.M41; desc.Transform[4] = m.M12; desc.Transform[5] = m.M22;
                    desc.Transform[6] = m.M32; desc.Transform[7] = m.M42; desc.Transform[8] = m.M13; desc.Transform[9] = m.M23; desc.Transform[10] = m.M33; desc.Transform[11] = m.M43;
                }
            });
        }
        _instances.Unmap(0);
    }
    private int[]? _moving;
}
