using System.Diagnostics; using System.Globalization; using ComputeSharp;
using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The GPU temperature test: a window on the turning star of <see cref="ShamsehScene"/>, drawn with no v-sync, which keeps the card at its power
/// limit with the clock held well under its boost (fur layers blended into a 16-bit frame: the card's memory works as hard as its cores), so the
/// card reaches the heat it would at its worst, with the core, the hot spot and the rest of the card read out large over the picture.
/// A card that only gets hot is not the pass: every <see cref="CheckSeconds"/> the star is also drawn off screen at one fixed moment and read back,
/// and that frame must be the first one (<see cref="SceneView.Same"/>) - a card that draws differently when hot has computed wrongly.
/// </summary>
public sealed class ShamsehThermalExecutor : ITestExecutor, ITestAvailability
{
    public static readonly TestDefinition Definition = new(new TestId("gpu.thermal"), "Test_Gpu_Thermal", 600, [GpuDevices.Option, GpuSceneExecutor.Resolution, GpuSceneExecutor.FullScreen]);
    TestDefinition ITestExecutor.Definition => Definition;
    public Unavailability? CheckAvailability(TestOptions options) => GpuFeatures.GpuAvailability(options);

    private const int CheckSeconds = 20; private const float CheckTime = 2.5f;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var id = Definition.Id;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        if (CheckAvailability(options) is { } no) return Task.FromResult(TestRunResult.Unsupported(id, started, no.Detail));
        var device = GpuDevices.Resolve(request, Definition)!;
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>   // the window, its messages and the drawing on one thread of their own, as the visual test's
        {
            try { done.SetResult(Run(request, options, device, started, ct)); }
            catch (Exception e)
            {
                var now = request.Clock.UtcNow;
                done.SetResult(GpuFault.Of(e) switch
                {
                    GpuFault.Kind.Cancelled => TestRunResult.Cancelled(id, started, now),
                    GpuFault.Kind.Unsupported => TestRunResult.Unsupported(id, now, e.Message),
                    GpuFault.Kind.Lost or GpuFault.Kind.Wrong => new(id, TestOutcome.Failed, started, now, 1, $"GPU error during the visual test: {e.Message}"),   // lost under heat: what the test is for
                    _ => TestRunResult.Error(id, started, now, e),
                });
            }
        }, GpuDevices.RenderStack) { IsBackground = true, Name = "Mazesta GPU temperature" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }

    private static TestRunResult Run(TestExecutionRequest request, TestOptions options, GraphicsDevice device, DateTimeOffset started, CancellationToken ct)
    {
        string res = options.Get(GpuSceneExecutor.ResolutionOption); bool full = options.Get(GpuSceneExecutor.FullScreenOption) == "on";
        var (w, h) = GpuSceneExecutor.ParseSize(res);
        using var session = new D3D12Session(device, 2);
        using var window = new TestWindow("Mazesta — GPU temperature", w, h, full);
        using var scene = new ShamsehScene(session, window, w, h);
        var gpu = GpuSceneExecutor.GpuNode(request.Engine, session.AdapterName);
        string card = session.AdapterName;

        long frames = 0, checks = 0, errors = 0; float[]? reference = null; string firstError = ""; bool closed = false;
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        var lastCheck = Stopwatch.StartNew(); var updateClock = Stopwatch.StartNew(); long updateFrames = 0;
        List<float> coreTrace = [], hotTrace = []; int refreshing = 0;
        try
        {
            while (total.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) { closed = true; break; }
                if (window.OverlayKey()) scene.Overlay.Visible = !scene.Overlay.Visible;
                if (reference is null || lastCheck.Elapsed.TotalSeconds >= CheckSeconds)
                {
                    var sum = scene.CheckFrame(CheckTime); checks++; lastCheck.Restart();
                    if (reference is null) reference = sum;
                    else if (!SceneView.Same(sum, reference)) { errors++; if (firstError.Length == 0) firstError = $"check frame {checks} differs from the first one (same scene, same moment)"; }
                }
                scene.Present((float)total.Elapsed.TotalSeconds);
                frames++; updateFrames++;
                if (updateClock.Elapsed.TotalSeconds >= 0.5)
                {
                    double fps = updateFrames / updateClock.Elapsed.TotalSeconds; updateFrames = 0; updateClock.Restart();
                    window.Title = $"Mazesta — GPU temperature — {fps:F0} FPS — {(int)(duration - total.Elapsed).TotalSeconds} s";
                    Readout(fps);
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe()); }
        finally { while (Volatile.Read(ref refreshing) == 1) Thread.Sleep(1); }   // (the scene is disposed on leaving: the readout is not left being drawn)
        if (closed) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, SensorEvidence.Join("the test window was closed before the time was up", Describe()));
        if (reference is { } r && !SceneView.Same(scene.CheckFrame(CheckTime), r)) { errors++; if (firstError.Length == 0) firstError = "the final check frame differs from the first one"; }   // the end, when the card is hottest, is checked too
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe());

        // only what was read in the last seconds is shown: a figure this card does not report is a dash
        void Readout(double fps)
        {
            var now = request.Clock.UtcNow; var engine = request.Engine;
            double? Now(SensorRole role) => GpuSceneExecutor.Latest(engine, gpu, role, now);
            double? core = Now(SensorRole.GpuCoreTemp), hot = Now(SensorRole.GpuHotSpotTemp);
            if (core is { } c) Keep(coreTrace, (float)c); if (hot is { } t) Keep(hotTrace, (float)t);
            static string? F(double? v, string format) => v?.ToString(format, CultureInfo.InvariantCulture);
            (string, string?, string)[] figures =
            [
                ("VRAM", F(Now(SensorRole.GpuVramTemp), "F0"), "°C"), ("POWER", F(Now(SensorRole.GpuPower), "F0"), "W"), ("CLOCK", F(Now(SensorRole.GpuCoreClock), "F0"), "MHz"),
                ("MEMORY", F(Now(SensorRole.GpuMemoryClock), "F0"), "MHz"), ("VOLTAGE", F(Now(SensorRole.GpuVoltage), "F3"), "V"), ("FAN", F(Now(SensorRole.GpuFanPercent), "F0"), "%"),
                ("LOAD", F(Now(SensorRole.GpuLoad3D) ?? Now(SensorRole.GpuLoadD3D3D), "F0"), "%"), ("FPS", F(fps, "F0"), ""),
            ];
            var heat = new SceneOverlay.Heat(card, core, hot, [.. coreTrace], [.. hotTrace], figures, $"{(int)total.Elapsed.TotalSeconds}/{request.DurationSeconds} s · {w} × {h} · errors {errors}", errors > 0);
            if (Interlocked.Exchange(ref refreshing, 1) == 1) return;   // laying out text takes milliseconds: off the frame's thread, one at a time
            Task.Run(() => { try { scene.Overlay.UpdateHeat(heat); } catch (Exception) { } finally { Volatile.Write(ref refreshing, 0); } });
        }
        static void Keep(List<float> trace, float v) { trace.Add(v); if (trace.Count > FramePace.Kept * 2) trace.RemoveAt(0); }

        string Describe()
        {
            var finished = request.Clock.UtcNow; var node = GpuDevices.SensorNode(request.Engine, session.AdapterName);
            var c = SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, node, null); var hs = SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, node, null);
            return SensorEvidence.Join($"Shamseh fur scene drawn at {w}x{h} (window {window.Width}x{window.Height}) on {session.AdapterName}, {ShamsehScene.Layers} velvet layers and {ShamsehScene.FurLayers} fur layers into a 16-bit frame",
                $"frames={frames}", $"{frames / Math.Max(0.001, total.Elapsed.TotalSeconds):F1} FPS average", $"check frames={checks}", firstError.Length > 0 ? firstError : null,
                SensorEvidence.ReadFirstOf(request.Engine, HardwareKind.Gpu, started, finished, node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D)?.Format("measured GPU load", "%"),
                c?.Format("GPU core", "°C", includeMax: true), hs?.Format("GPU hot spot", "°C", includeMax: true),
                c is { } cc && hs is { } hh ? $"hot spot above core at the peak {hh.Max - cc.Max:F1}°C, on average {hh.Average - cc.Average:F1}°C" : null,
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuVramTemp, started, finished, node, null)?.Format("VRAM temperature", "°C", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, node, null)?.Format("GPU power", " W", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreClock, started, finished, node, null)?.Format("GPU clock", " MHz", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, node, null)?.Format("GPU memory clock", " MHz", includeMax: true));
        }
    }
}
