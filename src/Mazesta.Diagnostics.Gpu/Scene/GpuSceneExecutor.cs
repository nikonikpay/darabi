using System.Diagnostics; using System.Globalization; using System.Numerics; using System.Runtime.InteropServices; using ComputeSharp;
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The visual GPU test (what FurMark is used for, with Mazesta's own scene): a window opens and the test model - the Mazesta logo, or the
/// owner's Models\gpu-test.obj - turns in the middle with a ring of copies around it, drawn as fast as the GPU can with no v-sync cap, so the
/// card runs at full load while the technician watches the picture for artefacts and the monitor records clocks, power and temperature.
/// Two modes on the same scene: Direct3D 12 rasterisation, and DirectX Raytracing (DXR 1.1) with shadows and reflections, offered only on
/// a GPU with hardware ray tracing.
/// A picture that merely looks right is not the pass: every few seconds the scene is also drawn off screen at one fixed moment and read
/// back, and that frame must be bit-for-bit the first one - a GPU that draws the same frame differently under load has computed wrongly.
/// </summary>
public sealed class GpuSceneExecutor(bool rayTraced) : ITestExecutor, ITestAvailability
{
    public const string ResolutionOption = "resolution", LoadOption = "load";
    // The frame is drawn at the chosen size even where the screen is smaller (the window then shows it scaled down), so 4K load can be
    // tested on any monitor; full screen draws at the screen's own size.
    private static readonly TestOption Resolution = new(ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, "1920x1080",
        () => [new("1280x720", "1280 × 720 (HD)"), new("1920x1080", "1920 × 1080 (Full HD)"), new("2560x1440", "2560 × 1440 (2K)"), new("3840x2160", "3840 × 2160 (4K)"),
               new("fullscreen", "Test_Resolution_FullScreen", true)]);
    public static readonly TestDefinition Raster = new(new TestId("gpu.scene.d3d"), "Test_Gpu_Scene3D", 120,
        [GpuDevices.Option, Resolution, new TestOption(LoadOption, "Test_Option_GpuLoad", TestOptionKind.Choice, "3",
            () => [new("1", "Test_GpuLoad_Light", true), new("2", "Test_GpuLoad_Medium", true), new("3", "Test_GpuLoad_Heavy", true), new("4", "Test_GpuLoad_Extreme", true)])]);
    public static readonly TestDefinition RayTraced = new(new TestId("gpu.scene.rt"), "Test_Gpu_SceneRt", 120, [GpuDevices.Option, Resolution]);
    public TestDefinition Definition => rayTraced ? RayTraced : Raster;

    public Unavailability? CheckAvailability(TestOptions options) => rayTraced ? GpuFeatures.RayTracingAvailability(options) : GpuFeatures.GpuAvailability(options);

    private const int Ring = 12, CheckSeconds = 3; private const float Radius = 5.2f, CheckTime = 1.234f;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var id = Definition.Id;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        if (CheckAvailability(options) is { } no) return Task.FromResult(TestRunResult.Unsupported(id, started, no.Detail));
        var device = GpuDevices.Resolve(request, Definition)!;
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The window, its messages and the rendering share one thread of their own, for the whole run.
        var thread = new Thread(() =>
        {
            try { done.SetResult(Run(request, options, device, started, ct)); }
            catch (Exception e) { done.SetResult(new(id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, $"GPU error during the visual test: {e.GetType().Name}: {e.Message}")); }
        }) { IsBackground = true, Name = "Mazesta GPU scene" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
    private TestRunResult Run(TestExecutionRequest request, TestOptions options, GraphicsDevice device, DateTimeOffset started, CancellationToken ct)
    {
        var model = SceneModel.Load(out string? modelProblem);
        string res = options.Get(ResolutionOption); bool full = res == "fullscreen";
        var (w, h) = full ? (0, 0) : ParseSize(res);
        uint load = rayTraced ? 3u : (uint)Math.Clamp(int.TryParse(options.Get(LoadOption), out int l) ? l : 3, 1, 4);
        string mode = rayTraced ? "DirectX Raytracing" : "Direct3D 12", level = rayTraced ? "3 reflection bounces" : LoadNames[load - 1];
        using var session = new D3D12Session(device);
        using var window = new TestWindow($"Mazesta — {mode} — {model.Name}", w, h, full);
        if (full) (w, h) = (window.Width, window.Height);
        using var renderer = rayTraced ? (SceneRenderer)new RayRenderer(session, window, model, w, h) : new RasterRenderer(session, window, model, w, h, load);
        string work = renderer is RasterRenderer raster
            ? $"{raster.TrianglesPerFrame / 1e6:F2} M triangles · {raster.Shells} fur layers · {raster.Particles / 1000}k particles"
            : $"{model.Triangles * (Ring + 1):N0} triangles ray traced · shadows · reflections";
        string card = $"{session.AdapterName} · {device.DedicatedMemorySize / (1024.0 * 1024 * 1024):F1} GB";
        var gpu = GpuNode(request.Engine, session.AdapterName);

        long frames = 0, checks = 0, errors = 0; ulong? reference = null; string firstError = ""; bool closed = false; double minFps = double.MaxValue;
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        var lastCheck = Stopwatch.StartNew(); var updateClock = Stopwatch.StartNew(); long updateFrames = 0;
        ShowReadout(0);
        try
        {
            while (total.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) { closed = true; break; }
                if (reference is null || lastCheck.Elapsed.TotalSeconds >= CheckSeconds)
                {
                    ulong sum = renderer.CheckFrame(CheckTime); checks++; lastCheck.Restart();
                    if (reference is null) reference = sum;
                    else if (sum != reference) { errors++; if (firstError.Length == 0) firstError = $"check frame {checks} differs from the first one (same scene, same moment)"; }
                }
                renderer.Present((float)total.Elapsed.TotalSeconds);
                frames++; updateFrames++;
                if (updateClock.Elapsed.TotalSeconds >= 0.5)
                {
                    double fps = updateFrames / updateClock.Elapsed.TotalSeconds;
                    if (total.Elapsed.TotalSeconds > 2) minFps = Math.Min(minFps, fps);   // the first moments include start-up, not the card's pace
                    window.Title = $"Mazesta — {mode} — {fps:F0} FPS — {(int)(duration - total.Elapsed).TotalSeconds} s";
                    ShowReadout(fps);
                    updateFrames = 0; updateClock.Restart();
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe()); }
        if (closed) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, SensorEvidence.Join("the test window was closed before the time was up", Describe()));
        // One last check, so the end of the run is verified too.
        if (reference is { } r && renderer.CheckFrame(CheckTime) != r) { errors++; if (firstError.Length == 0) firstError = "the final check frame differs from the first one"; }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe());

        // The readout shows only what was measured: a sensor this card does not report (or has not reported in the last seconds) is left out.
        void ShowReadout(double fps)
        {
            double average = frames / Math.Max(0.001, total.Elapsed.TotalSeconds);
            var now = request.Clock.UtcNow;
            string sensors = string.Join(" · ", new[]
            {
                Reading(SensorRole.GpuCoreTemp, "{0:F0} °C"), Reading(SensorRole.GpuHotSpotTemp, "hot spot {0:F0} °C"), Reading(SensorRole.GpuCoreClock, "{0:F0} MHz"),
                Reading(SensorRole.GpuLoad3D, "load {0:F0} %"), Reading(SensorRole.GpuPower, "{0:F0} W"), Reading(SensorRole.GpuFanPercent, "fan {0:F0} %")
            }.Where(s => s is not null));
            renderer.Overlay.Update(
            [
                new($"MAZESTA  {mode} · {level}", 0xFFD21F),
                new(frames == 0 ? "starting…" : $"{fps:F0} FPS · {1000 / Math.Max(fps, 0.001):F2} ms · avg {average:F0}" + (minFps < double.MaxValue ? $" · min {minFps:F0}" : ""), 0x7CFF6B),
                new(card),
                new($"{w} × {h}" + (window.Width != w || window.Height != h ? $" drawn, shown in {window.Width} × {window.Height}" : ""), 0xC8D2E6),
                new(work, 0xC8D2E6),
                new(sensors.Length > 0 ? sensors : "no GPU sensor readings", sensors.Length > 0 ? 0xFFB347u : 0x8890A0u),
                new($"{(int)total.Elapsed.TotalSeconds} / {request.DurationSeconds} s · check frames {checks} · errors {errors} · Esc stops", errors > 0 ? 0xFF4D4Du : 0xC8D2E6u)
            ]);
            string? Reading(SensorRole role, string format) => Latest(request.Engine, gpu, role, now) is { } v ? string.Format(CultureInfo.InvariantCulture, format, v) : null;
        }

        string Describe()
        {
            var finished = request.Clock.UtcNow;
            return SensorEvidence.Join($"{mode} scene drawn at {w}x{h} (window {window.Width}x{window.Height}) on {session.AdapterName}; model '{model.Name}' ({model.Triangles} triangles) with {Ring} copies",
                rayTraced ? "ray traced: camera, shadow and up to 3 reflection rays a pixel" : $"load level {load}: {work}",
                $"frames={frames}", $"{frames / Math.Max(0.001, total.Elapsed.TotalSeconds):F1} FPS average", minFps < double.MaxValue ? $"{minFps:F1} FPS lowest half-second" : null, $"check frames={checks}",
                firstError.Length > 0 ? firstError : null, modelProblem,
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuLoad3D, started, finished)?.Format("measured GPU load", "%"),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuPower, started, finished)?.Format("GPU power", " W", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished)?.Format("GPU temperature", "°C", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished)?.Format("GPU hot spot", "°C", includeMax: true));
        }
    }

    private static readonly string[] LoadNames = ["light", "medium", "heavy", "extreme"];

    /// <summary>The monitored GPU that is the card under test: matched by name, or the only GPU there is. With several cards and no name
    /// match there is no answer - another card's temperature must never be shown as this one's.</summary>
    internal static HardwareNode? GpuNode(PollingEngine? engine, string adapter)
    {
        var gpus = engine?.Hardware.Where(n => n.Kind == HardwareKind.Gpu).ToList();
        if (gpus is null || gpus.Count == 0) return null;
        return gpus.FirstOrDefault(n => n.Name.Contains(adapter, StringComparison.OrdinalIgnoreCase) || adapter.Contains(n.Name, StringComparison.OrdinalIgnoreCase))
            ?? (gpus.Count == 1 ? gpus[0] : null);
    }

    /// <summary>The node's newest reading of <paramref name="role"/> from the last few seconds, or null: a stale value is not a live reading.</summary>
    internal static double? Latest(PollingEngine? engine, HardwareNode? node, SensorRole role, DateTimeOffset now)
    {
        if (engine is null || node is null) return null;
        int recent = engine.History.SecondsSinceEpoch(now) - 5;
        foreach (var s in node.Sensors.Where(s => s.Role == role))
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = raw.Values.Length - 1; i >= 0 && raw.Seconds[i] >= recent; i--) if (!float.IsNaN(raw.Values[i])) return raw.Values[i];
        }
        return null;
    }

    internal static (int, int) ParseSize(string s) { var p = s.Split('x'); return p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) ? (w, h) : (1280, 720); }

    /// <summary>Where copy <paramref name="instance"/> is at <paramref name="time"/> - the CPU twin of SceneRaster.hlsl's Place, used to build the
    /// ray-traced mode's instances, so both modes show the same scene.</summary>
    internal static Matrix4x4 Placement(int instance, float time)
    {
        static Matrix4x4 RotY(float a) { float s = MathF.Sin(a), c = MathF.Cos(a); return new(c, 0, s, 0, 0, 1, 0, 0, -s, 0, c, 0, 0, 0, 0, 1); }
        static Matrix4x4 RotX(float a) { float s = MathF.Sin(a), c = MathF.Cos(a); return new(1, 0, 0, 0, 0, c, -s, 0, 0, s, c, 0, 0, 0, 0, 1); }
        // Rows as in HLSL's float3x3(...): world = R·p·scale + offset, stored row-major with the offset in the fourth column.
        Matrix4x4 r; Vector3 offset; float scale;
        if (instance == 0) { r = Mul(RotY(time * 0.9f), RotX(0.25f * MathF.Sin(time * 0.6f))); offset = new(0, 0.3f, 0); scale = 1; }
        else
        {
            float i = instance, a = i / Ring * 6.2831853f + time * 0.35f;
            r = Mul(RotY(-time * 1.7f + i), RotX(time * 1.1f + i));
            offset = new(MathF.Cos(a) * Radius, 0.9f * MathF.Sin(time * 1.3f + i * 0.7f) + 0.3f, MathF.Sin(a) * Radius); scale = 0.22f;
        }
        return new(r.M11 * scale, r.M12 * scale, r.M13 * scale, offset.X, r.M21 * scale, r.M22 * scale, r.M23 * scale, offset.Y, r.M31 * scale, r.M32 * scale, r.M33 * scale, offset.Z, 0, 0, 0, 1);
    }
    /// <summary>The HLSL product mul(a, b) of two row-major rotations.</summary>
    private static Matrix4x4 Mul(Matrix4x4 a, Matrix4x4 b) => Matrix4x4.Multiply(a, b);

    // ——— rendering ———

    private abstract class SceneRenderer : IDisposable
    {
        protected readonly D3D12Session S; protected readonly IDXGISwapChain3 Swap; protected readonly ID3D12Resource[] Back;
        protected readonly ID3D12Resource ModelBuffer; protected readonly SceneModel Model; private readonly bool _tearing;
        /// <summary>The size the frame is drawn at - the chosen resolution, whatever the window shows it at (the swap chain scales it to fit).</summary>
        public int Width { get; } public int Height { get; }
        public SceneOverlay Overlay { get; }

        protected SceneRenderer(D3D12Session s, TestWindow w, SceneModel model, int width, int height)
        {
            S = s; Model = model; Width = width; Height = height;
            ModelBuffer = s.Upload(model.Vertices, ResourceStates.NonPixelShaderResource);
            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory5>(false);
            _tearing = factory.PresentAllowTearing;
            var desc = new SwapChainDescription1
            {
                Width = (uint)width, Height = (uint)height, Format = Format.R8G8B8A8_UNorm, BufferUsage = Usage.RenderTargetOutput, BufferCount = 2, Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore, SampleDescription = SampleDescription.Default, Flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None
            };
            using var swap1 = factory.CreateSwapChainForHwnd(s.Queue, w.Handle, desc);
            factory.MakeWindowAssociation(w.Handle, WindowAssociationFlags.IgnoreAltEnter);
            Swap = swap1.QueryInterface<IDXGISwapChain3>();
            Back = [Swap.GetBuffer<ID3D12Resource>(0), Swap.GetBuffer<ID3D12Resource>(1)];
            Overlay = new SceneOverlay(s, Back, height);
        }

        /// <summary>Draws the scene at <paramref name="time"/> into the next back buffer, lays the readout over it and shows it, as fast as the
        /// GPU goes (no v-sync).</summary>
        public void Present(float time)
        {
            int index = (int)Swap.CurrentBackBufferIndex;
            S.Run(l => { Draw(l, time, Back[index], index); Overlay.Draw(l, index, Width, Height); });
            Swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();
        }

        /// <summary>Draws the scene at a fixed moment off screen and returns the exact bits of the picture.</summary>
        public abstract ulong CheckFrame(float time);
        protected abstract void Draw(ID3D12GraphicsCommandList4 l, float time, ID3D12Resource backBuffer, int index);

        protected static ulong Hash(ReadOnlySpan<uint> pixels) { ulong h = 1469598103934665603UL; foreach (uint p in pixels) h = (h ^ p) * 1099511628211UL; return h; }

        public virtual void Dispose() { Overlay.Dispose(); foreach (var b in Back) b.Dispose(); Swap.Dispose(); }
    }

    private sealed class RasterRenderer : SceneRenderer
    {
        [StructLayout(LayoutKind.Sequential)] private readonly record struct Frame(float Time, float Aspect, uint Ring, uint Load, float Radius, uint Shells, uint Particles, float Pad);
        private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _scene, _background, _fur, _particles;
        private readonly ID3D12Resource _depth, _check, _furBuffer; private readonly ID3D12DescriptorHeap _rtvHeap, _dsvHeap; private readonly uint _rtvSize; private readonly uint _load;
        public uint Shells { get; } public uint Particles { get; }

        public RasterRenderer(D3D12Session s, TestWindow w, SceneModel model, int width, int height, uint load) : base(s, w, model, width, height)
        {
            _load = load; Shells = FurShells(load); Particles = ParticleCount(load);
            _furBuffer = s.Upload(model.SmoothNormals(), ResourceStates.NonPixelShaderResource);
            byte[] vs = D3D12Session.Shader("SceneRasterVS");
            _root = s.Own(s.Device.CreateRootSignature(vs));
            var translucent = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual);   // hidden behind the models, but writes no depth of its own
            _scene = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(_root, vs, D3D12Session.Shader("SceneRasterPS"), DepthStencilDescription.Default, BlendDescription.Opaque)));
            _background = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(_root, D3D12Session.Shader("SceneBackgroundVS"), D3D12Session.Shader("SceneBackgroundPS"), DepthStencilDescription.None, BlendDescription.Opaque)));
            _fur = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(_root, D3D12Session.Shader("SceneFurVS"), D3D12Session.Shader("SceneFurPS"), translucent, BlendDescription.NonPremultiplied)));
            _particles = s.Own(s.Device.CreateGraphicsPipelineState(Pipeline(_root, D3D12Session.Shader("SceneParticleVS"), D3D12Session.Shader("SceneParticlePS"), translucent, BlendDescription.Additive)));
            _depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)Width, (uint)Height, 1, 1, flags: ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
            _check = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, 1, flags: ResourceFlags.AllowRenderTarget), ResourceStates.RenderTarget, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 1))));
            _rtvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 3, DescriptorHeapFlags.None, 0)));
            _dsvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None, 0)));
            _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            for (int i = 0; i < 2; i++) s.Device.CreateRenderTargetView(Back[i], null, Rtv(i));
            s.Device.CreateRenderTargetView(_check, null, Rtv(2));
            s.Device.CreateDepthStencilView(_depth, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        }

        /// <summary>Fur layers and embers per load level: FurMark-heavy from "heavy" up, enough to keep a current card busy at 4K.</summary>
        internal static uint FurShells(uint load) => load switch { 1 => 12, 2 => 24, 3 => 40, _ => 64 };
        internal static uint ParticleCount(uint load) => load switch { 1 => 16_384, 2 => 49_152, 3 => 98_304, _ => 196_608 };
        public long TrianglesPerFrame => (long)Model.Triangles * (Ring + 1) * (1 + Shells) + Particles * 2L + 1;

        private CpuDescriptorHandle Rtv(int i) => _rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize);

        private void Scene(ID3D12GraphicsCommandList4 l, float time, CpuDescriptorHandle rtv)
        {
            var dsv = _dsvHeap.GetCPUDescriptorHandleForHeapStart();
            l.ClearDepthStencilView(dsv, ClearFlags.Depth, 1, 0); l.OMSetRenderTargets(rtv, dsv);
            l.SetGraphicsRootSignature(_root); l.RSSetViewport(0, 0, Width, Height); l.RSSetScissorRect(Width, Height); l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            l.SetGraphicsRoot32BitConstants(0, new Frame(time, Width / (float)Height, Ring, _load, Radius, Shells, Particles, 0), 0);
            l.SetGraphicsRootShaderResourceView(1, ModelBuffer.GPUVirtualAddress);
            l.SetPipelineState(_background); l.DrawInstanced(3, 1, 0, 0);
            l.SetPipelineState(_scene); l.DrawInstanced((uint)Model.Vertices.Length, Ring + 1, 0, 0);
            l.SetGraphicsRootShaderResourceView(1, _furBuffer.GPUVirtualAddress);
            l.SetPipelineState(_fur); l.DrawInstanced((uint)Model.Vertices.Length, (Ring + 1) * Shells, 0, 0);
            l.SetPipelineState(_particles); l.DrawInstanced(6, Particles, 0, 0);
        }

        protected override void Draw(ID3D12GraphicsCommandList4 l, float time, ID3D12Resource backBuffer, int index)
        {
            l.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);
            Scene(l, time, Rtv(index));
            l.ResourceBarrierTransition(backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
        }

        public override ulong CheckFrame(float time)
        {
            uint pitch = ((uint)Width * 4 + 255) & ~255u;
            var pixels = S.Read((int)(pitch / 4 * Height), (l, readback) =>
            {
                Scene(l, time, Rtv(2));
                l.ResourceBarrierTransition(_check, ResourceStates.RenderTarget, ResourceStates.CopySource);
                l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(_check, 0));
                l.ResourceBarrierTransition(_check, ResourceStates.CopySource, ResourceStates.RenderTarget);
            });
            return Hash(pixels);
        }

        private static GraphicsPipelineStateDescription Pipeline(ID3D12RootSignature root, byte[] vs, byte[] ps, DepthStencilDescription depth, BlendDescription blend) => new()
        {
            RootSignature = root, VertexShader = vs, PixelShader = ps, BlendState = blend, DepthStencilState = depth, DepthStencilFormat = Format.D32_Float,
            RasterizerState = RasterizerDescription.CullNone, SampleMask = uint.MaxValue, PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Format.R8G8B8A8_UNorm], SampleDescription = SampleDescription.Default
        };
    }

    private sealed unsafe class RayRenderer : SceneRenderer
    {
        [StructLayout(LayoutKind.Sequential)] private readonly record struct Frame(uint Width, uint Height, uint Pitch, uint Bounces, float Time, float Aspect, float P0, float P1);
        [StructLayout(LayoutKind.Sequential, Size = 64)] private struct InstanceDesc { public fixed float Transform[12]; public uint IdAndMask, OffsetAndFlags; public ulong Blas; }
        private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline;
        private readonly ID3D12Resource _modelBlas, _floorBlas, _tlas, _tlasScratch, _instances, _pixels; private readonly uint _pitch;
        private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;

        public RayRenderer(D3D12Session s, TestWindow w, SceneModel model, int width, int height) : base(s, w, model, width, height)
        {
            byte[] cs = D3D12Session.Shader("SceneRay");
            _root = s.Own(s.Device.CreateRootSignature(cs));
            _pipeline = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = cs }));
            _pitch = ((uint)Width + 63) & ~63u;   // a row of 64 pixels is 256 bytes: the pitch a buffer-to-texture copy needs
            _pixels = s.UavBuffer((ulong)_pitch * (ulong)Height * 4);
            // The model's positions are the first 12 bytes of each 24-byte vertex: the BLAS reads them in place from the model buffer.
            _modelBlas = Blas(s, new RaytracingGeometryTrianglesDescription(new GpuVirtualAddressAndStride(ModelBuffer.GPUVirtualAddress, 24), Format.R32G32B32_Float, (uint)model.Vertices.Length));
            var floor = s.Upload<Vector3>([new(-60, -1.2f, -60), new(60, -1.2f, -60), new(60, -1.2f, 60), new(-60, -1.2f, -60), new(60, -1.2f, 60), new(-60, -1.2f, 60)], ResourceStates.NonPixelShaderResource);
            _floorBlas = Blas(s, new RaytracingGeometryTrianglesDescription(new GpuVirtualAddressAndStride(floor.GPUVirtualAddress, 12), Format.R32G32B32_Float, 6));
            _instances = s.Own(s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer((ulong)(Ring + 2) * 64), ResourceStates.GenericRead));
            _tlasInputs = new BuildRaytracingAccelerationStructureInputs
            {
                Type = RaytracingAccelerationStructureType.TopLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastBuild, Layout = ElementsLayout.Array,
                DescriptorsCount = Ring + 2, InstanceDescriptions = _instances.GPUVirtualAddress
            };
            var sizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(_tlasInputs);
            _tlas = s.Buffer(sizes.ResultDataMaxSizeInBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
            _tlasScratch = s.UavBuffer(sizes.ScratchDataSizeInBytes);
        }

        private static ID3D12Resource Blas(D3D12Session s, RaytracingGeometryTrianglesDescription triangles)
        {
            var inputs = new BuildRaytracingAccelerationStructureInputs
            {
                Type = RaytracingAccelerationStructureType.BottomLevel, Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace, Layout = ElementsLayout.Array, DescriptorsCount = 1,
                GeometryDescriptions = [new RaytracingGeometryDescription(triangles, RaytracingGeometryFlags.Opaque)]
            };
            var sizes = s.Device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
            var result = s.Buffer(sizes.ResultDataMaxSizeInBytes, state: ResourceStates.RaytracingAccelerationStructure, flags: ResourceFlags.AllowUnorderedAccess);
            using (s.Scope()) { var scratch = s.UavBuffer(sizes.ScratchDataSizeInBytes); s.Run(l => l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(result.GPUVirtualAddress, inputs, 0, scratch.GPUVirtualAddress))); }
            return result;
        }

        /// <summary>Instance 0 is the floor, instance 1 the centre model and 2.. the ring (the shader tells them apart by instance id).</summary>
        private void WriteInstances(float time)
        {
            var span = _instances.Map<InstanceDesc>(0, Ring + 2);
            span[0] = Instance(0, _floorBlas.GPUVirtualAddress, Matrix4x4.Identity);
            for (int i = 0; i <= Ring; i++) span[i + 1] = Instance((uint)i + 1, _modelBlas.GPUVirtualAddress, Placement(i, time));
            _instances.Unmap(0);
        }
        private static InstanceDesc Instance(uint id, ulong blas, Matrix4x4 m)
        {
            var d = new InstanceDesc { IdAndMask = id & 0xFFFFFF | 0xFFu << 24, Blas = blas };
            float[] rows = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34];
            for (int i = 0; i < 12; i++) d.Transform[i] = rows[i];
            return d;
        }

        private void Trace(ID3D12GraphicsCommandList4 l, float time)
        {
            l.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _tlasScratch.GPUVirtualAddress));
            l.ResourceBarrierUnorderedAccessView(_tlas);
            l.SetPipelineState(_pipeline); l.SetComputeRootSignature(_root);
            l.SetComputeRoot32BitConstants(0, new Frame((uint)Width, (uint)Height, _pitch, 3, time, Width / (float)Height, 0, 0), 0);
            l.SetComputeRootShaderResourceView(1, _tlas.GPUVirtualAddress); l.SetComputeRootShaderResourceView(2, ModelBuffer.GPUVirtualAddress);
            l.SetComputeRootUnorderedAccessView(3, _pixels.GPUVirtualAddress);
            l.Dispatch(((uint)Width + 7) / 8, ((uint)Height + 7) / 8, 1);
            l.ResourceBarrierUnorderedAccessView(_pixels);
        }

        protected override void Draw(ID3D12GraphicsCommandList4 l, float time, ID3D12Resource backBuffer, int index)
        {
            WriteInstances(time);   // safe: the previous frame's submission has finished (every Run waits for the GPU)
            Trace(l, time);
            l.ResourceBarrierTransition(_pixels, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
            l.ResourceBarrierTransition(backBuffer, ResourceStates.Present, ResourceStates.CopyDest);
            l.CopyTextureRegion(new TextureCopyLocation(backBuffer, 0), 0, 0, 0,
                new TextureCopyLocation(_pixels, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, _pitch * 4) }));
            l.ResourceBarrierTransition(backBuffer, ResourceStates.CopyDest, ResourceStates.Present);
            l.ResourceBarrierTransition(_pixels, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
        }

        public override ulong CheckFrame(float time)
        {
            WriteInstances(time);
            var pixels = S.Read((int)(_pitch * Height), (l, readback) =>
            {
                Trace(l, time);
                l.ResourceBarrierTransition(_pixels, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
                l.CopyBufferRegion(readback, 0, _pixels, 0, (ulong)_pitch * (ulong)Height * 4);
                l.ResourceBarrierTransition(_pixels, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
            });
            return Hash(pixels);
        }
    }
}
