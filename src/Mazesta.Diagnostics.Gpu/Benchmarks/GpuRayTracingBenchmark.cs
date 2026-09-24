using Mazesta.Core.Hardware; using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Benchmarks; using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// DirectX Raytracing: a frame that is ray-traced entirely - camera rays, a shadow ray per hit and up to three mirror
/// bounces - against a hardware acceleration structure of 2305 instances (2304 spheres of 5120 triangles and a ground
/// plane), at 2560x1440 off screen. Uses DXR 1.1 inline ray tracing from a compute shader, so it runs on GPUs that report
/// ray-tracing tier 1.1 (GeForce RTX, Radeon RX 6000 and later, Arc); others are Unsupported. The rays of one frame are
/// counted on the GPU once, so rays per second is measured, not estimated. Mazesta's own scene, not a 3DMark score.
/// </summary>
public sealed class GpuRayTracingBenchmark : IBenchmark
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.rt"), "Bench_Gpu_Rt", 60, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    private const int Width = 2560, Height = 1440, Grid = 48, Subdivisions = 4;

    [StructLayout(LayoutKind.Sequential)] private readonly record struct Frame(uint Width, uint Height, uint Count, float Unused);

    /// <summary>D3D12_RAYTRACING_INSTANCE_DESC: a row-major 3x4 transform, then 24-bit id + 8-bit mask, 24-bit hit-group offset + 8-bit flags, and the BLAS address.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private unsafe struct InstanceDesc
    {
        public fixed float Transform[12];
        public uint IdAndMask, OffsetAndFlags;
        public ulong Blas;
        public static InstanceDesc Create(uint id, ulong blas, Vector3 position, Vector3 scale)
        {
            var d = new InstanceDesc { IdAndMask = id & 0xFFFFFF | 0xFFu << 24, Blas = blas };
            d.Transform[0] = scale.X; d.Transform[3] = position.X; d.Transform[5] = scale.Y; d.Transform[7] = position.Y; d.Transform[10] = scale.Z; d.Transform[11] = position.Z;
            return d;
        }
    }

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Spec, request, s => Run(s, request, ct));

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        var tier = s.Device.CheckFeatureSupport<FeatureDataD3D12Options5>(Vortice.Direct3D12.Feature.Options5).RaytracingTier;
        if (tier < RaytracingTier.Tier1_1) throw new GpuUnsupportedException($"{s.AdapterName} does not support DirectX Raytracing 1.1 (inline ray tracing); reported tier: {(tier == RaytracingTier.NotSupported ? "none" : tier)}.");

        var (sphereVertices, sphereIndices) = Icosphere.Create(Subdivisions);
        var sphere = BuildBlas(s, sphereVertices, sphereIndices);
        var ground = BuildBlas(s, [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1)], [0, 1, 2, 0, 2, 3]);

        var instances = new List<InstanceDesc> { InstanceDesc.Create(0, ground.GPUVirtualAddress, Vector3.Zero, new(400, 1, 400)) };
        for (int i = 0; i < Grid * Grid; i++)
        {
            float radius = 0.35f + 0.3f * (i * 0.618034f % 1);
            instances.Add(InstanceDesc.Create((uint)i + 1, sphere.GPUVirtualAddress, new((i % Grid - Grid / 2f) * 1.6f, radius, i / Grid * 1.6f), new(radius)));
        }
        var instanceBuffer = s.Upload(instances.ToArray(), ResourceStates.NonPixelShaderResource);
        var tlas = Build(s, new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.TopLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace, Layout = ElementsLayout.Array,
            DescriptorsCount = (uint)instances.Count, InstanceDescriptions = instanceBuffer.GPUVirtualAddress
        });

        byte[] shader = D3D12Session.Shader("RayQuery");
        var root = s.Own(s.Device.CreateRootSignature(shader));
        var pipeline = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = root, ComputeShader = shader }));
        var pixels = s.UavBuffer((ulong)Width * Height * 4);
        var counter = s.Upload<uint>([0u], ResourceStates.UnorderedAccess, ResourceFlags.AllowUnorderedAccess);
        void Trace(ID3D12GraphicsCommandList4 l, bool count)
        {
            l.SetPipelineState(pipeline); l.SetComputeRootSignature(root);
            l.SetComputeRoot32BitConstants(0, new Frame(Width, Height, count ? 1u : 0u, 0), 0);
            l.SetComputeRootShaderResourceView(1, tlas.GPUVirtualAddress); l.SetComputeRootUnorderedAccessView(2, pixels.GPUVirtualAddress); l.SetComputeRootUnorderedAccessView(3, counter.GPUVirtualAddress);
            l.Dispatch((Width + 7) / 8, (Height + 7) / 8, 1);
        }

        // One counting frame: the exact number of rays the scene takes, measured by the GPU itself. Every camera ray that hits
        // something adds a shadow ray, so fewer than 1.5 rays a pixel means the frame was mostly empty sky - not a ray-tracing workload.
        uint raysPerFrame = s.Read(1, (l, readback) => { Trace(l, count: true); l.ResourceBarrierTransition(counter, ResourceStates.UnorderedAccess, ResourceStates.CopySource); l.CopyBufferRegion(readback, 0, counter, 0, 4); })[0];
        if (raysPerFrame < 1.5 * Width * Height) throw new InvalidOperationException($"The GPU traced {raysPerFrame} rays for a {Width}x{Height} frame; the scene was not hit.");

        double fps = s.Measure((l, frames) => { for (int f = 0; f < frames; f++) { Trace(l, count: false); l.ResourceBarrierUnorderedAccessView(pixels); } }, request.DurationSeconds, request.Report, ct);
        return ([new("Bench_Gpu_Rt_Fps", fps, "FPS"), new("Bench_Gpu_Rt_Rays", fps * raysPerFrame / 1e9, "Grays/s")],
            $"DXR 1.1 inline ray tracing (tier {(int)tier / 10}.{(int)tier % 10}), off-screen {Width}x{Height}; {instances.Count} instances ({sphereIndices.Length / 3} triangles per sphere); {raysPerFrame / (double)(Width * Height):F2} rays/pixel (camera, shadow, up to 3 bounces)");
    }

    private static ID3D12Resource BuildBlas(D3D12Session s, Vector3[] vertices, uint[] indices)
    {
        var vb = s.Upload<Vector3>(vertices, ResourceStates.NonPixelShaderResource); var ib = s.Upload<uint>(indices, ResourceStates.NonPixelShaderResource);
        var triangles = new RaytracingGeometryTrianglesDescription(new GpuVirtualAddressAndStride(vb.GPUVirtualAddress, 12), Format.R32G32B32_Float, (uint)vertices.Length, 0, ib.GPUVirtualAddress, Format.R32_UInt, (uint)indices.Length);
        return Build(s, new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.BottomLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace, Layout = ElementsLayout.Array, DescriptorsCount = 1,
            GeometryDescriptions = [new RaytracingGeometryDescription(triangles, RaytracingGeometryFlags.Opaque)]
        });
    }

    private static ID3D12Resource Build(D3D12Session s, BuildRaytracingAccelerationStructureInputs inputs)
    {
        var sizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
        var result = s.Buffer(sizes.ResultDataMaxSizeInBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
        using (s.Scope())   // the scratch memory is only needed while building
        {
            var scratch = s.UavBuffer(sizes.ScratchDataSizeInBytes);
            s.Run(l => l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(result.GPUVirtualAddress, inputs, 0, scratch.GPUVirtualAddress)));
        }
        return result;
    }
}
