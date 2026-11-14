using System.Diagnostics; using System.Globalization; using ComputeSharp;
using Mazesta.Core.Hardware; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The GPU temperature test: a window on the turning star of <see cref="ShamsehScene"/>, drawn with no v-sync, which keeps the card at its power
/// limit with the clock held well under its boost (hundreds of fur layers blended into a float frame), so the
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
        var readout = new FurReadout(scene, request.Engine, gpu, card); var stats = new FurStats(); double checkTime = 0, lastFps = 0; bool afterCheck = false; var recent = new Queue<double>();
        try
        {
            while (total.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) { closed = true; break; }
                if (window.OverlayKey()) scene.Overlay.Visible = !scene.Overlay.Visible;
                if (reference is null || lastCheck.Elapsed.TotalSeconds >= CheckSeconds)
                {
                    long checkStart = Stopwatch.GetTimestamp(); var sum = scene.CheckFrame(CheckTime); checks++; lastCheck.Restart();
                    checkTime += Stopwatch.GetElapsedTime(checkStart).TotalSeconds;   // left out of the frame rate: the figure is the scene's own pace
                    stats.Restart();
                    if (reference is null) reference = sum;
                    else if (!SceneView.Same(sum, reference)) { errors++; if (firstError.Length == 0) firstError = $"check frame {checks} differs from the first one (same scene, same moment)"; }
                }
                scene.Present((float)total.Elapsed.TotalSeconds);
                frames++; updateFrames++; stats.Frame();
                if (updateClock.Elapsed.TotalSeconds >= 0.5)
                {
                    // a half second with a check frame in it is not the scene's pace (the card idles while the check is read back): the last figure stands
                    // (and the half second after it: the card is still getting back to its clock)
                    if ((checkTime == 0 && !afterCheck) || lastFps == 0) lastFps = updateFrames / updateClock.Elapsed.TotalSeconds;
                    afterCheck = checkTime > 0;
                    // the shown figure is the last two seconds averaged: the clock's dither at the power limit does not make it jump
                    recent.Enqueue(lastFps); if (recent.Count > 4) recent.Dequeue(); double fps = recent.Average(); updateFrames = 0; updateClock.Restart(); checkTime = 0;
                    window.Title = $"Mazesta — GPU temperature — {fps:F0} FPS — {(int)(duration - total.Elapsed).TotalSeconds} s";
                    readout.Show(fps, request.Clock.UtcNow, total.Elapsed, duration, stats, $"{w} × {h} · check frames {checks} · errors {errors}", errors > 0);
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe()); }
        finally { readout.Wait(); }   // (the scene is disposed on leaving: the readout is not left being drawn)
        if (closed) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, SensorEvidence.Join("the test window was closed before the time was up", Describe()));
        if (reference is { } r && !SceneView.Same(scene.CheckFrame(CheckTime), r)) { errors++; if (firstError.Length == 0) firstError = "the final check frame differs from the first one"; }   // the end, when the card is hottest, is checked too
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe());

        string Describe()
        {
            var finished = request.Clock.UtcNow; var node = GpuDevices.SensorNode(request.Engine, session.AdapterName);
            var c = SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, node, null); var hs = SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, node, null);
            return SensorEvidence.Join($"GPU temperature scene drawn at {w}x{h} (window {window.Width}x{window.Height}) on {session.AdapterName}, the gate of {scene.Triangles:N0} triangles into a float frame",
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

/// <summary>The frames of a run as a fur test counts them: how many, the average rate, and the slowest and quickest single frame (as rates). The first
/// frames (shaders and clocks settling) count in the total and the average, not in the slowest and quickest; <see cref="Restart"/> after a check frame
/// keeps its time out of the frame that follows.</summary>
internal sealed class FurStats
{
    private const double Settle = 15;   // seconds: the clock and the temperature are still settling at the start, so the slowest and quickest are of the loaded card
    // The slowest and quickest are rates over a quarter second, not of one frame: with two frames in flight a single frame's time is how the CPU
    // happened to be scheduled (a late one is followed by an early one, thousands of FPS for a moment), not what the card drew.
    private const double Window = 1.0;   // seconds
    private readonly long _born = Stopwatch.GetTimestamp(); private long _last = Stopwatch.GetTimestamp(), _windowStart = Stopwatch.GetTimestamp(); private double _busyMs, _minFps = double.MaxValue, _maxFps; private int _inWindow;
    public long Frames { get; private set; }
    public void Restart() { _last = _windowStart = Stopwatch.GetTimestamp(); _inWindow = 0; }
    public void Frame()
    {
        long now = Stopwatch.GetTimestamp(); double ms = (now - _last) * 1000.0 / Stopwatch.Frequency; _last = now; Frames++; _busyMs += ms; _inWindow++;
        double span = (now - _windowStart) / (double)Stopwatch.Frequency; if (span < Window) return;
        double fps = _inWindow / span; _windowStart = now; _inWindow = 0;
        if (Stopwatch.GetElapsedTime(_born).TotalSeconds > Settle) { _minFps = Math.Min(_minFps, fps); _maxFps = Math.Max(_maxFps, fps); }
    }
    public double? Average => _busyMs > 0 ? Frames * 1000 / _busyMs : null;
    public double? Slowest => _minFps < double.MaxValue ? _minFps : null;
    public double? Fastest => _maxFps > 0 ? _maxFps : null;
}

/// <summary>The star's large readout, shared by the temperature test and its benchmark: core and hot spot with their traces, then the card's other
/// figures. Only what was read in the last seconds is shown: a figure this card does not report is a dash. Laid out off the frame's thread, one at a time.</summary>
internal sealed class FurReadout(ShamsehScene scene, Mazesta.Monitoring.PollingEngine? engine, HardwareNode? gpu, string card)
{
    private readonly List<float> _core = [], _hot = []; private int _refreshing;
    private readonly Mazesta.Hardware.Nvidia.NvidiaRunProbe? _probe = Probe(card);   // the driver's version and the PCIe link (NVIDIA only: another maker's card shows none)

    private static Mazesta.Hardware.Nvidia.NvidiaRunProbe? Probe(string card) { try { return Mazesta.Hardware.Nvidia.NvidiaRunProbe.Start(card); } catch (Exception) { return null; } }
    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours} h {t.Minutes:00} m {t.Seconds:00} s";

    public void Show(double fps, DateTimeOffset now, TimeSpan elapsed, TimeSpan total, FurStats stats, string status, bool failing)
    {
        double? Now(SensorRole role) => GpuSceneExecutor.Latest(engine, gpu, role, now);
        double? core = Now(SensorRole.GpuCoreTemp), hot = Now(SensorRole.GpuHotSpotTemp);
        if (core is { } c) Keep(_core, (float)c); if (hot is { } t) Keep(_hot, (float)t);
        static string? F(double? v, string format) => v?.ToString(format, CultureInfo.InvariantCulture);
        var gb = (used: GpuSceneExecutor.Gb(GpuSceneExecutor.Reading(engine, gpu, SensorRole.GpuVramUsed, now)), all: GpuSceneExecutor.Gb(GpuSceneExecutor.Reading(engine, gpu, SensorRole.GpuVramTotal, now)));
        double? vramShare = gb.used is { } u && gb.all is > 0 ? u / gb.all : null, load = Now(SensorRole.GpuLoad3D) ?? Now(SensorRole.GpuLoadD3D3D), powerShare = Now(SensorRole.GpuPowerPercent);
        const uint G = SceneOverlay.GpuHue;
        var cells = new List<SceneOverlay.Cell>
        {
            new("LOAD", F(load, "F0"), "%", G, load / 100), new("POWER", F(Now(SensorRole.GpuPower), "F0"), "W", SceneOverlay.Game, powerShare / 100),
            new("CLOCK", F(Now(SensorRole.GpuCoreClock), "F0"), "MHz", SceneOverlay.CpuHue), new("MEMORY", F(Now(SensorRole.GpuMemoryClock), "F0"), "MHz", SceneOverlay.RamHue),
            new("VRAM °C",F(Now(SensorRole.GpuVramTemp), "F0"), "°C", SceneOverlay.LowHue), new("VOLTAGE", F(Now(SensorRole.GpuVoltage), "F3"), "V", SceneOverlay.Game),
            new("VRAM", gb.used is { } used ? used.ToString("F1", CultureInfo.InvariantCulture) + (gb.all is { } all ? " / " + all.ToString("F0", CultureInfo.InvariantCulture) : "") : null, "GB", SceneOverlay.RamHue),
            new("VRAM %",F(vramShare * 100, "F0"), "%", SceneOverlay.RamHue, vramShare),
        };
        // each fan its own cell with its speed, and its share of the top speed where the monitor reports one (one share for all, or one for each)
        var rpm = GpuSceneExecutor.Each(engine, gpu, SensorRole.GpuFanRpm, now); var share = GpuSceneExecutor.Each(engine, gpu, SensorRole.GpuFanPercent, now);
        for (int i = 0; i < rpm.Count; i++)
        {
            double? p = share.Count == rpm.Count ? share[i].Value : share.Count == 1 ? share[0].Value : null;
            cells.Add(new(rpm.Count > 1 ? "FAN " + (i + 1) : "FAN", rpm[i].Value.ToString("F0", CultureInfo.InvariantCulture), p is { } pc ? "RPM · " + pc.ToString("F0", CultureInfo.InvariantCulture) + "%" : "RPM", SceneOverlay.FanHue, p / 100));
        }
        if (rpm.Count == 0) for (int i = 0; i < share.Count; i++) cells.Add(new(share.Count > 1 ? "FAN " + (i + 1) : "FAN", share[i].Value.ToString("F0", CultureInfo.InvariantCulture), "%", SceneOverlay.FanHue, share[i].Value / 100));
        cells.Add(new("FRAMES", stats.Frames.ToString("N0", CultureInfo.InvariantCulture), "", SceneOverlay.Game));

        var live = _probe?.Now();
        var parts = new List<string>();
        if (live is { Gen: { } g, Width: { } w }) parts.Add($"PCIe Gen {g} x{w}" + (live.Value.MaxGen is { } mg && live.Value.MaxWidth is { } mw ? $" (max Gen {mg} x{mw})" : ""));
        if (live?.Driver is { Length: > 0 } drv) parts.Add("Driver " + drv);
        var heat = new SceneOverlay.Heat(card, string.Join(" · ", parts), Clock(elapsed), Clock(total), elapsed / total, core, hot, [.. _core], [.. _hot],
            fps, stats.Average, stats.Slowest, stats.Fastest, fps > 0 ? 1000 / fps : null, cells, status, failing);
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;   // laying out text takes milliseconds
        Task.Run(() => { try { scene.Overlay.UpdateHeat(heat); } catch (Exception) { } finally { Volatile.Write(ref _refreshing, 0); } });
    }

    /// <summary>Before the scene is disposed: the readout is not left being drawn.</summary>
    public void Wait() { while (Volatile.Read(ref _refreshing) == 1) Thread.Sleep(1); _probe?.Dispose(); }

    private static void Keep(List<float> trace, float v) { trace.Add(v); if (trace.Count > FramePace.Kept * 2) trace.RemoveAt(0); }
}
