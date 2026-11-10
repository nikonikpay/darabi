using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Gpu.Render; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// The processor as a renderer: the Persian garden path-traced on the CPU (<see cref="RenderScene"/>), progressively, in a window that shows
/// the picture clearing - the kind of work V-Ray, Corona and Cinebench give a processor, not a bare numeric kernel. On one thread or on every
/// logical processor: two separate benchmarks, as everywhere else. The run lasts the time asked for; what it is ranked by is the rays it sent
/// a second (Corona's measure), with the paths a second and the passes it got through beside it. Building the scene's trees is done before
/// the clock starts and is not in the number. A small check picture drawn before and after must be the same bits, or the run failed: the
/// processor computed wrongly. Mazesta's own renderer and scene - not comparable with other programs' scores. It lives with the GPU tests
/// only because the garden does. Closing the window cancels the run.
/// </summary>
public sealed class CpuRenderBenchmark(bool allThreads) : IBenchmark
{
    public static readonly TestDefinition Single = new(new TestId("bench.cpu.render.single"), "Bench_Render_Single", 60);
    public static readonly TestDefinition Multi = new(new TestId("bench.cpu.render.multi"), "Bench_Render_Multi", 60);
    public TestDefinition Definition => allThreads ? Multi : Single;
    public HardwareKind Component => HardwareKind.Cpu;
    public const int Width = 1280, Height = 720;

    public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow;
        if (request.DurationSeconds <= 0) return Task.FromResult(BenchmarkResult.Unsupported(Definition.Id, started, "Duration must be positive."));
        // a thread of its own: the window is made, pumped and closed on the one thread
        var done = new TaskCompletionSource<BenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { done.SetResult(Run(request, started, ct)); }
            catch (OperationCanceledException) { done.SetResult(BenchmarkResult.Cancelled(Definition.Id, started, request.Clock.UtcNow)); }
            catch (Exception e) { done.SetResult(BenchmarkResult.Error(Definition.Id, started, request.Clock.UtcNow, e)); }
        }) { IsBackground = true, Name = "Mazesta render benchmark" }.Start();
        return done.Task;
    }

    private BenchmarkResult Run(TestExecutionRequest request, DateTimeOffset started, CancellationToken ct)
    {
        int threads = allThreads ? Environment.ProcessorCount : 1; var length = TimeSpan.FromSeconds(request.DurationSeconds);
        var scene = new RenderScene(GardenScene.Embedded);
        ct.ThrowIfCancellationRequested();
        ulong before = ProgressiveRender.Check(scene, threads);
        var render = new ProgressiveRender(scene, Width, Height); var pixels = new uint[Width * Height];
        using var window = new TestWindow($"Mazesta — CPU render — {threads} thread(s)", Width, Height, false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var from = request.Clock.UtcNow;
        var work = Task.Factory.StartNew(() => render.Run(threads, length, cts.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var watch = System.Diagnostics.Stopwatch.StartNew(); long shown = -500;
        while (!work.IsCompleted)
        {
            if (!window.Pump()) cts.Cancel();
            if (watch.ElapsedMilliseconds - shown >= 500) { shown = watch.ElapsedMilliseconds; Blit(window, render, pixels); request.Report(watch.Elapsed / length); }
            Thread.Sleep(30);
        }
        work.GetAwaiter().GetResult();
        var to = request.Clock.UtcNow;
        if (cts.IsCancellationRequested) return BenchmarkResult.Cancelled(Definition.Id, started, to);
        Blit(window, render, pixels); window.Pump();
        ulong after = ProgressiveRender.Check(scene, threads);
        if (after != before) return BenchmarkResult.Failed(Definition.Id, started, request.Clock.UtcNow, "The check picture drawn after the run is not the one drawn before it: the processor computed wrongly during the run.");

        double seconds = Math.Max(0.001, render.Elapsed.TotalSeconds), mrays = render.Rays / seconds / 1e6;
        List<BenchmarkMetric> metrics = [new("Bench_Render_Rays", mrays, "Mrays/s"), new("Bench_Render_Samples", render.Samples / seconds / 1e3, "kS/s"), new("Bench_Render_Passes", render.Passes, "")];
        if (allThreads) metrics.AddRange([new("Bench_Cpu_PerThread", mrays / threads, "Mrays/s"), new("Bench_Threads", threads, "")]);
        CpuBenchmark.AddConditions(metrics, request, allThreads, from, to);
        string detail = string.Create(CultureInfo.InvariantCulture, $"path tracing, {Width}x{Height} progressive, up to {RenderScene.MaxDepth} surfaces a path, {scene.Triangles / 1e6:F1} M triangles ({scene.UniqueTriangles / 1e6:F1} M held once) on {threads} thread(s) for {request.DurationSeconds} s; check picture {before:X16}");
        return new(Definition.Id, BenchmarkStatus.Completed, started, to, metrics, detail);
    }

    private static void Blit(TestWindow window, ProgressiveRender render, uint[] pixels)
    {
        render.Show(pixels);
        var info = new BitmapInfoHeader { Size = 40, Width = Width, Height = -Height, Planes = 1, BitCount = 32 };   // a negative height: the top row first
        IntPtr dc = GetDC(window.Handle); if (dc == IntPtr.Zero) return;
        try { SetStretchBltMode(dc, 4 /* HALFTONE */); _ = StretchDIBits(dc, 0, 0, window.Width, window.Height, 0, 0, Width, Height, pixels, ref info, 0, 0x00CC0020 /* SRCCOPY */); }
        finally { _ = ReleaseDC(window.Handle, dc); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPerMeter, YPerMeter; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, uint[] bits, ref BitmapInfoHeader info, uint usage, uint rop);
}
