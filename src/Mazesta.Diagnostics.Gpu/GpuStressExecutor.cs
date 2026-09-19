using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

public enum GpuStressProfile { Steady, Variable, Pulse }

/// <summary>
/// GPU compute stress with three load shapes (spec §10): <b>Steady</b> keeps the GPU saturated,
/// <b>Variable</b> walks through load levels (100, 30, 0, 60 …) to exercise power-state transitions,
/// <b>Pulse</b> alternates full load and idle on a configurable beat to provoke coil whine and PSU
/// transients. Every batch is read back and a sample of its results is recomputed on the CPU - a wrong
/// result is a counted error - and the GPU's own load, temperature and power are measured for the run.
/// </summary>
public sealed class GpuStressExecutor(GpuStressProfile profile) : ITestExecutor
{
    public const string PulseMsOption = "pulseMs", GapMsOption = "gapMs";
    public static readonly TestDefinition Steady = new(new TestId("gpu.steady"), "Test_Gpu_Steady", 60, [GpuDevices.Option]);
    public static readonly TestDefinition Variable = new(new TestId("gpu.variable"), "Test_Gpu_Variable", 60, [GpuDevices.Option]);
    public static readonly TestDefinition Pulse = new(new TestId("gpu.pulse"), "Test_Gpu_Pulse", 60,
        [GpuDevices.Option, new TestOption(PulseMsOption, "Test_Option_PulseMs", TestOptionKind.Integer, "250"), new TestOption(GapMsOption, "Test_Option_GapMs", TestOptionKind.Integer, "250")]);

    public TestDefinition Definition => profile switch { GpuStressProfile.Steady => Steady, GpuStressProfile.Variable => Variable, _ => Pulse };

    private const int Threads = 1 << 21, Rounds = 512, DispatchesPerBatch = 16, FrameMs = 100, VariableStepSeconds = 5, SamplesPerBatch = 512;
    private static readonly double[] VariableLevels = [1, 0.3, 0, 0.6, 1, 0.15, 0.75, 0];

    /// <summary>How long the GPU is kept busy, and how long the frame lasts in all, starting at a moment of the run.
    /// Steady and Variable use 100 ms frames whose busy share is the current level; Pulse is one busy pulse followed
    /// by one idle gap, exactly as configured.</summary>
    internal static (TimeSpan Busy, TimeSpan Frame) Plan(GpuStressProfile profile, double elapsedSeconds, int pulseMs, int gapMs)
    {
        var frame = TimeSpan.FromMilliseconds(FrameMs);
        return profile switch
        {
            GpuStressProfile.Steady => (frame, frame),
            GpuStressProfile.Variable => (frame * VariableLevels[(int)(elapsedSeconds / VariableStepSeconds) % VariableLevels.Length], frame),
            _ => (TimeSpan.FromMilliseconds(pulseMs), TimeSpan.FromMilliseconds(pulseMs + gapMs))
        };
    }

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        var device = GpuDevices.Resolve(options.Get(GpuDevices.OptionKey));
        if (device is null) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "No DirectX 12 hardware GPU is available."));
        int pulse = profile == GpuStressProfile.Pulse ? options.GetInt(PulseMsOption) : 0, gap = profile == GpuStressProfile.Pulse ? options.GetInt(GapMsOption) : 0;
        if (profile == GpuStressProfile.Pulse && (pulse < 1 || gap < 1)) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Pulse and gap lengths must be positive."));
        return Task.Run(() => Run(request, device, pulse, gap, started, ct), CancellationToken.None);
    }

    private TestRunResult Run(TestExecutionRequest request, GraphicsDevice device, int pulseMs, int gapMs, DateTimeOffset started, CancellationToken ct)
    {
        long errors = 0, dispatches = 0, batches = 0;
        var clock = Stopwatch.StartNew(); var random = new Random(0x5EED);
        try
        {
            using var buffer = device.AllocateReadWriteBuffer<uint>(Threads);
            var host = new uint[Threads];
            while (clock.Elapsed.TotalSeconds < request.DurationSeconds)
            {
                ct.ThrowIfCancellationRequested();
                var (busy, frame) = Plan(profile, clock.Elapsed.TotalSeconds, pulseMs, gapMs);
                var frameStart = clock.Elapsed;
                while (clock.Elapsed - frameStart < busy)
                {
                    uint seed = unchecked((uint)(++batches * 2654435761u));
                    using (var context = device.CreateComputeContext())
                        for (int d = 0; d < DispatchesPerBatch; d++) { context.For(Threads, new HashStressShader(buffer, Rounds, seed)); if (d + 1 < DispatchesPerBatch) context.Barrier(buffer); }
                    buffer.CopyTo(host);   // waits for the GPU: the queue never runs away and the results are in hand
                    dispatches += DispatchesPerBatch;
                    for (int s = 0; s < SamplesPerBatch; s++) { int i = random.Next(Threads); if (host[i] != GpuHash.Reference((uint)i, Rounds, seed)) errors++; }
                }
                Report(request, clock);
                if (clock.Elapsed - frameStart < frame) ct.WaitHandle.WaitOne(frame - (clock.Elapsed - frameStart));   // the idle part of the frame; wakes at once on cancel
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(device, dispatches, clock, request, started)); }
        catch (Exception ex)
        {
            // A device that is removed or reset mid-run (driver timeout, overheating, PSU) is exactly the failure this test exists to provoke.
            return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"GPU error during the run: {ex.GetType().Name}: {ex.Message}");
        }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(device, dispatches, clock, request, started));
    }

    private string Describe(GraphicsDevice device, long dispatches, Stopwatch clock, TestExecutionRequest request, DateTimeOffset started)
    {
        var finished = request.Clock.UtcNow;
        double gops = dispatches * (double)Threads * Rounds * 6 / Math.Max(0.001, clock.Elapsed.TotalSeconds) / 1e9;
        return SensorEvidence.Join($"GPU {profile} compute stress on {device.Name}", $"dispatches={dispatches}", $"{gops:F0} Gop/s integer",
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuLoad3D, started, finished)?.Format("measured GPU load", "%"),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished)?.Format("GPU core", "°C", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished)?.Format("GPU hot spot", "°C", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuPower, started, finished)?.Format("GPU power", " W", includeMax: true));
    }

    private static void Report(TestExecutionRequest request, Stopwatch clock)
        => request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));
}
