using Mazesta.Core.Hardware; using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Benchmarks; using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// Direct3D 12 rasterisation: the classic graphics pipeline (vertex and pixel shaders, depth test, blending) rendering off
/// screen at 2560x1440, with no window, no presentation and no v-sync cap. Two thirds of the duration draw a scene of 4096
/// lit spheres (5.2 million triangles a frame) for frames per second and triangle throughput; the rest blends 16
/// full-screen layers a frame for pixel fill rate. Mazesta's own scene - not comparable with other programs' or games' scores.
/// </summary>
public sealed class GpuRasterBenchmark : IBenchmark, ITestAvailability
{
    public static readonly TestDefinition Spec = new(new TestId("bench.gpu.d3d"), "Bench_Gpu_D3D", 60, [GpuDevices.Option]);
    public TestDefinition Definition => Spec;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private const int Width = 2560, Height = 1440, Columns = 64, Instances = Columns * Columns, FillLayers = 16, Subdivisions = 3;

    [StructLayout(LayoutKind.Sequential)] private readonly record struct Frame(float Time, uint Columns, float Aspect, uint Layers);

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Spec, request, s => Run(s, request, ct), (Width, Height));

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        var (vertices, indices) = Icosphere.Create(Subdivisions);
        var vertexBuffer = s.Upload<Vector3>(vertices, ResourceStates.NonPixelShaderResource); var indexBuffer = s.Upload<uint>(indices, ResourceStates.IndexBuffer);
        byte[] sceneVs = D3D12Session.Shader("RasterSceneVS"), fillVs = D3D12Session.Shader("RasterFillVS");
        var root = s.Own(s.Device.CreateRootSignature(sceneVs));   // declared in the shader ([RootSignature]), shared by both passes
        var scene = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(root, sceneVs, D3D12Session.Shader("RasterScenePS"), BlendDescription.Opaque, DepthStencilDescription.Default, Format.D32_Float)));
        var fill = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(root, fillVs, D3D12Session.Shader("RasterFillPS"), BlendDescription.NonPremultiplied, DepthStencilDescription.None, Format.Unknown)));

        var color = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, Width, Height, 1, 1, flags: ResourceFlags.AllowRenderTarget), ResourceStates.RenderTarget, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 1))));
        var depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, Width, Height, 1, 1, flags: ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
        var rtvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 1, DescriptorHeapFlags.None, 0)));
        var dsvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None, 0)));
        var rtv = rtvHeap.GetCPUDescriptorHandleForHeapStart(); var dsv = dsvHeap.GetCPUDescriptorHandleForHeapStart();
        s.Device.CreateRenderTargetView(color, null, rtv); s.Device.CreateDepthStencilView(depth, null, dsv);

        void Begin(ID3D12GraphicsCommandList4 l, ID3D12PipelineState pipeline, int frame)
        {
            l.ClearRenderTargetView(rtv, new Color4(0.02f, 0.02f, 0.03f, 1));
            if (pipeline == scene) { l.ClearDepthStencilView(dsv, ClearFlags.Depth, 1, 0); l.OMSetRenderTargets(rtv, dsv); } else l.OMSetRenderTargets(rtv);   // only the scene is depth-tested
            l.SetPipelineState(pipeline); l.SetGraphicsRootSignature(root);
            l.RSSetViewport(0, 0, Width, Height); l.RSSetScissorRect(Width, Height); l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            l.SetGraphicsRoot32BitConstants(0, new Frame(frame / 60f, Columns, Width / (float)Height, FillLayers), 0);
        }

        double sceneSeconds = request.DurationSeconds * 2 / 3.0, fillSeconds = request.DurationSeconds - sceneSeconds;
        double fps = s.Measure((l, frames) =>
        {
            for (int f = 0; f < frames; f++)
            {
                Begin(l, scene, f);
                l.SetGraphicsRootShaderResourceView(1, vertexBuffer.GPUVirtualAddress);
                l.IASetIndexBuffer(indexBuffer.GPUVirtualAddress, (uint)(indices.Length * sizeof(uint)), Format.R32_UInt);
                l.DrawIndexedInstanced((uint)indices.Length, Instances, 0, 0, 0);
            }
        }, sceneSeconds, p => request.Report(p * 2 / 3), ct);
        // The last scene frame must actually show the scene: a frame rate of an empty screen is not a rendering result.
        var frame = s.Read(Width * Height, (l, readback) =>
        {
            l.ResourceBarrierTransition(color, ResourceStates.RenderTarget, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, Width, Height, 1, Width * 4) }), 0, 0, 0, new TextureCopyLocation(color, 0));
            l.ResourceBarrierTransition(color, ResourceStates.CopySource, ResourceStates.RenderTarget);
        });
        double coverage = frame.Count(p => p != frame[0]) / (double)frame.Length;   // pixel 0 is background: the top-left corner is sky
        if (coverage < 0.1) throw new InvalidOperationException($"The rendered frame shows almost nothing ({coverage:P0} of the pixels); the scene did not render.");
        double fillFrames = s.Measure((l, frames) =>
        {
            for (int f = 0; f < frames; f++) { Begin(l, fill, f); l.DrawInstanced(3, FillLayers, 0, 0); }
        }, fillSeconds, p => request.Report(2 / 3.0 + p / 3), ct);

        long triangles = indices.Length / 3L * Instances;
        return ([new("Bench_Gpu_Fps", fps, "FPS"), new("Bench_Gpu_Triangles", fps * triangles / 1e9, "Gtri/s"), new("Bench_Gpu_Fill", fillFrames * FillLayers * Width * Height / 1e9, "Gpixel/s")],
            $"Direct3D 12 off-screen {Width}x{Height}; scene {Instances} spheres x {indices.Length / 3} triangles ({triangles / 1e6:F1} M/frame) with 4 lights, covering {coverage:P0} of the frame; fill {FillLayers} blended full-screen layers/frame");
    }

    private static GraphicsPipelineStateDescription Pipeline(ID3D12RootSignature root, byte[] vs, byte[] ps, BlendDescription blend, DepthStencilDescription depth, Format depthFormat) => new()
    {
        RootSignature = root, VertexShader = vs, PixelShader = ps, BlendState = blend, DepthStencilState = depth, DepthStencilFormat = depthFormat,
        RasterizerState = RasterizerDescription.CullNone, SampleMask = uint.MaxValue, PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
        RenderTargetFormats = [Format.R8G8B8A8_UNorm], SampleDescription = SampleDescription.Default
    };
}
