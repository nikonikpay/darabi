using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The visual tests' Persian garden as a benchmark: the same scene and camera walk, drawn off screen at 2560x1440 with no window and no
/// v-sync cap - by Direct3D 12 rasterisation at the "heavy" level (4096 shadow map, the pool's reflection at full size, 4x MSAA) or by
/// DirectX Raytracing (4 camera rays a pixel, each with a soft-shadow ray to the moon and every lamp in reach and a bounced-light ray,
/// reflection and refraction, 4 bounces). Every run draws the same
/// tour of 128 views spread over the camera's loop, whole tours only, so a faster card draws the same frames more often, not other ones. Reported: the average frame rate, the 1 % low (the
/// frame rate of the average of the slowest hundredth of frames, as CapFrameX and most reviews define it) and the 99th-percentile frame
/// time (the other common definition: 99 % of frames were quicker). The check frames are drawn before and after the timed run, never in it. A frame drawn before the run and again after it at the same moment must be the same
/// bits, or the run failed. Mazesta's own scene - not comparable with other programs' or games' scores.
/// </summary>
public sealed class GpuSceneBenchmark(bool rayTraced) : IBenchmark, ITestAvailability
{
    public static readonly TestDefinition Raster = new(new TestId("bench.gpu.scene.d3d"), "Bench_Gpu_SceneD3D", 60, [GpuDevices.Option]);
    public static readonly TestDefinition RayTraced = new(new TestId("bench.gpu.scene.rt"), "Bench_Gpu_SceneRt", 60, [GpuDevices.Option]);
    public TestDefinition Definition => rayTraced ? RayTraced : Raster;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => rayTraced ? GpuFeatures.RayTracingAvailability(options) : GpuFeatures.GpuAvailability(options);
    private const int Width = 2560, Height = 1440; private const uint Load = 3; private const float Step = 1 / 30f, CheckTime = 1.234f; private const int Stops = 128;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Definition, request, s => Run(s, request, ct), (Width, Height));

    private (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        if (rayTraced && !GpuFeatures.SupportsInlineRayTracing(GpuDevices.Resolve(request, Definition)!))
            throw new GpuUnsupportedException($"{s.AdapterName} does not support DirectX Raytracing 1.1 (inline ray tracing).");
        var garden = new GardenGpu(s, GardenScene.Embedded, rayTraced ? GardenScene.Mode.RayTraced : GardenScene.Mode.Raster);
        using GardenRenderer renderer = rayTraced ? new GardenRay(s, garden, Width, Height, []) : new GardenRaster(s, garden, Width, Height, [], Load);
        var before = renderer.Capture(CheckTime);
        for (int i = 0; i < 3; i++) s.Run(l => renderer.Draw(l, i * Step, renderer.OwnTarget));   // warm-up: first-use costs are not measured

        // The same tour on every card: Stops frames spread evenly over the camera's loop, drawn in order, and only whole tours (a faster card
        // draws the tour more times, never other parts of the walk). The time counted is the frames' own, so the check frame and the progress
        // report are outside it. A card too slow for one tour in twice the length stops where it is and says so.
        var times = new List<double>(); var total = Stopwatch.StartNew(); var frame = new Stopwatch(); int n = 0;
        while (total.Elapsed.TotalSeconds < request.DurationSeconds || n % Stops != 0)
        {
            ct.ThrowIfCancellationRequested();
            if (total.Elapsed.TotalSeconds >= request.DurationSeconds * 2) break;
            float t = n++ % Stops * (GardenCamera.Loop / Stops);
            frame.Restart();
            s.Run(l => renderer.Draw(l, t, renderer.OwnTarget));   // every submission is waited for: the time is the frame's own
            frame.Stop(); times.Add(frame.Elapsed.TotalSeconds);
            request.Report(Math.Min(1, total.Elapsed.TotalSeconds / request.DurationSeconds));
        }
        if (!renderer.Capture(CheckTime).AsSpan().SequenceEqual(before))
            throw new GpuWrongResultException("The check frame drawn after the run differs from the one drawn before it (same scene, same moment): the GPU computed wrongly under load.");
        double fps = n / times.Sum();
        int tours = n / Stops; bool partial = n % Stops != 0;
        var sorted = times.Order().ToArray();
        var slowest = sorted.TakeLast(Math.Max(1, sorted.Length / 100)).Average();
        double p99 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)];
        string how = renderer is GardenRaster r
            ? $"Direct3D 12, heavy level: shadow map {r.Level.ShadowSize}, pool reflection 1/{r.Level.ReflectionDivisor}, MSAA {r.Samples}x; {garden.Triangles / 1e6:F2} M triangles a frame"
            : $"DXR 1.1 inline ray tracing: {((GardenRay)renderer).Samples} rays a pixel, soft shadows from the moon and {garden.PointLights.Length} lamps, bounced light, reflection and refraction, {((GardenRay)renderer).Bounces} bounces";
        return ([new("Bench_Gpu_Scene_Fps", fps, "FPS"), new("Bench_Gpu_Scene_Low", 1 / slowest, "FPS"), new("Bench_Gpu_Scene_P99", p99 * 1000, "ms")],
            $"Persian garden off screen at {Width}x{Height}, {n} frames: " + (partial ? $"{tours} whole tours of {Stops} views and a part of one (the card was too slow to finish it in twice the length)" : $"{tours} whole tours of the same {Stops} views of the camera walk") + $"; {how}; {garden.Instances.Length} objects; check frame identical before and after");
    }
}
