using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// Power-delivery test (spec §10): a CPU load and steady GPU compute stress at the same time, the
/// worst realistic draw a PSU sees. The CPU's side is the vector multiply-add load, the one that draws the most the processor can (the matrix
/// load it once was is scalar work, well under that), and five minutes by default: a supply or a VRM that gives out does so warm, not in the first minute. Each side keeps its own verification, so a fault on either shows; a
/// failure on one side does not stop the other (spec §8 asks that a worker error cancel its peers - that is
/// the queue's cancellation, which both halves already honour). Without a PSU sensor nothing is claimed
/// about the PSU: the evidence is the CPU package and GPU power the machine itself reports.
/// </summary>
public sealed class PowerExecutor(ITestExecutor cpu, GpuStressExecutor gpu) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("power.combined"), "Test_Power_Combined", 300, [GpuDevices.Option]);
    TestDefinition ITestExecutor.Definition => Definition;

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");
        request.Note("Log_Power_Start", "CPU: widest vector multiply-add load on every thread  +  GPU: steady compute stress, at the same time");
        var cpuTask = cpu.RunAsync(request with { Options = null }, ct);   // the CPU's test takes its own defaults (the widest vectors), not the power test's options
        var gpuTask = gpu.RunAsync(request, ct);
        var cpuResult = await cpuTask.ConfigureAwait(false); var gpuResult = await gpuTask.ConfigureAwait(false);
        var finished = request.Clock.UtcNow;

        var combined = cpuResult.Combine(gpuResult);
        // The GPU half already reports the GPU's own power; only the CPU package power is new here.
        string? cpuPower = SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true);
        return new(Definition.Id, combined.Outcome, started, finished, combined.ErrorCount,
            SensorEvidence.Join($"CPU [{cpuResult.Outcome}]: {cpuResult.Detail}", $"GPU [{gpuResult.Outcome}]: {gpuResult.Detail}", cpuPower));
    }
}
