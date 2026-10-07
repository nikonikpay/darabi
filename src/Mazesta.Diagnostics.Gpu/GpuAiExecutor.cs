using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// The GPU's AI compute as a test: the benchmark's DirectML matrix multiplies at FP32, FP16 and INT8 (the work Windows AI applications hand the
/// card), run for the time asked. It passes when every precision the card supports ran to the end without the device being lost, and says what
/// each measured; the measured GPU load is the evidence that the card was really worked. A card (or DirectML) that cannot run any precision is
/// Unsupported, never a pass. The same work is the Benchmarks page's AI benchmark, where its rates are compared with other systems.
/// </summary>
public sealed class GpuAiExecutor : ITestExecutor, ITestAvailability
{
    public static readonly TestDefinition Definition = new(new TestId("gpu.ai"), "Test_Gpu_Ai", 120, [GpuDevices.Option]);
    TestDefinition ITestExecutor.Definition => Definition;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);
    private readonly GpuAiBenchmark _work = new();

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        var r = await _work.RunAsync(request, ct).ConfigureAwait(false);
        var now = request.Clock.UtcNow;
        string numbers = string.Join(", ", r.Metrics.Where(m => m.Unit is "TFLOPS" or "TOPS").Select(m => $"{m.Key.Replace("Bench_Gpu_Ai_", "")} {m.Value:F1} {m.Unit}"));
        string detail = SensorEvidence.Join(r.Detail, numbers.Length > 0 ? numbers + " (Mazesta's DirectML matrix multiply, not a commercial score)" : null);
        return r.Status switch
        {
            BenchmarkStatus.Completed => new(Definition.Id, TestOutcome.Passed, started, now, 0, detail),
            BenchmarkStatus.Cancelled => TestRunResult.Cancelled(Definition.Id, started, now),
            BenchmarkStatus.Unsupported => TestRunResult.Unsupported(Definition.Id, now, r.Detail ?? ""),
            BenchmarkStatus.Failed => new(Definition.Id, TestOutcome.Failed, started, now, 1, r.Detail),
            _ => new(Definition.Id, TestOutcome.Error, started, now, 0, r.Detail),
        };
    }
}
