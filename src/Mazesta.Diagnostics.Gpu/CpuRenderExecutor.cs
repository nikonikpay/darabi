using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Render; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// The processor's render (<see cref="CpuRenderBenchmark"/>'s work: the garden path-traced, progressively, in a window) as a test of the
/// processor: a long, branchy, memory-wide load of the kind a renderer gives it, on every logical processor or on one thread. What makes
/// it a test is that nothing is taken on trust: every tile of every pass is drawn twice - as a rule by two different threads - and the two
/// must be the same bits (<see cref="ProgressiveRender"/>). A tile that differs is an error; a run too short to compare one is inconclusive, not a pass.
/// It lives with the GPU tests only because the garden does. Closing the window cancels the run.
/// </summary>
public sealed class CpuRenderExecutor(bool allThreads) : ITestExecutor
{
    public static readonly TestDefinition Multi = new(new TestId("cpu.render"), "Test_Cpu_Render", 300);
    public static readonly TestDefinition Single = new(new TestId("cpu.render.single"), "Test_Cpu_Render_Single", 300);
    public TestDefinition Definition => allThreads ? Multi : Single;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        return CpuRenderBenchmark.OwnThread(() => Run(request, started, ct));
    }

    private TestRunResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        const int width = CpuRenderBenchmark.Width, height = CpuRenderBenchmark.Height; int threads = allThreads ? Environment.ProcessorCount : 1;
        request.Note("Log_CpuRender_Start", "L = Le + ∫ f·Li·cosθ dω, path traced (sun and lamps asked directly, GGX gloss, up to 8 surfaces a path);  check: every tile of every pass drawn twice, FNV-1a(bits of its samples) equal", threads, width, height);
        var scene = new RenderScene(GardenScene.Embedded);
        var render = new ProgressiveRender(scene, width, height, twice: true); var pacer = new LogPacer();
        bool done = CpuRenderBenchmark.Watch(render, threads, TimeSpan.FromSeconds(request.DurationSeconds), request.Report, ct,
            () => { if (pacer.Due()) request.Note("Log_CpuRender_Progress", null, render.Passes.ToString("F1", CultureInfo.InvariantCulture), render.Checked, render.Wrong); });
        var finished = request.Clock.UtcNow; long wrong = render.Wrong, compared = render.Checked;
        if (wrong > 0) request.NoteError("Log_Wrong_Result", $"{wrong} of {compared} tiles differed between their two drawings");
        string detail = SensorEvidence.Join(string.Create(CultureInfo.InvariantCulture, $"path tracing, {width}x{height} progressive, {scene.Triangles / 1e6:F1} M triangles, on {threads} thread(s); every tile of every pass drawn twice and the two compared bit for bit"),
            $"tiles compared={compared}", $"differing={wrong}", string.Create(CultureInfo.InvariantCulture, $"{render.Rays / Math.Max(0.001, render.Elapsed.TotalSeconds) / 1e6:F2} Mrays/s"),
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuPackagePower, started, finished)?.Format("CPU package power", " W", includeMax: true),
            SensorEvidence.Read(request.Engine, HardwareKind.Cpu, SensorRole.CpuCoreClock, started, finished)?.Format("CPU core clock", " MHz", includeMax: true),
            SensorEvidence.CpuTemperature(request.Engine, started, finished)?.Format("CPU temperature", "°C", includeMax: true));
        if (!done) return new(Definition.Id, TestOutcome.Cancelled, started, finished, wrong, detail);
        if (compared == 0) return new(Definition.Id, TestOutcome.Inconclusive, started, finished, 0, detail + "; the run was too short for one tile to be drawn twice");
        return new(Definition.Id, wrong > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, finished, wrong, detail);
    }
}
