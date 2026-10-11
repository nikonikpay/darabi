using Xunit; using Xunit.Abstractions; using Mazesta.Core.Time; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>Runs the GPU benchmarks for a few seconds on the real GPU (Category=Hardware). Each must either measure
/// positive numbers or say plainly that the GPU cannot run it - never complete with nothing, never fail on a healthy card.</summary>
[Trait("Category", "Hardware")]
public class GpuBenchmarkHardwareTests(ITestOutputHelper output)
{
    private static bool NoGpu => GpuDevices.Resolve("") is null;

    private async Task<BenchmarkResult> Run(IBenchmark benchmark)
    {
        var result = await benchmark.RunAsync(new TestExecutionRequest(3, new SystemClock(), null, null, TestOptions.None(benchmark.Definition)), CancellationToken.None);
        output.WriteLine($"{result.Status}: {string.Join(", ", result.Metrics.Select(m => $"{m.Key}={m.Value:F2} {m.Unit}"))}\n{result.Detail}");
        Assert.True(result.Status is BenchmarkStatus.Completed or BenchmarkStatus.Unsupported, result.Detail);
        if (result.Status == BenchmarkStatus.Completed) Assert.All(result.Metrics, m => Assert.True(m.Value > 0, m.Key));
        else Assert.Empty(result.Metrics);
        return result;
    }

    [Fact] public async Task Ray_tracing_measures_frames_and_rays_or_is_unsupported() => await Run(new GpuRayTracingBenchmark());
    [Fact] public async Task Ai_measures_every_precision_the_gpu_supports() => await Run(new GpuAiBenchmark());
    [Fact] public async Task The_garden_measures_its_frame_rate_and_1_percent_low() { if (!NoGpu) Assert.Equal(2, (await Run(new GpuSceneBenchmark())).Metrics.Count(m => m.Unit == "FPS")); }
    /// <summary>The garden's benchmark runs with one frame in flight and with two, and says how busy it kept the card (taken from GPU timestamps); with two it keeps it busier. With MAZESTA_BENCH_SECONDS the run is that long
    /// (162 for a whole walk, which also gives the scores) and MAZESTA_BENCH_WEATHER, MAZESTA_BENCH_RES, MAZESTA_BENCH_RT set the other options.</summary>
    [Theory, InlineData("1"), InlineData("2")]
    public async Task The_garden_says_how_busy_it_kept_the_card_with_one_frame_in_flight_or_two(string frames)
    {
        if (NoGpu) return;
        var b = new GpuSceneBenchmark(); int seconds = int.TryParse(Environment.GetEnvironmentVariable("MAZESTA_BENCH_SECONDS"), out int s0) ? s0 : 12;
        var chosen = new Dictionary<string, string> { ["frames"] = frames, ["resolution"] = Environment.GetEnvironmentVariable("MAZESTA_BENCH_RES") ?? "2560x1440", ["raytracing"] = Environment.GetEnvironmentVariable("MAZESTA_BENCH_RT") ?? "off", ["fullscreen"] = "off",
            ["weather"] = Environment.GetEnvironmentVariable("MAZESTA_BENCH_WEATHER") ?? "off" };
        var result = await b.RunAsync(new TestExecutionRequest(seconds, new SystemClock(), null, null, new TestOptions(b.Definition, chosen)), CancellationToken.None);
        output.WriteLine($"{result.Status} frames={frames}: {string.Join(", ", result.Metrics.Select(m => $"{m.Key}={m.Value:F2} {m.Unit}"))}{Environment.NewLine}{result.Detail}");
        Assert.Equal(BenchmarkStatus.Completed, result.Status);
        double busy = result.Metrics.Single(m => m.Key == "Bench_Scene_GpuBusy").Value;
        Assert.InRange(busy, frames == "2" ? 93 : 60, 100.0);
    }
    [Fact] public async Task A_cancelled_gpu_benchmark_returns_no_numbers()
    {
        if (NoGpu) return;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var r = await new GpuAiBenchmark().RunAsync(new TestExecutionRequest(3, new SystemClock(), null, null), cts.Token);
        Assert.Equal(BenchmarkStatus.Cancelled, r.Status); Assert.Empty(r.Metrics);
    }
}
