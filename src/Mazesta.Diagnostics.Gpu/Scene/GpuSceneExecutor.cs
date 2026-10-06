using System.Diagnostics; using System.Globalization; using System.Numerics; using System.Runtime.InteropServices; using ComputeSharp;
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Diagnostics.Evidence; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The visual GPU test: a window opens on the Persian garden (<see cref="GardenScene"/>) - a walled courtyard with its pool and fountain,
/// columned hall, cypresses and lanterns, the Mazesta logo floating over the water (or the owner's Models\gpu-test.obj in its place) - and
/// the camera walks round it and through the hall, drawn as fast as the GPU can with no v-sync cap, so the card runs at full load while the
/// technician watches the picture for artefacts and the monitor records clocks, power and temperature. In one walk the garden goes
/// through a day (<see cref="GardenDay"/>): morning on the way up it, the afternoon sun coming in at the hall's stained windows, sunset,
/// and the way back by night with the lamps lit. Drawn by Direct3D 12 (shadow maps, sky map, ambient occlusion, the pool's
/// reflection, MSAA - set by the load level); with the ray-tracing option on (DXR 1.1, on a GPU that has it) the same frame finds
/// its shadows and mirror images with rays instead: one to the sun or the moon and to each lit lamp from every pixel, one along what
/// water, glass and polished surfaces mirror.
/// A picture that merely looks right is not the pass: every few seconds the scene is also drawn off screen at one fixed moment and read
/// back, and that frame must be bit-for-bit the first one - a GPU that draws the same frame differently under load has computed wrongly.
/// </summary>
public sealed class GpuSceneExecutor : ITestExecutor, ITestAvailability
{
    public const string ResolutionOption = "resolution", LoadOption = "load", RayTracingOption = "raytracing";
    /// <summary>Ray tracing on or off: the same garden, its shadows and mirror images found with rays (DXR 1.1) instead of maps.</summary>
    internal static readonly TestOption RayTracing = new(RayTracingOption, "Test_Option_RayTracing", TestOptionKind.Choice, "off",
        () => [new("off", "Test_RayTracing_Off", true, "shadow maps, pictures of the surroundings"), new("on", "Test_RayTracing_On", true, "ray-traced shadows and reflections (DXR 1.1)")]);
    // The frame is drawn at the chosen size even where the screen is smaller (the window then shows it scaled down), so 4K load can be
    // tested on any monitor; full screen draws at the screen's own size.
    private static readonly TestOption Resolution = new(ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, "1920x1080",
        () => [new("1280x720", "1280 × 720 (HD)"), new("1920x1080", "1920 × 1080 (Full HD)"), new("2560x1440", "2560 × 1440 (2K)"), new("3840x2160", "3840 × 2160 (4K)"),
               new("fullscreen", "Test_Resolution_FullScreen", true)]);
    public static readonly TestDefinition Scene = new(new TestId("gpu.scene.d3d"), "Test_Gpu_Scene3D", 300,
        [GpuDevices.Option, Resolution, new TestOption(LoadOption, "Test_Option_GpuLoad", TestOptionKind.Choice, "3",
            () => [new("1", "Test_GpuLoad_Light", true), new("2", "Test_GpuLoad_Medium", true), new("3", "Test_GpuLoad_Heavy", true), new("4", "Test_GpuLoad_Extreme", true)]), RayTracing]);
    public TestDefinition Definition => Scene;

    public Unavailability? CheckAvailability(TestOptions options) => options.Get(RayTracingOption) == "on" ? GpuFeatures.RayTracingAvailability(options) : GpuFeatures.GpuAvailability(options);

    private const int CheckSeconds = 3; private const float CheckTime = 1.234f;

