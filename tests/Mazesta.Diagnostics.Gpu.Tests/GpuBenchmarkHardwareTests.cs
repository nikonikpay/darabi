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

    [Fact] public async Task Direct3D_rasterisation_measures_frames_triangles_and_fill() { if (!NoGpu) Assert.Equal(3, (await Run(new GpuRasterBenchmark())).Metrics.Count); }
    [Fact] public async Task Ray_tracing_measures_frames_and_rays_or_is_unsupported() => await Run(new GpuRayTracingBenchmark());
    [Fact] public async Task Ai_measures_every_precision_the_gpu_supports() => await Run(new GpuAiBenchmark());
    [Fact] public async Task The_garden_measures_its_frame_rate_and_1_percent_low() { if (!NoGpu) Assert.Equal(2, (await Run(new GpuSceneBenchmark(rayTraced: false))).Metrics.Count(m => m.Unit == "FPS")); }
    [Fact] public async Task The_ray_traced_garden_measures_its_frame_rate_or_is_unsupported() => await Run(new GpuSceneBenchmark(rayTraced: true));
    [Fact] public async Task A_cancelled_gpu_benchmark_returns_no_numbers()
    {
        if (NoGpu) return;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var r = await new GpuRasterBenchmark().RunAsync(new TestExecutionRequest(3, new SystemClock(), null, null), cts.Token);
        Assert.Equal(BenchmarkStatus.Cancelled, r.Status); Assert.Empty(r.Metrics);
    }
}
