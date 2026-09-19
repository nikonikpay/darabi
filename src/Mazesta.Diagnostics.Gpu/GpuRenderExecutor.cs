using System.Diagnostics; using ComputeSharp; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// Ray-traced scene benchmark (spec §10, "3-D real light"): a fixed 25-sphere scene at 512x512 with four
/// reflection bounces, rendered repeatedly. The first frame is validated (finite, in range, actually
/// containing an image) and every later frame must equal it exactly - a GPU that renders a different image
/// under load has produced a wrong result. Reports frames per second and megapixels per second; that is this
/// application's own scene, not a commercial benchmark score, and is labelled so.
/// </summary>
public sealed class GpuRenderExecutor : ITestExecutor
{
    public static readonly TestDefinition Definition = new(new TestId("gpu.render"), "Test_Gpu_Render", 30, [GpuDevices.Option]);
    TestDefinition ITestExecutor.Definition => Definition;
    private const int Pixels = 512 * 512, FramesPerBatch = 8;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        var device = GpuDevices.Resolve((request.Options ?? TestOptions.None(Definition)).Get(GpuDevices.OptionKey));
        if (device is null) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "No DirectX 12 hardware GPU is available."));
        return Task.Run(() => Run(request, device, started, ct), CancellationToken.None);
    }

    private static TestRunResult Run(TestExecutionRequest request, GraphicsDevice device, DateTimeOffset started, CancellationToken ct)
    {
        long frames = 0, errors = 0; var clock = Stopwatch.StartNew();
        try
        {
            using var buffer = device.AllocateReadWriteBuffer<float>(Pixels);
            device.For(Pixels, new RayTraceShader(buffer));
            var reference = new float[Pixels]; buffer.CopyTo(reference);
            if (reference.Any(v => !float.IsFinite(v) || v < 0 || v > 2) || reference.Max() - reference.Min() < 0.05f)
                return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, 1, "The first rendered frame is not a valid image (non-finite values or no contrast).");
            var frame = new float[Pixels];
            do
            {
                ct.ThrowIfCancellationRequested();
                using (var context = device.CreateComputeContext())
                    for (int f = 0; f < FramesPerBatch; f++) { context.For(Pixels, new RayTraceShader(buffer)); if (f + 1 < FramesPerBatch) context.Barrier(buffer); }
                buffer.CopyTo(frame);
                frames += FramesPerBatch;
                if (!frame.AsSpan().SequenceEqual(reference)) errors++;
                request.Progress?.Invoke(new TestProgress(Math.Clamp(clock.Elapsed.TotalSeconds / request.DurationSeconds, 0, 1), "Test_Status_Running"));
            }
            while (clock.Elapsed.TotalSeconds < request.DurationSeconds);
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe(device, frames, clock, request, started)); }
        catch (Exception ex) { return new(Definition.Id, TestOutcome.Failed, started, request.Clock.UtcNow, errors + 1, $"GPU error during rendering: {ex.GetType().Name}: {ex.Message}"); }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe(device, frames, clock, request, started));
    }

    private static string Describe(GraphicsDevice device, long frames, Stopwatch clock, TestExecutionRequest request, DateTimeOffset started)
        => SensorEvidence.Join($"ray-traced scene 512x512, 25 spheres, 4 bounces, on {device.Name}", $"frames={frames}", $"{frames / Math.Max(0.001, clock.Elapsed.TotalSeconds):F1} frame/s",
            $"{frames * (double)Pixels / Math.Max(0.001, clock.Elapsed.TotalSeconds) / 1e6:F0} MPixel/s (Mazesta's own scene, not a commercial score)",
            SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuLoad3D, started, request.Clock.UtcNow)?.Format("measured GPU load", "%"));
}
