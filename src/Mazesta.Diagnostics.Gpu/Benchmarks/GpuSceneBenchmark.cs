using System.Diagnostics; using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The visual tests' Persian garden as a benchmark: the same scene and camera walk, in a window like the test's, drawn with no v-sync cap - by
/// Direct3D 12 rasterisation (shadow map, the pool's reflection, MSAA: set by the quality) or by DirectX Raytracing (camera rays a pixel, each with a
/// soft-shadow ray to the moon and every lamp in reach and a bounced-light ray, reflection and refraction, 4 bounces). The resolution and the quality are
/// options; the defaults (2560x1440, heavy / 4 rays) are what the benchmark measured before they were options, so those runs stay comparable.
/// Every run draws the same tour of 128 views spread over the camera's loop, whole tours only, so a faster card draws the same frames more often, not other
/// ones. Only the drawing of each frame is timed: showing it in the window and the readout over it are not. Reported: the average frame rate, the 1 % low (the
/// frame rate of the average of the slowest hundredth of frames, as CapFrameX and most reviews define it) and the 99th-percentile frame
/// time (the other common definition: 99 % of frames were quicker). The check frames are drawn before and after the timed run, never in it. A frame drawn before the run and again after it at the same moment must be the same
/// bits, or the run failed. Mazesta's own scene - not comparable with other programs' or games' scores. Closing the window cancels the run.
/// </summary>
public sealed class GpuSceneBenchmark(bool rayTraced) : IBenchmark, ITestAvailability
{
    public const string ResolutionOption = "resolution", QualityOption = "quality", DefaultResolution = "2560x1440";
    // The frame is drawn at the chosen size even where the screen is smaller (the window then shows it scaled down), so 4K can be measured on any monitor.
    private static readonly TestOption Resolution = new(ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, DefaultResolution,
        () => [new("1280x720", "1280 × 720 (HD)"), new("1920x1080", "1920 × 1080 (Full HD)"), new(DefaultResolution, "2560 × 1440 (2K)"), new("3840x2160", "3840 × 2160 (4K)")]);
    private static readonly TestOption RasterQuality = new(QualityOption, "Bench_Option_Quality", TestOptionKind.Choice, "3",
        () => [new("1", "Test_GpuLoad_Light", true, "shadows 2048 · no MSAA · no pool reflection"), new("2", "Test_GpuLoad_Medium", true, "shadows 2048 · MSAA 2× · pool reflection 1/2"),
               new("3", "Test_GpuLoad_Heavy", true, "shadows 4096 · MSAA 4× · pool reflection 1/1"), new("4", "Test_GpuLoad_Extreme", true, "shadows 4096 (3 taps) · MSAA 8× · pool reflection 1/1")]);
    private static readonly TestOption RayQuality = new(QualityOption, "Bench_Option_Quality", TestOptionKind.Choice, "4",
        () => [new("1", "Bench_RtQuality_1", true, "1 ray a pixel · 4 bounces"), new("2", "Bench_RtQuality_2", true, "2 rays a pixel · 4 bounces"), new("4", "Bench_RtQuality_4", true, "4 rays a pixel · 4 bounces")]);
    public static readonly TestDefinition Raster = new(new TestId("bench.gpu.scene.d3d"), "Bench_Gpu_SceneD3D", 60, [GpuDevices.Option, Resolution, RasterQuality]);
    public static readonly TestDefinition RayTraced = new(new TestId("bench.gpu.scene.rt"), "Bench_Gpu_SceneRt", 60, [GpuDevices.Option, Resolution, RayQuality]);
    public TestDefinition Definition => rayTraced ? RayTraced : Raster;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => rayTraced ? GpuFeatures.RayTracingAvailability(options) : GpuFeatures.GpuAvailability(options);
    private const float Step = 1 / 30f, CheckTime = 1.234f; private const int Stops = 128;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Definition, request, s => Run(s, request, ct), ownThread: true);

    private (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        if (rayTraced && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve(request, Definition)!))
            throw new GpuUnsupportedException($"{s.AdapterName} does not support DirectX Raytracing 1.1 (inline ray tracing).");
        var options = request.Options ?? TestOptions.None(Definition);
        var (width, height) = GpuSceneExecutor.ParseSize(options.Get(ResolutionOption));
        int quality = int.TryParse(options.Get(QualityOption), out int q) ? q : rayTraced ? 4 : 3;
        uint load = rayTraced ? 3u : (uint)Math.Clamp(quality, 1, 4); int rays = rayTraced ? (quality is 1 or 2 or 4 ? quality : 4) : 4;
        string mode = rayTraced ? "DirectX Raytracing" : "Direct3D 12";
        using var window = new TestWindow($"Mazesta — {mode} — benchmark", width, height, false);
        using var view = new SceneView(s, window, width, height, rayTraced, load, null, null, rays);
        var renderer = view.Renderer; var garden = view.Garden;
        var gpu = GpuSceneExecutor.GpuNode(request.Engine, s.AdapterName);
        string card = $"{s.AdapterName}";
        var before = view.CheckFrame(CheckTime);
        for (int i = 0; i < 3; i++) { view.DrawFrame(i * Step); view.ShowFrame(); }   // warm-up: first-use costs are not measured

        // The same tour on every card: Stops frames spread evenly over the camera's loop, drawn in order, and only whole tours (a faster card
        // draws the tour more times, never other parts of the walk). The time counted is each frame's drawing alone, so the readout, the picture
        // shown, the check frame and the progress report are outside it. A card too slow for one tour in twice the length stops where it is and says so.
        var times = new List<double>(); var total = Stopwatch.StartNew(); var frame = new Stopwatch(); int n = 0;
        var tick = Stopwatch.StartNew(); double tickTime = 0; int tickFrames = 0; double lowestHalfSecond = double.MaxValue;
        ShowReadout(0, 0);
        while (total.Elapsed.TotalSeconds < request.DurationSeconds || n % Stops != 0)
        {
            ct.ThrowIfCancellationRequested();
            if (!window.Pump()) throw new OperationCanceledException("The benchmark window was closed.");
            if (total.Elapsed.TotalSeconds >= request.DurationSeconds * 2) break;
            float t = n++ % Stops * (GardenCamera.Loop / Stops);
            frame.Restart();
            view.DrawFrame(t);   // every submission is waited for: the time is the frame's own
            frame.Stop(); times.Add(frame.Elapsed.TotalSeconds); tickTime += frame.Elapsed.TotalSeconds; tickFrames++;
            view.ShowFrame();
            if (tick.Elapsed.TotalSeconds >= 0.5)
            {
                double fps = tickFrames / tickTime;
                if (total.Elapsed.TotalSeconds > 2) lowestHalfSecond = Math.Min(lowestHalfSecond, fps);
                window.Title = $"Mazesta — {mode} — benchmark — {fps:F0} FPS — {Math.Max(0, (int)(request.DurationSeconds - total.Elapsed.TotalSeconds))} s";
                ShowReadout(fps, n / times.Sum());
                tickTime = 0; tickFrames = 0; tick.Restart();
                request.Report(Math.Min(1, total.Elapsed.TotalSeconds / request.DurationSeconds));
            }
        }
        if (view.CheckFrame(CheckTime) != before)
            throw new GpuWrongResultException("The check frame drawn after the run differs from the one drawn before it (same scene, same moment): the GPU computed wrongly under load.");
        double average = n / times.Sum();
        int tours = n / Stops; bool partial = n % Stops != 0;
        var sorted = times.Order().ToArray();
        var slowest = sorted.TakeLast(Math.Max(1, sorted.Length / 100)).Average();
        double p99 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];
        string how = renderer is GardenRaster r
            ? $"Direct3D 12, quality {quality}: shadow map {r.Level.ShadowSize}, pool reflection 1/{r.Level.ReflectionDivisor}, MSAA {r.Samples}x; {garden.Triangles / 1e6:F2} M triangles a frame"
            : $"DXR 1.1 inline ray tracing: {((GardenRay)renderer).Samples} rays a pixel, soft shadows from the moon and {garden.PointLights.Length} lamps, bounced light, reflection and refraction, {((GardenRay)renderer).Bounces} bounces";
        return ([new("Bench_Gpu_Scene_Fps", average, "FPS"), new("Bench_Gpu_Scene_Low", 1 / slowest, "FPS"), new("Bench_Gpu_Scene_P99", p99 * 1000, "ms")],
            $"Persian garden in a window at {width}x{height}, {n} frames: " + (partial ? $"{tours} whole tours of {Stops} views and a part of one (the card was too slow to finish it in twice the length)" : $"{tours} whole tours of the same {Stops} views of the camera walk") + $"; {how}; {garden.Instances.Length:N0} objects");

        // The readout shows only what was measured: a sensor this card does not report (or has not reported in the last seconds) is left out.
        void ShowReadout(double fps, double mean)
        {
            var now = request.Clock.UtcNow;
            var sensors = new[]
            {
                Reading(SensorRole.GpuCoreTemp, "TEMP", "°C"), Reading(SensorRole.GpuHotSpotTemp, "HOT SPOT", "°C"), Reading(SensorRole.GpuCoreClock, "CLOCK", "MHz"),
                Reading(SensorRole.GpuLoad3D, "LOAD", "%"), Reading(SensorRole.GpuPower, "POWER", "W"), Reading(SensorRole.GpuFanPercent, "FAN", "%")
            }.OfType<SceneOverlay.Tile>().ToList();
            bool running = tickFrames > 0 || n > 0;
            view.Overlay.Update(new($"{mode} · benchmark", running && fps > 0 ? fps : null, running && mean > 0 ? mean : null, lowestHalfSecond < double.MaxValue ? lowestHalfSecond : null, card, sensors,
                $"{width} × {height}" + (window.Width != width || window.Height != height ? $" → {window.Width} × {window.Height}" : ""), view.Work,
                $"{(int)total.Elapsed.TotalSeconds} / {request.DurationSeconds} s · Esc stops", false));
            SceneOverlay.Tile? Reading(SensorRole role, string label, string unit) =>
                GpuSceneExecutor.Latest(request.Engine, gpu, role, now) is { } v ? new(label, v.ToString("F0", CultureInfo.InvariantCulture), unit) : null;
        }
    }
}
