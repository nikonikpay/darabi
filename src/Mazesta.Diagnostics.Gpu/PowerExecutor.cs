using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// Power-delivery test (spec §10): CPU matrix load and steady GPU compute stress at the same time, the
/// worst realistic draw a PSU sees. Each side keeps its own verification, so a fault on either shows; a
/// failure on one side does not stop the other (spec §8 asks that a worker error cancel its peers - that is
/// the queue's cancellation, which both halves already honour). Without a PSU sensor nothing is claimed
/// about the PSU: the evidence is the CPU package and GPU power the machine itself reports.
/// </summary>
public sealed class PowerExecutor(CpuMatrixStressExecutor cpu, GpuStressExecutor gpu) : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("power.combined"), "Test_Power_Combined", 60, [GpuDevices.Option]);
    TestDefinition ITestExecutor.Definition => Definition;

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive.");
        var cpuTask = cpu.RunAsync(request, ct);
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