    public Task<TestRunResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
    {
        var started = request.Clock.UtcNow; var id = Definition.Id;
        if (request.DurationSeconds <= 0) return Task.FromResult(TestRunResult.Unsupported(id, started, "Duration must be positive."));
        var options = request.Options ?? TestOptions.None(Definition);
        if (CheckAvailability(options) is { } no) return Task.FromResult(TestRunResult.Unsupported(id, started, no.Detail));
        var device = GpuDevices.Resolve(request, Definition)!;
        var done = new TaskCompletionSource<TestRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The window, its messages and the rendering share one thread of their own, for the whole run.
        var thread = new Thread(() =>
        {
            try { done.SetResult(Run(request, options, device, started, ct)); }
            catch (Exception e)
            {
                var now = request.Clock.UtcNow;
                done.SetResult(Benchmarks.GpuFault.Of(e) switch
                {
                    Benchmarks.GpuFault.Kind.Cancelled => TestRunResult.Cancelled(id, started, now),
                    Benchmarks.GpuFault.Kind.Unsupported => TestRunResult.Unsupported(id, now, e.Message),
                    Benchmarks.GpuFault.Kind.Lost or Benchmarks.GpuFault.Kind.Wrong => new(id, TestOutcome.Failed, started, now, 1, $"GPU error during the visual test: {e.Message}"),
                    _ => TestRunResult.Error(id, started, now, e),   // a window, a shader or a file: the program's fault, not the card's
                });
            }
        }) { IsBackground = true, Name = "Mazesta GPU scene" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
    private TestRunResult Run(TestExecutionRequest request, TestOptions options, GraphicsDevice device, DateTimeOffset started, CancellationToken ct)
    {
        var custom = SceneModel.LoadCustom(out string? modelProblem);
        string res = options.Get(ResolutionOption); bool full = res == "fullscreen", rayTraced = options.Get(RayTracingOption) == "on";
        var (w, h) = full ? (0, 0) : ParseSize(res);
        uint load = (uint)Math.Clamp(int.TryParse(options.Get(LoadOption), out int l) ? l : 3, 1, 4);
        string mode = rayTraced ? "Direct3D 12 + ray tracing" : "Direct3D 12", level = LoadNames[load - 1];
        using var session = new D3D12Session(device);
        using var window = new TestWindow($"Mazesta — {mode} — Persian garden", w, h, full);
        if (full) (w, h) = (window.Width, window.Height);
        using var renderer = new SceneView(session, window, w, h, rayTraced, load, custom, modelProblem);
        string work = renderer.Work;
        var model = renderer.Garden;
        string card = $"{session.AdapterName} · {device.DedicatedMemorySize / (1024.0 * 1024 * 1024):F1} GB";
        var gpu = GpuNode(request.Engine, session.AdapterName);

        long frames = 0, checks = 0, errors = 0; ulong? reference = null; string firstError = ""; bool closed = false; double minFps = double.MaxValue;
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        var lastCheck = Stopwatch.StartNew(); var updateClock = Stopwatch.StartNew(); long updateFrames = 0;
        ShowReadout(0);
        try
        {
            while (total.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) { closed = true; break; }
                if (reference is null || lastCheck.Elapsed.TotalSeconds >= CheckSeconds)
                {
                    ulong sum = renderer.CheckFrame(CheckTime); checks++; lastCheck.Restart();
                    if (reference is null) reference = sum;
                    else if (sum != reference) { errors++; if (firstError.Length == 0) firstError = $"check frame {checks} differs from the first one (same scene, same moment)"; }
                }
                renderer.Present((float)total.Elapsed.TotalSeconds);
                frames++; updateFrames++;
                if (updateClock.Elapsed.TotalSeconds >= 0.5)
                {
                    double fps = updateFrames / updateClock.Elapsed.TotalSeconds;
                    if (total.Elapsed.TotalSeconds > 2) minFps = Math.Min(minFps, fps);   // the first moments include start-up, not the card's pace
                    window.Title = $"Mazesta — {mode} — {fps:F0} FPS — {(int)(duration - total.Elapsed).TotalSeconds} s";
                    ShowReadout(fps);
                    updateFrames = 0; updateClock.Restart();
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe()); }
        if (closed) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, SensorEvidence.Join("the test window was closed before the time was up", Describe()));
        // One last check, so the end of the run is verified too.
        if (reference is { } r && renderer.CheckFrame(CheckTime) != r) { errors++; if (firstError.Length == 0) firstError = "the final check frame differs from the first one"; }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe());

        // The readout shows only what was measured: a sensor this card does not report (or has not reported in the last seconds) is left out.
        void ShowReadout(double fps)
        {
            double average = frames / Math.Max(0.001, total.Elapsed.TotalSeconds);
            var now = request.Clock.UtcNow;
            var sensors = new[]
            {
                Reading(SensorRole.GpuCoreTemp, "TEMP", "°C"), Reading(SensorRole.GpuHotSpotTemp, "HOT SPOT", "°C"), Reading(SensorRole.GpuCoreClock, "CLOCK", "MHz"),
                Reading(SensorRole.GpuLoad3D, "LOAD", "%"), Reading(SensorRole.GpuPower, "POWER", "W"), Reading(SensorRole.GpuFanPercent, "FAN", "%")
            }.OfType<SceneOverlay.Tile>().ToList();
            bool running = frames > 0;
            renderer.Overlay.Update(new(
                $"{mode} · {level}", running ? fps : null, running ? average : null, minFps < double.MaxValue ? minFps : null, card, sensors,
                $"{w} × {h}" + (window.Width != w || window.Height != h ? $" → {window.Width} × {window.Height}" : ""), work,
                $"{(int)total.Elapsed.TotalSeconds} / {request.DurationSeconds} s · check frames {checks} · errors {errors} · Esc stops", errors > 0));
            SceneOverlay.Tile? Reading(SensorRole role, string label, string unit) =>
                Latest(request.Engine, gpu, role, now) is { } v ? new(label, v.ToString("F0", CultureInfo.InvariantCulture), unit) : null;
        }

        string Describe()
        {
            var finished = request.Clock.UtcNow;
            return SensorEvidence.Join($"{mode} Persian garden drawn at {w}x{h} (window {window.Width}x{window.Height}) on {session.AdapterName}; {model.Triangles:N0} triangles in {model.Instances.Length:N0} objects, centre model '{model.ModelName}'",
                $"load level {load}: {work}",
                $"frames={frames}", $"{frames / Math.Max(0.001, total.Elapsed.TotalSeconds):F1} FPS average", minFps < double.MaxValue ? $"{minFps:F1} FPS lowest half-second" : null, $"check frames={checks}",
                firstError.Length > 0 ? firstError : null, model.ModelProblem,
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuLoad3D, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("measured GPU load", "%"),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuPower, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU power", " W", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreTemp, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU temperature", "°C", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU hot spot", "°C", includeMax: true));
        }
    }

    private static readonly string[] LoadNames = ["light", "medium", "heavy", "extreme"];

    /// <summary>The monitored GPU that is the card under test: matched by name, or the only GPU there is. With several cards and no name
    /// match there is no answer - another card's temperature must never be shown as this one's.</summary>
    internal static HardwareNode? GpuNode(PollingEngine? engine, string adapter)
    {
        var gpus = engine?.Hardware.Where(n => n.Kind == HardwareKind.Gpu).ToList();
        if (gpus is null || gpus.Count == 0) return null;
        return gpus.FirstOrDefault(n => n.Name.Contains(adapter, StringComparison.OrdinalIgnoreCase) || adapter.Contains(n.Name, StringComparison.OrdinalIgnoreCase))
            ?? (gpus.Count == 1 ? gpus[0] : null);
    }

    /// <summary>The node's newest reading of <paramref name="role"/> from the last few seconds, or null: a stale value is not a live reading.</summary>
    internal static double? Latest(PollingEngine? engine, HardwareNode? node, SensorRole role, DateTimeOffset now)
    {
        if (engine is null || node is null) return null;
        int recent = engine.History.SecondsSinceEpoch(now) - 5;
        foreach (var s in node.Sensors.Where(s => s.Role == role))
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = raw.Values.Length - 1; i >= 0 && raw.Seconds[i] >= recent; i--) if (!float.IsNaN(raw.Values[i])) return raw.Values[i];
        }
        return null;
    }

    internal static (int, int) ParseSize(string s) { var p = s.Split('x'); return p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) ? (w, h) : (1280, 720); }
}
