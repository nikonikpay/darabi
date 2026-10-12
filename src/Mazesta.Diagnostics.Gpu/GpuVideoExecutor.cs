using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// The card's video decoder or encoder as a test: every form the benchmark measures (codec, resolution, 8 or 10 bits, 4:2:0 or 4:2:2), each
/// checked frame for frame - a decoded pass must give back every frame of the clip, an encoded file must play back with every frame it was given.
/// A lost frame fails the test; a form the card has no engine for is named in the result, never counted as passed or failed. A card with no
/// video engine Windows can use is Unsupported.
/// </summary>
public sealed class GpuVideoExecutor(VideoWork work) : ITestExecutor, ITestAvailability
{
    public static readonly TestDefinition Decode = new(new TestId("gpu.video.decode"), "Test_Gpu_VideoDecode", 120, [GpuDevices.Option]);
    public static readonly TestDefinition Encode = new(new TestId("gpu.video.encode"), "Test_Gpu_VideoEncode", 120, [GpuDevices.Option]);
    public TestDefinition Definition => work == VideoWork.Encode ? Encode : Decode;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);

    public async Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        var r = await GpuVideoBenchmark.RunAsync(Definition, work, request, ct).ConfigureAwait(false);
        var now = request.Clock.UtcNow;
        return r.Status switch
        {
            BenchmarkStatus.Completed => new(Definition.Id, TestOutcome.Passed, started, now, 0, r.Detail),
            BenchmarkStatus.Cancelled => TestRunResult.Cancelled(Definition.Id, started, now),
            BenchmarkStatus.Unsupported => TestRunResult.Unsupported(Definition.Id, now, r.Detail ?? ""),
            BenchmarkStatus.Failed => new(Definition.Id, TestOutcome.Failed, started, now, 1, r.Detail),
            _ => new(Definition.Id, TestOutcome.Error, started, now, 0, r.Detail),
        };
    }
}
