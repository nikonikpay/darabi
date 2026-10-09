using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

public enum GpuStressProfile { Steady, Variable, Pulse }

/// <summary>
/// GPU compute stress with three load shapes (spec §10): <b>Steady</b> keeps the GPU saturated,
/// <b>Variable</b> walks through load levels (100, 30, 0, 60 …) to exercise power-state transitions,
/// <b>Pulse</b> alternates full load and idle on a configurable beat to provoke coil whine and PSU
/// transients. A batch is many dispatches, each continuing from the previous one's output, so the value a thread
/// ends with depends on every dispatch of the batch; a sample of those end values is recomputed on the CPU through
/// the whole chain - a wrong result in any dispatch is a counted error. It is a sample of the threads, and the
/// result says how many were checked. The GPU's own load, temperature and power are measured for the run.
/// </summary>
public sealed class GpuStressExecutor(GpuStressProfile profile) : ITestExecutor, ITestAvailability
{
    public const string PulseMsOption = "pulseMs", GapMsOption = "gapMs";
    public static readonly TestDefinition Steady = new(new TestId("gpu.steady"), "Test_Gpu_Steady", 300, [GpuDevices.Option]);
    public static readonly TestDefinition Variable = new(new TestId("gpu.variable"), "Test_Gpu_Variable", 300, [GpuDevices.Option]);
    public static readonly TestDefinition Pulse = new(new TestId("gpu.pulse"), "Test_Gpu_Pulse", 300,
        [GpuDevices.Option, new TestOption(PulseMsOption, "Test_Option_PulseMs", TestOptionKind.Integer, "250"), new TestOption(GapMsOption, "Test_Option_GapMs", TestOptionKind.Integer, "250")]);

    public TestDefinition Definition => profile switch { GpuStressProfile.Steady => Steady, GpuStressProfile.Variable => Variable, _ => Pulse };
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);

    private const int Threads = 1 << 21, Rounds = 512, FrameMs = 100, VariableStepSeconds = 5, SamplesPerBatch = 128;
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

    /// <summary>Steady keeps one long submission after another (the readback between them is then a few percent); the shaped profiles keep a
    /// submission within half of their busy window, so a 100 ms frame at 30 % or a 250 ms pulse keeps its shape.</summary>
    internal static TimeSpan BatchTarget(GpuStressProfile profile, TimeSpan busy)
        => profile == GpuStressProfile.Steady ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(Math.Max(5, busy.TotalMilliseconds / 2));

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        var device = GpuDevices.Resolve(request, Definition);
        if (device is null) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, GpuDevices.NoGpu));
        int pulse = profile == GpuStressProfile.Pulse ? options.GetInt(PulseMsOption) : 0, gap = profile == GpuStressProfile.Pulse ? options.GetInt(GapMsOption) : 0;
        if (profile == GpuStressProfile.Pulse && (pulse < 1 || gap < 1)) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Pulse and gap lengths must be positive."));
        return Task.Run(() => Run(request, device, pulse, gap, started, ct), CancellationToken.None);
    }

    private TestRunResult Run(TestExecutionRequest request, GraphicsDevice device, int pulseMs, int gapMs, DateTimeOffset started, CancellationToken ct)
    {
        long errors = 0, dispatches = 0, batches = 0, checkedResults = 0;
        var clock = Stopwatch.StartNew(); var random = new Random(0x5EED); var sizer = new BatchSizer(); var pacer = new LogPacer();
        request.Note("Log_Gpu_Start", $"{Threads:N0} threads × {Rounds} rounds per dispatch:  h ← prev ⊕ seed;  h = h·1664525 + 1013904223;  h ^= h≫13;  h *= 0x5BD1E995;  h ^= h≫15   check: {SamplesPerBatch} threads per batch recomputed on the CPU through every dispatch", profile.ToString(), device.Name);
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
                    uint seed = unchecked((uint)(++batches * 2654435761u)); int count = sizer.Count; var batchStart = clock.Elapsed;
                    using (var context = device.CreateComputeContext())
                        for (int d = 0; d < count; d++) { context.For(Threads, new HashStressShader(buffer, Rounds, seed, d > 0 ? 1 : 0)); if (d + 1 < count) context.Barrier(buffer); }
                    buffer.CopyTo(host);   // waits for the GPU: the queue never runs away and the results are in hand
                    dispatches += count;
                    errors += CheckSample(host, random, seed, count); checkedResults += (long)SamplesPerBatch * count;
                    sizer.Record(clock.Elapsed - batchStart, BatchTarget(profile, busy));
                }
                Report(request, clock);
                if (pacer.Due()) request.Note("Log_Gpu_Progress", null, dispatches, dispatches * (double)Threads * Rounds * 6 / Math.Max(0.001, clock.Elapsed.TotalSeconds) / 1e9, checkedResults, errors);
                if (clock.Elapsed - frameStart < frame) ct.WaitHandle.WaitOne(frame - (clock.Elapsed - frameStart));   // the idle part of the frame; wakes at once on cancel
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(device, dispatches, checkedResults, clock, request, started)); }
        catch (Exception ex)
        {
            GpuDevices.Invalidate();   // the card is opened afresh for the next run
            // A device that is removed or reset mid-run (driver timeout, overheating, PSU) is exactly the failure this test exists to provoke.
            return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"GPU error during the run: {ex.GetType().Name}: {ex.Message}");
        }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(device, dispatches, checkedResults, clock, request, started));
    }

    private string Describe(GraphicsDevice device, long dispatches, long checkedResults, Stopwatch clock, TestExecutionRequest request, DateTimeOffset started)
    {
        var finished = request.Clock.UtcNow; var card = GpuDevices.SensorNode(request.Engine, device.Name);
        double gops = dispatches * (double)Threads * Rounds * 6 / Math.Max(0.001, clock.Elapsed.TotalSeconds) / 1e9;
        return SensorEvidence.Join($"GPU {profile} compute stress on {device.Name}", $"dispatches={dispatches}", $"{gops:F0} Gop/s integer",
            $"verified {checkedResults:N0} of {dispatches * (double)Threads:N0} thread results through chained dispatches (a sample, not every thread)",
            SensorEvidence.ReadFirstOf(request.Engine, HardwareKind.Gpu, started, finished, card, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D)?.Format("measured GPU load", "%"),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, card, null)?.Format("GPU core", "°C", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, card, null)?.Format("GPU hot spot", "°C", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, card, null)?.Format("GPU power", " W", includeMax: true));
    }

    /// <summary>Recomputes <see cref="SamplesPerBatch"/> random threads through all <paramref name="dispatches"/> of a batch on the CPU cores and
    /// counts those whose end value differs. Parallel, so a long chain on a fast card does not leave the GPU idle for long.</summary>
    internal static long CheckSample(uint[] host, Random random, uint seed, int dispatches)
    {
        var picks = new int[SamplesPerBatch];
        for (int s = 0; s < picks.Length; s++) picks[s] = random.Next(host.Length);
        long wrong = 0;
        Parallel.For(0, picks.Length, s => { int i = picks[s]; if (host[i] != GpuHash.Chained((uint)i, Rounds, seed, dispatches)) Interlocked.Increment(ref wrong); });
        return wrong;
    }

    private static void Report(TestExecutionRequest request, Stopwatch clock)
        => request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));
}
