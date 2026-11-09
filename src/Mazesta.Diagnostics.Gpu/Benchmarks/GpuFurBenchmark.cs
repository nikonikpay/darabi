using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The GPU temperature test (<see cref="ShamsehScene"/>, shown to the user as "GPU temperature", never by the star's name) as a benchmark: the same fur and aurora, drawn with no v-sync at the card's power limit,
/// ranked by its frame rate. Beside it, how steady that rate was (the slowest and quickest half second after the first few, and their spread) and,
/// from the card's sensors, its power, clocks and temperatures: a card that throttles as it heats shows it as a wider spread. The star swings only a
/// little, so the frame's work is nearly the same from start to end and the spread is the card's, not the scene's. A frame drawn before the run and
/// again after it at the same moment must be the same bits, or the run failed. Closing the window cancels the run.
/// </summary>
public sealed class GpuFurBenchmark : IBenchmark, ITestAvailability
{
    private static readonly TestOption Resolution = new(GpuSceneExecutor.ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, "1920x1080", GpuSceneExecutor.Sizes);
    public static readonly TestDefinition Fur = new(new TestId("bench.gpu.fur"), "Bench_Gpu_Fur", 60, [GpuDevices.Option, Resolution, GpuSceneExecutor.FullScreen]);
    public TestDefinition Definition => Fur;
    public HardwareKind Component => HardwareKind.Gpu;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private const float CheckTime = 2.5f;
    /// <summary>The first seconds are the card warming its clocks, not its pace: they count in the average, not in the slowest and quickest.</summary>
    private const double Settle = 5;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => GpuBenchmark.RunAsync(Definition, request, s => Run(s, request, ct), ownThread: true, frames: 2);

    private static (List<BenchmarkMetric>, string) Run(D3D12Session s, TestExecutionRequest request, CancellationToken ct)
    {
        var options = request.Options ?? TestOptions.None(Fur);
        var (w, h) = GpuSceneExecutor.ParseSize(options.Get(GpuSceneExecutor.ResolutionOption));
        using var window = new TestWindow("Mazesta — GPU temperature — benchmark", w, h, options.Get(GpuSceneExecutor.FullScreenOption) == "on");
        using var scene = new ShamsehScene(s, window, w, h);
        var readout = new FurReadout(scene, request.Engine, GpuSceneExecutor.GpuNode(request.Engine, s.AdapterName), s.AdapterName);
        var before = scene.CheckFrame(CheckTime);
        double runSeconds = Math.Max(10, request.DurationSeconds); long n = 0, tickFrames = 0; double low = double.MaxValue, high = 0;
        var total = Stopwatch.StartNew(); var tick = Stopwatch.StartNew(); var from = request.Clock.UtcNow;
        try
        {
            while (total.Elapsed.TotalSeconds < runSeconds)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) throw new OperationCanceledException("The benchmark window was closed.");
                if (window.OverlayKey()) scene.Overlay.Visible = !scene.Overlay.Visible;
                scene.Present((float)total.Elapsed.TotalSeconds); n++; tickFrames++;
                if (tick.Elapsed.TotalSeconds >= 0.5)
                {
                    double fps = tickFrames / tick.Elapsed.TotalSeconds; tickFrames = 0; tick.Restart();
                    if (total.Elapsed.TotalSeconds > Settle) { low = Math.Min(low, fps); high = Math.Max(high, fps); }
                    window.Title = $"Mazesta — GPU temperature — benchmark — {fps:F0} FPS — {Math.Max(0, (int)(runSeconds - total.Elapsed.TotalSeconds))} s";
                    readout.Show(fps, request.Clock.UtcNow, $"{(int)total.Elapsed.TotalSeconds}/{runSeconds:F0} s · {w} × {h}", false);
                    request.Report(total.Elapsed.TotalSeconds / runSeconds);
                }
            }
        }
        finally { readout.Wait(); }
        double wall = total.Elapsed.TotalSeconds, average = n / wall; var to = request.Clock.UtcNow;
        if (!SceneView.Same(scene.CheckFrame(CheckTime), before))
            throw new GpuWrongResultException("The check frame drawn after the run differs from the one drawn before it (same scene, same moment): the GPU computed wrongly under load.");
        var metrics = new List<BenchmarkMetric> { new("Bench_Fur_Fps", average, "FPS") };
        if (high > 0) metrics.AddRange([new("Bench_Fur_FpsHigh", high, "FPS"), new("Bench_Fur_FpsLow", low, "FPS"), new("Bench_Fur_Spread", (high - low) / average * 100, "%")]);
        // The card's heat, clock and power over the timed run, in the row beside the frame rate (each says whether it is the average or the peak);
        // a sensor this card does not have adds nothing. The same figures also stand among the run's conditions, as every GPU benchmark's.
        var node = GpuBenchmark.Node(request, s.AdapterName);
        foreach (var (role, key, unit, peak) in new[] {
            (SensorRole.GpuCoreTemp, "Bench_Fur_Read_TempAvg", Unit.Celsius, false), (SensorRole.GpuCoreTemp, "Bench_Fur_Read_TempMax", Unit.Celsius, true),
            (SensorRole.GpuHotSpotTemp, "Bench_Fur_Read_HotAvg", Unit.Celsius, false), (SensorRole.GpuHotSpotTemp, "Bench_Fur_Read_HotMax", Unit.Celsius, true),
            (SensorRole.GpuVramTemp, "Bench_Fur_Read_VramAvg", Unit.Celsius, false), (SensorRole.GpuVramTemp, "Bench_Fur_Read_VramMax", Unit.Celsius, true),
            (SensorRole.GpuCoreClock, "Bench_Fur_Read_Clock", Unit.MegaHertz, false), (SensorRole.GpuPower, "Bench_Fur_Read_Power", Unit.Watt, false), (SensorRole.GpuPower, "Bench_Fur_Read_PowerMax", Unit.Watt, true) })
            metrics.AddSensor(request, HardwareKind.Gpu, role, from, to, key, unit, peak, node);
        return (metrics, $"GPU temperature scene drawn at {w}x{h} (window {window.Width}x{window.Height}), {ShamsehScene.Layers} aurora layers and {ShamsehScene.FurLayers} fur layers into a float frame, {n} frames in {wall:F0} s, no v-sync; slowest and quickest half second counted after the first {Settle:F0} s");
    }
}
