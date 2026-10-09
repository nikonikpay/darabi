using System.Diagnostics; using System.Globalization; using System.Numerics; using System.Runtime.InteropServices; using ComputeSharp;
using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Evidence; using Mazesta.Monitoring; using Mazesta.Diagnostics.Gpu.Benchmarks;
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
public sealed class GpuSceneExecutor(MemoryFactsSource? facts = null) : ITestExecutor, ITestAvailability
{
    public const string ResolutionOption = "resolution", LoadOption = "load", RayTracingOption = "raytracing", OverlayOption = "overlay", FullScreenOption = "fullscreen", WeatherOption = "weather", FramesOption = "frames";
    /// <summary>The readout over the scene (frame rate, the card, its memory, the processor, the RAM): on unless switched off here; the O key in the
    /// test's window shows and hides it while it runs. It is laid over the finished frame and is outside what a benchmark times.</summary>
    internal static readonly TestOption Overlay = new(OverlayOption, "Test_Option_SceneOverlay", TestOptionKind.Choice, "on", () => [new("off", "Test_RayTracing_Off", true), new("on", "Test_Switch_On", true)]);
    /// <summary>Ray tracing on or off: the same garden, its shadows and mirror images found with rays (DXR 1.1) instead of maps.</summary>
    internal static readonly TestOption RayTracing = new(RayTracingOption, "Test_Option_RayTracing", TestOptionKind.Choice, "off",
        () => [new("off", "Test_RayTracing_Off", true, "shadow maps, pictures of the surroundings"), new("on", "Test_RayTracing_On", true, "ray-traced shadows and reflections (DXR 1.1)")]);
    /// <summary>The window covers the screen with no border (the frame is still drawn at the resolution chosen and shown scaled to the screen): on unless switched off.</summary>
    internal static readonly TestOption FullScreen = new(FullScreenOption, "Test_Option_FullScreen", TestOptionKind.Choice, "on", () => [new("off", "Test_RayTracing_Off", true), new("on", "Test_Switch_On", true)]);
    /// <summary>The garden's weather (<see cref="GardenWeather"/>): rain, gusts that lift leaves and twigs out of the crowns and carry them, simulated on every core of the processor each frame, in air that is a fluid going round the hall; the plants sway in it.
    /// Off unless switched on (the card is then the only thing the test loads); it loads the processor and the memory (the grids of solids and of air), not the card.</summary>
    internal static readonly TestOption Weather = new(WeatherOption, "Test_Option_Weather", TestOptionKind.Choice, "off",
        () => [new("off", "Test_RayTracing_Off", true, "no rain, no wind-blown leaves"), new("on", "Test_Switch_On", true, "rain, gusts, leaves and twigs, the air a fluid on every core, plants swaying (20,000 bodies, 29 MB of solids, 29 MB of air)"),
               new("high", "Test_Weather_High", true, "three times the bodies, a finer air (98 MB) and 233 MB of solids: the memory and every core set the speed")]);
    /// <summary>How many frames are in flight at once: one is drawn and waited for before the next is prepared (the processor's share of a frame, a millisecond or two, is time the card is idle); two lets the processor prepare
    /// the next frame while the card draws this one, so the card is never idle - what a game does. The scene is the same; the frame rate, and how busy the card is kept, are what differ.</summary>
    internal static readonly TestOption Frames = new(FramesOption, "Test_Option_Frames", TestOptionKind.Choice, "1",
        () => [new("1", "Test_Frames_One", true, "each frame is finished before the next is prepared"), new("2", "Test_Frames_Two", true, "the next frame is prepared while the card draws this one (what a game does)")]);
    /// <summary>The frames in flight a choice means.</summary>
    internal static int FramesOf(string choice) => choice == "2" ? 2 : 1;
    /// <summary>A size as the page shows it: width first. Isolated left to right, so a right-to-left page does not turn "2560 × 1440" into "1440 × 2560".</summary>
    internal static string SizeLabel(string text) => "⁦" + text + "⁩";
    internal static IReadOnlyList<OptionChoice> Sizes() => [new("1280x720", SizeLabel("1280 × 720 (HD)")), new("1920x1080", SizeLabel("1920 × 1080 (Full HD)")), new("2560x1440", SizeLabel("2560 × 1440 (2K)")), new("3840x2160", SizeLabel("3840 × 2160 (4K)"))];
    /// <summary>Ray tracing starts on where the card has DXR 1.1, off otherwise.</summary>
    private static readonly TestOption RayTracingPreferred = RayTracing with { Preferred = () => GpuFeatures.RayTracingAvailability(TestOptions.None(Scene!)) is null ? "on" : "off" };
    // The frame is drawn at the chosen size even where the screen is smaller (the window then shows it scaled down), so 4K load can be
    // tested on any monitor; full screen draws at the screen's own size.
    private static readonly TestOption Resolution = new(ResolutionOption, "Test_Option_Resolution", TestOptionKind.Choice, "1920x1080",
        Sizes);
    public static readonly TestDefinition Scene = new(new TestId("gpu.scene.d3d"), "Test_Gpu_Scene3D", 300,
        [GpuDevices.Option, Resolution, new TestOption(LoadOption, "Test_Option_GpuLoad", TestOptionKind.Choice, "3",
            () => [new("1", "Test_GpuLoad_Light", true), new("2", "Test_GpuLoad_Medium", true), new("3", "Test_GpuLoad_Heavy", true), new("4", "Test_GpuLoad_Extreme", true)]), RayTracingPreferred, Weather, Frames, FullScreen, Overlay]);
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
        string res = options.Get(ResolutionOption); bool screenSize = res == "fullscreen" /* an older saved choice: the screen's own size */, full = screenSize || options.Get(FullScreenOption) == "on", rayTraced = options.Get(RayTracingOption) == "on";
        var (w, h) = screenSize ? (0, 0) : ParseSize(res);
        uint load = (uint)Math.Clamp(int.TryParse(options.Get(LoadOption), out int l) ? l : 3, 1, 4);
        string mode = rayTraced ? "Direct3D 12 + ray tracing" : "Direct3D 12", level = LoadNames[load - 1];
        using var session = new D3D12Session(device, FramesOf(options.Get(FramesOption)));
        using var window = new TestWindow($"Mazesta — {mode} — Persian garden", w, h, full);
        if (screenSize) (w, h) = (window.Width, window.Height);
        using var renderer = new SceneView(session, window, w, h, rayTraced, load, custom, modelProblem, WeatherLevel.Parse(options.Get(WeatherOption)));
        string work = renderer.Work;
        var model = renderer.Garden;
        string card = $"{session.AdapterName} · {device.DedicatedMemorySize / (1024.0 * 1024 * 1024):F1} GB";
        var gpu = GpuNode(request.Engine, session.AdapterName); var ram = facts?.Invoke();
        renderer.Overlay.Visible = options.Get(OverlayOption) != "off";
        using var pace0 = new SceneSensorPace(request.Engine, renderer.Overlay.Visible);

