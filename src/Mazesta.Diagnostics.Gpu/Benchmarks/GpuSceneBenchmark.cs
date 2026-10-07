using System.Diagnostics; using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The visual tests' Persian garden as a benchmark: the same scene and camera walk, in a window like the test's, drawn with no v-sync cap - by
/// Direct3D 12 rasterisation (shadow map, sky map, ambient occlusion, the pool's reflection, MSAA: set by the quality) or by DirectX Raytracing (camera rays a pixel, each with a
/// soft-shadow ray to the moon and every lamp in reach and a bounced-light ray, reflection and refraction, 4 bounces). The resolution and the quality are
/// options (rasterisation); the ray-traced one has a single setting, 4 rays a pixel, like the visual test, and only the resolution to choose.
/// The camera walks the garden at the test's own pace (a walk is <see cref="GardenCamera.Loop"/> seconds, by the clock: the default length), and a run
/// lasts the time asked for, from the gate on. Only a run of whole walks draws the route every card is compared on: a shorter or longer one reports
/// its frame rate under a name of its own ("part of the walk"), which is kept neither as the machine's record nor in the comparison list. Only the drawing of each frame is timed: showing it in the window and the readout over it are not. Reported: the average frame rate, the 1 % low (the
/// frame rate of the average of the slowest hundredth of frames, as CapFrameX and most reviews define it) and the 99th-percentile frame
/// time (the other common definition: 99 % of frames were quicker). The check frames are drawn before and after the timed run, never in it. A frame drawn before the run and again after it at the same moment must be the same
/// bits, or the run failed. Mazesta's own scene - not comparable with other programs' or games' scores. Closing the window cancels the run.
/// </summary>
public sealed class GpuSceneBenchmark(MemoryFactsSource? facts = null, IMemoryProbe? memory = null) : IBenchmark, ITestAvailability
{
    public const string ResolutionOption = "resolution", QualityOption = "quality", DefaultResolution = "1920x1080";
    // The frame is drawn at the chosen size even where the screen is smaller (the window then shows it scaled down), so 4K can be measured on any monitor.
    private static readonly TestOption Resolution = new(ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, DefaultResolution,
        GpuSceneExecutor.Sizes);
    private static readonly TestOption RasterQuality = new(QualityOption, "Bench_Option_Quality", TestOptionKind.Choice, "3",
        () => [new("1", "Test_GpuLoad_Light", true, "shadows 2048 · no MSAA · no pool reflection"), new("2", "Test_GpuLoad_Medium", true, "shadows 2048 · MSAA 2× · pool reflection 1/2"),
               new("3", "Test_GpuLoad_Heavy", true, "shadows 4096 · MSAA 4× · pool reflection 1/1"), new("4", "Test_GpuLoad_Extreme", true, "shadows 4096 (3 taps) · MSAA 8× · pool reflection 1/1")], When: GpuSceneExecutor.RayTracingOption + "=off");
    /// <summary>Ray tracing starts on where the card has DXR 1.1, off otherwise.</summary>
    private static readonly TestOption RayTracing = GpuSceneExecutor.RayTracing with { Preferred = () => GpuFeatures.RayTracingAvailability(TestOptions.None(Scene!)) is null ? "on" : "off" };
    public static readonly TestDefinition Scene = new(new TestId("bench.gpu.scene.d3d"), "Bench_Gpu_SceneD3D", (int)GardenCamera.Loop, [GpuDevices.Option, Resolution, RasterQuality, RayTracing, GpuSceneExecutor.Smoothing, GpuSceneExecutor.Weather, GpuSceneExecutor.Upscaling, GpuSceneExecutor.FullScreen, GpuSceneExecutor.Overlay]);
    public TestDefinition Definition => Scene;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => options.Get(GpuSceneExecutor.RayTracingOption) == "on" ? GpuFeatures.RayTracingAvailability(options) : GpuFeatures.GpuAvailability(options);
    private const float Step = 1 / 30f, CheckTime = 1.234f;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Definition, request, s => Run(s, request, ct), ownThread: true);

    /// <summary>The RAM's Triad bandwidth and latency in a few seconds, with the window kept alive (its messages answered) so it is not marked as not responding.</summary>
    private static (double Triad, double Latency)? MeasureRam(TestWindow window, IMemoryProbe memory, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var work = Task.Run(() => MemoryBenchmark.Quick(memory, TimeSpan.FromSeconds(3), cts.Token), CancellationToken.None);
        while (!work.IsCompleted) { if (!window.Pump()) cts.Cancel(); Thread.Sleep(25); }
        ct.ThrowIfCancellationRequested();
        if (cts.IsCancellationRequested) throw new OperationCanceledException("The benchmark window was closed.");
        return work.Result;
    }

    private (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        var options = request.Options ?? TestOptions.None(Definition); bool rayTraced = options.Get(GpuSceneExecutor.RayTracingOption) == "on";
        if (rayTraced && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve(request, Definition)!))
            throw new GpuUnsupportedException($"{s.AdapterName} does not support DirectX Raytracing 1.1 (inline ray tracing).");
        var (width, height) = GpuSceneExecutor.ParseSize(options.Get(ResolutionOption));
        // The quality is for the rasterised garden and is not offered with rays: a ray-traced run is always at the default one, whatever a stale choice says.
        int quality = !rayTraced && int.TryParse(options.Get(QualityOption), out int q) ? q : 3;
        uint load = (uint)Math.Clamp(quality, 1, 4);
        string mode = rayTraced ? "Direct3D 12 + ray tracing" : "Direct3D 12";
        var nis = NisMode.Parse(options.Get(GpuSceneExecutor.UpscalingOption));
        using var window = new TestWindow($"Mazesta — {mode} — benchmark", width, height, options.Get(GpuSceneExecutor.FullScreenOption) == "on");
        using var view = new SceneView(s, window, width, height, rayTraced, load, null, null, int.TryParse(options.Get(GpuSceneExecutor.SmoothingOption), out int smooth) ? Math.Clamp(smooth, 0, MeshSmoother.MaxLevel) : 0, options.Get(GpuSceneExecutor.WeatherOption) != "off", nis);
        var renderer = view.Renderer; var garden = view.Garden;
        var gpu = GpuSceneExecutor.GpuNode(request.Engine, s.AdapterName);
        view.Overlay.Visible = options.Get(GpuSceneExecutor.OverlayOption) != "off";
        var ram = facts?.Invoke();   // what the RAM runs at, for the readout and the result (null where it is not known)
        var before = view.CheckFrame(CheckTime);
        for (int i = 0; i < 3; i++) { view.DrawFrame(i * Step); view.ShowFrame(); }   // warm-up: first-use costs are not measured

        // The camera follows the clock, as in the visual test, so the walk looks the same however fast the card is; the run lasts whole walks, so
        // every card draws the same route. The time counted is each frame's drawing alone, so the readout, the picture shown, the check frame and
        // the progress report are outside it.
        double runSeconds = Math.Max(10, request.DurationSeconds), walks = runSeconds / GardenCamera.Loop; bool whole = Math.Abs(walks - Math.Round(walks)) < 0.01;
        var hostFrom = request.Clock.UtcNow; double cpuSeconds = 0, gpuSeconds = 0, simSeconds = 0;
        var times = new List<double>(); var total = Stopwatch.StartNew(); var frame = new Stopwatch(); int n = 0;
        var tick = Stopwatch.StartNew(); double tickTime = 0; int tickFrames = 0; double lowestHalfSecond = double.MaxValue; var pace = new FramePace();
        ShowReadout(0, 0);
        while (total.Elapsed.TotalSeconds < runSeconds)
        {
            ct.ThrowIfCancellationRequested();
            if (!window.Pump()) throw new OperationCanceledException("The benchmark window was closed.");
            if (window.OverlayKey()) { view.Overlay.Visible = !view.Overlay.Visible; ShowReadout(0, n > 0 ? n / times.Sum() : 0); }
            float t = (float)total.Elapsed.TotalSeconds; n++;
            frame.Restart();
            view.DrawFrame(t);   // every submission is waited for: the time is the frame's own
            frame.Stop(); cpuSeconds += s.LastRecordSeconds; simSeconds += garden.Weather?.LastSeconds ?? 0; gpuSeconds += s.LastSubmitSeconds; times.Add(frame.Elapsed.TotalSeconds); tickTime += frame.Elapsed.TotalSeconds; tickFrames++; pace.Frame(frame.Elapsed.TotalSeconds);
            view.ShowFrame();
            if (tick.Elapsed.TotalSeconds >= 0.5)
            {
                double fps = tickFrames / tickTime;
                if (total.Elapsed.TotalSeconds > 2) lowestHalfSecond = Math.Min(lowestHalfSecond, fps);
                window.Title = $"Mazesta — {mode} — benchmark — {fps:F0} FPS — {Math.Max(0, (int)(runSeconds - total.Elapsed.TotalSeconds))} s";
                pace.Sample(fps); ShowReadout(fps, n / times.Sum());
                tickTime = 0; tickFrames = 0; tick.Restart();
                request.Report(Math.Min(1, total.Elapsed.TotalSeconds / runSeconds));
            }
        }
        var hostTo = request.Clock.UtcNow;
        // The RAM's own speed in a few seconds, after the timed run (the window is kept alive meanwhile): the score's third part.
        (double Triad, double Latency)? probe = whole && memory is not null ? MeasureRam(window, memory, ct) : null;
        if (view.CheckFrame(CheckTime) != before)
            throw new GpuWrongResultException("The check frame drawn after the run differs from the one drawn before it (same scene, same moment): the GPU computed wrongly under load.");
        double average = n / times.Sum();
        var sorted = times.Order().ToArray();
        var slowest = sorted.TakeLast(Math.Max(1, sorted.Length / 100)).Average();
        double p99 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];
        var r = renderer;
        string how = rayTraced
            ? $"Direct3D 12 with DXR 1.1 ray tracing, quality {quality}: {r.Level.ShadowTaps} shadow ray{(r.Level.ShadowTaps == 1 ? "" : "s")} a pixel to the sun or the moon and one to each lit lamp in reach, ray-traced reflections, ambient occlusion {r.Level.OcclusionTaps} taps, MSAA {r.Samples}x; {garden.Triangles / 1e6:F2} M triangles a frame"
            : $"Direct3D 12, quality {quality}: shadow map {r.Level.ShadowSize}, ambient occlusion {r.Level.OcclusionTaps} taps, pool reflection 1/{r.Level.ReflectionDivisor}, MSAA {r.Samples}x; {garden.Triangles / 1e6:F2} M triangles a frame" + (nis.Scales ? $"; drawn at {r.Width}x{r.Height} and scaled up to {width}x{height} by NVIDIA Image Scaling" : nis.Active ? "; NVIDIA Image Scaling sharpening" : "");
        var metrics = new List<BenchmarkMetric>();
        double cpuFrame = cpuSeconds / n, gpuFrame = gpuSeconds / n;   // the processor's recording and the card's time to finish, per frame
        var setup = new List<SpecItem>();
        if (whole)
        {
            double graphics = SceneScore.Graphics(1 / gpuFrame), cpu = SceneScore.Cpu(1 / cpuFrame);
            double? ramScore = probe is { } p ? SceneScore.Ram(p.Triad, p.Latency) : null;
            if (ramScore is { } rs) metrics.Add(new("Bench_Scene_Score", SceneScore.Overall(graphics, cpu, rs), "pts"));
            metrics.AddRange([new("Bench_Scene_ScoreGpu", graphics, "pts"), new("Bench_Scene_ScoreCpu", cpu, "pts")]);
            if (ramScore is { } r2) metrics.Add(new("Bench_Scene_ScoreRam", r2, "pts"));
            metrics.AddRange([new("Bench_Scene_Frames", n, ""), new("Bench_Scene_GpuFps", 1 / gpuFrame, "FPS"), new("Bench_Scene_CpuFps", 1 / cpuFrame, "FPS")]);
            if (ramScore is { } r3)
            {
                var (sg, sc, sr) = SceneScore.Shares(graphics, cpu, r3);
                metrics.AddRange([new("Bench_Scene_ShareGpu", sg, "%"), new("Bench_Scene_ShareCpu", sc, "%"), new("Bench_Scene_ShareRam", sr, "%"), new("Bench_Scene_RamEffect", SceneScore.RamEffect(graphics, cpu, r3), "%")]);
            }
            setup.Add(new(BenchmarkDetails.RunGroup, "Bench_Set_Limit", SceneScore.Bottleneck(gpuFrame, cpuFrame) switch { SceneScore.Limit.Graphics => "GPU", SceneScore.Limit.Cpu => "CPU", _ => "balanced" }));
        }
        metrics.AddRange([new(whole ? "Bench_Gpu_Scene_Fps" : "Bench_Gpu_Scene_FpsPart", average, "FPS"), new("Bench_Gpu_Scene_Low", 1 / slowest, "FPS"), new("Bench_Gpu_Scene_P99", p99 * 1000, "ms"),
            new("Bench_Scene_GpuFrame", gpuFrame * 1000, "ms"), new("Bench_Scene_CpuFrame", cpuFrame * 1000, "ms")]);
        if (garden.Weather is { } weather) metrics.Add(new("Bench_Scene_SimFrame", simSeconds / n * 1000, "ms"));
        HostMetrics.AddCpu(metrics, request, hostFrom, hostTo); HostMetrics.AddRam(metrics, request, hostFrom, hostTo, ram);
        if (probe is { } pr) metrics.AddRange([new("Bench_Ram_Bandwidth", pr.Triad, "GB/s"), new("Bench_Ram_Latency", pr.Latency, "ns")]);
        s.Setup.AddRange(setup);
        return (metrics,
            $"Persian garden in a window at {width}x{height}, {n} frames: " + (whole ? $"{walks:F0} walk{(Math.Round(walks) == 1 ? "" : "s")} of the garden at walking pace, {runSeconds:F0} s" : $"the first {runSeconds:F0} s of the {GardenCamera.Loop:F0} s walk of the garden (not the whole route: not compared with other runs)") + $"; {how}; {garden.Instances.Length:N0} objects" + (garden.Weather is { } w2 ? $"; weather on {GardenWeather.Threads} threads: {GardenWeather.Rain:N0} raindrops, {GardenWeather.Leaves:N0} leaves, {GardenWeather.Twigs:N0} twigs, a {w2.VoxelBytes / 1048576.0:F0} MB grid of solids" : ""));

        // The readout shows only what was measured: a sensor this card does not report (or has not reported in the last seconds) is left out.
        void ShowReadout(double fps, double mean)
        {
            var now = request.Clock.UtcNow;
            bool running = tickFrames > 0 || n > 0;
            view.Overlay.Update(new($"{(rayTraced ? "D3D12 + RT" : "D3D12")} · benchmark", running && fps > 0 ? fps : null, running && mean > 0 ? mean : null, lowestHalfSecond < double.MaxValue ? lowestHalfSecond : null, pace.Low, pace.Fps, pace.Lows, GpuSceneExecutor.Rows(request.Engine, gpu, now, ram),
                $"{width} × {height}" + (window.Width != width || window.Height != height ? $" → {window.Width} × {window.Height}" : ""),
                $"{(int)total.Elapsed.TotalSeconds}/{runSeconds:F0} s", false));
        }
    }
}
