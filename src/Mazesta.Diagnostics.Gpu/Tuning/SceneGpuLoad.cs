using System.Diagnostics; using ComputeSharp; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene; using Mazesta.Diagnostics.Tuning;
namespace Mazesta.Diagnostics.Gpu.Tuning;

/// <summary>
/// The tuner's third load: the benchmark's own Persian garden (Direct3D 12, 1080p, the heavy quality) drawn from one fixed camera in one fixed light (the water, the fountain, the wind and the wings
/// move on the clock), frame after frame with no v-sync. A game's load, not a stress test's: the card runs into its boost the way it does in use, where the
/// hash chain of <see cref="ComputeGpuLoad"/> holds it at the power limit well under its boost clock. The score is the frame rate, and with a still
/// camera the view is the same every frame and the motion repeats over seconds, so two profiles are comparable by it (and by the temperature and power read beside it). A frame drawn
/// before and after the run at one moment must be the same picture, or the card computed wrongly. With <c>rayTraced</c> the frame is the DXR 1.1 one (shadows and reflections by rays).
/// With <c>bursts</c> it is <see cref="GpuLoadKind.Transient"/>: the scene in bursts from idle, every CPU core loaded during each burst, and the check frame drawn again
/// after every long rest - the first frames after the jump are where an undervolt or an overclock fails.
/// </summary>
internal static class SceneGpuLoad
{
    private const int Width = 1920, Height = 1080; private const float CheckTime = 1.234f;

    public static LoadRunResult Run(GraphicsDevice device, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct, bool bursts = false)
    {
        if (rayTraced && !GpuFeatures.SupportsInlineRayTracing(device)) return new(0, 0, false, $"{device.Name} reports ray-tracing tier {GpuFeatures.RayTracingTier(device)}; DXR 1.1 (inline ray tracing) is required.");
        LoadRunResult? result = null; Exception? fault = null;
        // The window, its messages and the drawing share one thread, as in the tests.
        var thread = new Thread(() => { try { result = RunOnThread(device, duration, settle, rayTraced, ct, bursts); } catch (Exception e) { fault = e; } }, GpuDevices.RenderStack) { IsBackground = true, Name = "Mazesta tuning scene" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (fault is OperationCanceledException) throw fault;
        return fault is not null ? new(0, 0, true, $"{fault.GetType().Name}: {fault.Message}") : result!;
    }

    private static LoadRunResult RunOnThread(GraphicsDevice device, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct, bool bursts)
    {
        GardenCamera.Pinned = 0;   // the camera and the light stay put; the water, the fountain, the wind and the wings move with the clock
        try { return bursts ? Bursts(device, duration, rayTraced, ct) : Draw(device, duration, settle, rayTraced, ct); } finally { GardenCamera.Pinned = null; }
    }

    /// <summary>Bursts of 30-400 ms, one in four 1-4 s long, after rests of 20-600 ms, one in five 3-8 s long (long enough for the card to drop to its idle clock and
    /// voltage). The score is frames per second of burst; an error is a check frame, drawn after each long rest, that is not the picture drawn before the run.</summary>
    private static LoadRunResult Bursts(GraphicsDevice device, TimeSpan duration, bool rayTraced, CancellationToken ct)
    {
        using var session = new D3D12Session(device, 2);
        using var window = new TestWindow("Mazesta — tuning load", Width, Height, false);
        using var view = new SceneView(session, window, Width, Height, rayTraced, 3, null, null);
        using var cpu = new CpuBursts();
        view.Overlay.Visible = false;
        var before = view.CheckFrame(CheckTime);
        var rng = new Random(0x7A5E); var clock = Stopwatch.StartNew(); long frames = 0, errors = 0; double burstSeconds = 0;
        void Rest(double ms) { var until = clock.Elapsed.TotalMilliseconds + ms; while (clock.Elapsed.TotalMilliseconds < until) { ct.ThrowIfCancellationRequested(); if (!window.Pump()) throw new OperationCanceledException("The tuning window was closed."); Thread.Sleep(5); } }
        while (clock.Elapsed < duration)
        {
            bool longRest = rng.Next(5) == 0;
            Rest(longRest ? rng.Next(3000, 8001) : rng.Next(20, 601));
            if (longRest) { if (!SceneView.Same(view.CheckFrame(CheckTime), before)) errors++; }   // the first frame after the jump, checked
            double burstMs = rng.Next(4) == 0 ? rng.Next(1000, 4001) : rng.Next(30, 401); var start = clock.Elapsed;
            cpu.On();
            try
            {
                while ((clock.Elapsed - start).TotalMilliseconds < burstMs && clock.Elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!window.Pump()) throw new OperationCanceledException("The tuning window was closed.");
                    view.Present((float)clock.Elapsed.TotalSeconds); frames++;
                }
                view.Drain();   // the card really goes idle in the rest, not still working through queued frames
            }
            finally { cpu.Off(); }
            burstSeconds += (clock.Elapsed - start).TotalSeconds;
        }
        if (!SceneView.Same(view.CheckFrame(CheckTime), before)) errors++;
        return new(frames / Math.Max(0.001, burstSeconds), errors, false, null);
    }

    /// <summary>Every core but one (the drawing thread's) spinning while a burst is on and asleep between: the CPU's surge lands on the power supply with the card's.</summary>
    private sealed class CpuBursts : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false); private volatile bool _stop; private readonly Thread[] _threads;
        public CpuBursts()
        {
            _threads = [.. Enumerable.Range(0, Math.Max(1, Environment.ProcessorCount - 1)).Select(i => new Thread(Spin) { IsBackground = true, Name = $"Mazesta burst {i}" })];
            foreach (var t in _threads) t.Start();
        }
        public void On() => _gate.Set();
        public void Off() => _gate.Reset();
        private void Spin()
        {
            ulong x = (ulong)Environment.CurrentManagedThreadId * 0x9E3779B97F4A7C15UL;
            while (true)
            {
                _gate.Wait(); if (_stop) return;
                for (int i = 0; i < 1 << 16; i++) x = x * 6364136223846793005UL + 1442695040888963407UL ^ (x >> 29);
                if (x == 42) Thread.Yield();   // keeps the loop from being optimised away
            }
        }
        public void Dispose() { _stop = true; _gate.Set(); foreach (var t in _threads) t.Join(1000); _gate.Dispose(); }
    }

    private static LoadRunResult Draw(GraphicsDevice device, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct)
    {
        using var session = new D3D12Session(device, 2);
        using var window = new TestWindow("Mazesta — tuning load", Width, Height, false);
        using var view = new SceneView(session, window, Width, Height, rayTraced, 3, null, null);
        view.Overlay.Visible = false;
        var before = view.CheckFrame(CheckTime);
        for (int i = 0; i < 3; i++) view.Present(0);
        view.Drain();
        long frames = 0, counted = 0; var clock = Stopwatch.StartNew(); TimeSpan countedFrom = default;
        while (clock.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            if (!window.Pump()) throw new OperationCanceledException("The tuning window was closed.");
            view.Present((float)clock.Elapsed.TotalSeconds); frames++;
            if (clock.Elapsed >= settle) { if (countedFrom == TimeSpan.Zero) countedFrom = clock.Elapsed; else counted++; }
        }
        view.Drain();
        long errors = SceneView.Same(view.CheckFrame(CheckTime), before) ? 0 : 1;
        return new(counted / Math.Max(0.001, (clock.Elapsed - countedFrom).TotalSeconds), errors, false, null);
    }
}
