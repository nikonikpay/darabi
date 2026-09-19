using ComputeSharp; using Xunit; using Mazesta.Core.Time; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Gpu;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>Runs the real shaders on the real GPU, so they are excluded from the default run
/// (<c>--filter "Category!=Hardware"</c>) like the other hardware tests; run them with <c>Category=Hardware</c>.
/// A machine with no DirectX 12 GPU has nothing to test and the tests return early.</summary>
[Trait("Category", "Hardware")]
public class GpuHardwareTests
{
    private static readonly IClock Clock = new SystemClock();
    private static bool NoGpu => GpuDevices.Resolve("") is null;
    private static TestExecutionRequest Request(TestDefinition def, int seconds, params (string, string)[] options)
        => new(seconds, Clock, null, null, new TestOptions(def, options.ToDictionary(o => o.Item1, o => o.Item2)));

    [Fact] public void The_gpu_computes_the_same_integers_the_cpu_reference_does()
    {
        if (NoGpu) return;
        var device = GpuDevices.Resolve("")!;
        const int rounds = 64; const uint seed = 12345;
        using var buffer = device.AllocateReadWriteBuffer<uint>(4096);
        device.For(4096, new HashStressShader(buffer, rounds, seed));
        var host = buffer.ToArray();
        for (uint i = 0; i < host.Length; i++) Assert.Equal(GpuHash.Reference(i, rounds, seed), host[i]);
    }

    [Fact] public async Task The_vram_pattern_shader_writes_what_the_cpu_reference_expects_and_finds_no_errors()
    {
        if (NoGpu) return;
        var result = await new GpuVramExecutor().RunAsync(Request(GpuVramExecutor.Definition, 2, (GpuVramExecutor.SizeOption, "512")), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal(0, result.ErrorCount);
        Assert.Contains("tested=512 MiB", result.Detail);
    }
    [Fact] public async Task Steady_stress_passes_with_verified_batches()
    {
        if (NoGpu) return;
        var result = await new GpuStressExecutor(GpuStressProfile.Steady).RunAsync(Request(GpuStressExecutor.Steady, 3), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal(0, result.ErrorCount); Assert.Contains("Gop/s", result.Detail);
    }
    [Fact] public async Task Pulse_stress_passes()
    {
        if (NoGpu) return;
        var result = await new GpuStressExecutor(GpuStressProfile.Pulse).RunAsync(Request(GpuStressExecutor.Pulse, 3), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome);
    }
    [Fact] public async Task The_render_benchmark_produces_an_identical_image_every_frame()
    {
        if (NoGpu) return;
        var result = await new GpuRenderExecutor().RunAsync(Request(GpuRenderExecutor.Definition, 2), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Contains("frame/s", result.Detail);
    }
    [Fact] public async Task The_combined_power_test_runs_cpu_and_gpu_together_and_reports_both()
    {
        if (NoGpu) return;
        var result = await new PowerExecutor(new CpuMatrixStressExecutor(), new GpuStressExecutor(GpuStressProfile.Steady)).RunAsync(Request(PowerExecutor.Definition, 3), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Contains("CPU [Passed]", result.Detail); Assert.Contains("GPU [Passed]", result.Detail);
    }
    [Fact] public async Task Cancelling_a_gpu_test_reports_Cancelled_promptly()
    {
        if (NoGpu) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await new GpuStressExecutor(GpuStressProfile.Steady).RunAsync(Request(GpuStressExecutor.Steady, 60), cts.Token);
        Assert.Equal(TestOutcome.Cancelled, result.Outcome); Assert.True(sw.Elapsed.TotalSeconds < 5);
    }
}