        long frames = 0, checks = 0, errors = 0; float[]? reference = null; string firstError = ""; bool closed = false; double minFps = double.MaxValue;
        var total = Stopwatch.StartNew(); var duration = TimeSpan.FromSeconds(request.DurationSeconds);
        var lastCheck = Stopwatch.StartNew(); var updateClock = Stopwatch.StartNew(); long updateFrames = 0; var pace = new FramePace(); long lastShown = Stopwatch.GetTimestamp();
        ShowReadout(0);
        try
        {
            while (total.Elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!window.Pump()) { closed = true; break; }
                if (window.OverlayKey()) { renderer.Overlay.Visible = !renderer.Overlay.Visible; ShowReadout(0); }
                if (reference is null || lastCheck.Elapsed.TotalSeconds >= CheckSeconds)
                {
                    var sum = renderer.CheckFrame(CheckTime); checks++; lastCheck.Restart(); lastShown = Stopwatch.GetTimestamp();   // (a check frame between two is not a stutter)
                    if (reference is null) reference = sum;
                    else if (!SceneView.Same(sum, reference)) { errors++; if (firstError.Length == 0) firstError = $"check frame {checks} differs from the first one (same scene, same moment)"; }
                }
                renderer.Present((float)total.Elapsed.TotalSeconds);
                long now = Stopwatch.GetTimestamp(); pace.Frame(Stopwatch.GetElapsedTime(lastShown, now).TotalSeconds); lastShown = now;   // the gap from one shown frame to the next, as the benchmark counts it
                frames++; updateFrames++;
                if (updateClock.Elapsed.TotalSeconds >= 0.5)
                {
                    double fps = updateFrames / updateClock.Elapsed.TotalSeconds;
                    if (total.Elapsed.TotalSeconds > 2) minFps = Math.Min(minFps, fps);   // the first moments include start-up, not the card's pace
                    window.Title = $"Mazesta — {mode} — {fps:F0} FPS — {(int)(duration - total.Elapsed).TotalSeconds} s";
                    pace.Sample(fps); ShowReadout(fps);
                    updateFrames = 0; updateClock.Restart();
                    request.Progress?.Invoke(new TestProgress(Math.Clamp(total.Elapsed / duration, 0, 1), "Test_Status_Running"));
                }
            }
        }
        catch (OperationCanceledException) { return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, Describe()); }
        if (closed) return new(Definition.Id, TestOutcome.Cancelled, started, request.Clock.UtcNow, errors, SensorEvidence.Join("the test window was closed before the time was up", Describe()));
        // One last check, so the end of the run is verified too.
        if (reference is { } r && !SceneView.Same(renderer.CheckFrame(CheckTime), r)) { errors++; if (firstError.Length == 0) firstError = "the final check frame differs from the first one"; }
        request.Progress?.Invoke(new TestProgress(1, "Test_Status_Running"));
        return new(Definition.Id, errors > 0 ? TestOutcome.Failed : TestOutcome.Passed, started, request.Clock.UtcNow, errors, Describe());

        // The readout shows only what was measured: a sensor this card does not report (or has not reported in the last seconds) is left out.
        void ShowReadout(double fps)
        {
            double average = frames / Math.Max(0.001, total.Elapsed.TotalSeconds);
            var now = request.Clock.UtcNow;
            bool running = frames > 0;
            renderer.Overlay.Update(new(
                $"{(rayTraced ? "D3D12 + RT" : "D3D12")} · {level}", running && fps > 0 ? fps : null, running ? average : null, minFps < double.MaxValue ? minFps : null, pace.Low, pace.Fps, pace.Lows, Rows(request.Engine, gpu, now, ram),
                $"{w} × {h}" + (window.Width != w || window.Height != h ? $" → {window.Width} × {window.Height}" : ""),
                $"{(int)total.Elapsed.TotalSeconds}/{request.DurationSeconds} s · errors {errors}", errors > 0));
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
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuHotSpotTemp, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU hot spot", "°C", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuCoreClock, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU clock", " MHz", includeMax: true),
                SensorEvidence.Read(request.Engine, HardwareKind.Gpu, SensorRole.GpuMemoryClock, started, finished, GpuDevices.SensorNode(request.Engine, session.AdapterName), null)?.Format("GPU memory clock", " MHz", includeMax: true),
                string.Join("; ", HostMetrics.Evidence(request, started, finished, ram)));
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
    internal static double? Latest(PollingEngine? engine, HardwareNode? node, SensorRole role, DateTimeOffset now) => Reading(engine, node, role, now)?.Value;

    /// <summary><see cref="Latest"/> with the unit the sensor reports in; the node's own sensors first, then those of the nodes under it.</summary>
    private static (double Value, Unit Unit)? Reading(PollingEngine? engine, HardwareNode? node, SensorRole role, DateTimeOffset now)
    {
        if (engine is null || node is null) return null;
        int recent = engine.History.SecondsSinceEpoch(now) - 5;
        foreach (var s in node.Sensors.Concat(engine.Hardware.Where(n => n.ParentId == node.Id).SelectMany(n => n.Sensors)).Where(s => s.Role == role))
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = raw.Values.Length - 1; i >= 0 && raw.Seconds[i] >= recent; i--) if (!float.IsNaN(raw.Values[i])) return (raw.Values[i], s.Unit);
        }
        return null;
    }

    /// <summary>The readout's lines: the card under test, its memory, the processor and the RAM, each with what the monitor read of it in the last
    /// seconds. A figure that was not read is left out, a part with none has no line: nothing is shown that was not measured.</summary>
    /// <summary>The fastest core's clock over the last few seconds, as Task Manager's speed shows it: an average over every thread (most of them idle) says nothing about what the cores that work run at.</summary>
    private static SceneOverlay.Figure? HighestCoreClock(PollingEngine? engine, HardwareNode? cpu, DateTimeOffset now)
    {
        if (engine is null || cpu is null) return null;
        int recent = engine.History.SecondsSinceEpoch(now) - 5; double best = 0;
        foreach (var s in cpu.Sensors.Where(s => s.Role == SensorRole.CpuCoreClock))
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = raw.Values.Length - 1; i >= 0 && raw.Seconds[i] >= recent; i--) if (!float.IsNaN(raw.Values[i])) { best = Math.Max(best, raw.Values[i]); break; }
        }
        return best > 0 ? new(best.ToString("F0", CultureInfo.InvariantCulture), "MHz") : null;
    }

    internal static IReadOnlyList<SceneOverlay.Row> Rows(PollingEngine? engine, HardwareNode? gpu, DateTimeOffset now, MemoryFacts? ram = null)
    {
        var cpu = engine?.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Cpu && n.ParentId is null); var ramNode = engine?.Hardware.FirstOrDefault(n => n.Kind == HardwareKind.Memory && n.ParentId is null);
        SceneOverlay.Figure? One(HardwareNode? node, string unit, params SensorRole[] roles)
        {
            foreach (var role in roles) if (Reading(engine, node, role, now) is { } r) return new(r.Value.ToString("F0", CultureInfo.InvariantCulture), unit);
            return null;
        }
        static double? Gb((double Value, Unit Unit)? r) => r is not { } x ? null : x.Unit == Unit.Megabyte ? x.Value / 1024 : x.Unit == Unit.Gigabyte ? x.Value : null;
        SceneOverlay.Figure? Used(HardwareNode? node, SensorRole used, SensorRole total, SensorRole free)
        {
            double? u = Gb(Reading(engine, node, used, now)), t = Gb(Reading(engine, node, total, now)) ?? (u is { } a && Gb(Reading(engine, node, free, now)) is { } f ? a + f : null);
            return u is null ? null : new(u.Value.ToString("F1", CultureInfo.InvariantCulture) + (t is { } all ? " / " + all.ToString("F0", CultureInfo.InvariantCulture) : ""), "GB");
        }
        var rows = new List<SceneOverlay.Row>();
        void Add(string name, uint hue, params SceneOverlay.Figure?[] figures) { var f = figures.OfType<SceneOverlay.Figure>().ToList(); if (f.Count > 0) rows.Add(new(name, hue, f)); }
        Add("GPU", SceneOverlay.GpuHue, One(gpu, "%", SensorRole.GpuLoad3D), One(gpu, "°C", SensorRole.GpuCoreTemp), One(gpu, "°C HOT", SensorRole.GpuHotSpotTemp), One(gpu, "W", SensorRole.GpuPower), One(gpu, "% FAN", SensorRole.GpuFanPercent));
        Add("CLK", SceneOverlay.GpuHue, One(gpu, "MHz", SensorRole.GpuCoreClock), One(gpu, "MHz MEM", SensorRole.GpuMemoryClock));   // the graphics card's core and memory clocks
        Add("VRAM", SceneOverlay.GpuHue, Used(gpu, SensorRole.GpuVramUsed, SensorRole.GpuVramTotal, SensorRole.GpuVramFree), One(gpu, "°C", SensorRole.GpuVramTemp));
        Add("CPU", SceneOverlay.CpuHue, One(cpu, "%", SensorRole.CpuTotalLoad), One(cpu, "°C", SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie, SensorRole.CpuCcdMaxTemp), One(cpu, "W", SensorRole.CpuPackagePower),
            HighestCoreClock(engine, cpu, now) ?? One(cpu, "MHz", SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage));
        // A Ryzen's CCDs, each with the clock of its cores (the cores that share a level-3 cache), where the processor has more than one.
        if (cpu is not null && engine is not null)
        {
            int recent = engine.History.SecondsSinceEpoch(now) - 5; var byCcd = new SortedDictionary<int, List<double>>();
            foreach (var s in cpu.Sensors.Where(s => s.Role == SensorRole.CpuCoreClock))
                if (HostMetrics.CcdOfCore(s.Name) is { } ccd)
                {
                    var raw = engine.History.GetRaw(s.Id);
                    for (int i = raw.Values.Length - 1; i >= 0 && raw.Seconds[i] >= recent; i--) if (!float.IsNaN(raw.Values[i])) { (byCcd.TryGetValue(ccd, out var l) ? l : byCcd[ccd] = []).Add(raw.Values[i]); break; }
                }
            Add("CCD", SceneOverlay.CpuHue, [.. byCcd.Select(c => (SceneOverlay.Figure?)new SceneOverlay.Figure(c.Value.Average().ToString("F0", CultureInfo.InvariantCulture), "MHz·" + c.Key.ToString(CultureInfo.InvariantCulture)))]);
        }
        // The RAM: in use, the speed it is set to, the CAS latency of its profile at that speed (Windows does not report the timings in use), and last the load, which is dropped first when the line is full.
        Add("RAM", SceneOverlay.RamHue, new SceneOverlay.Figure(RunFootprint.CurrentRamMb().ToString("F0", CultureInfo.InvariantCulture), "MB APP"), ram?.SpeedMts is { } mts ? new(mts.ToString(CultureInfo.InvariantCulture), "MT/s") : null,
            ram?.CasLatency is { } cl ? new("CL" + cl.ToString(CultureInfo.InvariantCulture), "") : null, Used(ramNode, SensorRole.RamUsed, SensorRole.RamTotal, SensorRole.RamFree), One(ramNode, "%", SensorRole.RamLoad));
        return rows;
    }

    internal static (int, int) ParseSize(string s) { var p = s.Split('x'); return p.Length == 2 && int.TryParse(p[0], out int w) && int.TryParse(p[1], out int h) ? (w, h) : (1280, 720); }
}
