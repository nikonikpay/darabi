using System.Diagnostics; using ComputeSharp; using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene; using Mazesta.Diagnostics.Tuning;
namespace Mazesta.Diagnostics.Gpu.Tuning;

/// <summary>
/// The tuner's third load: the benchmark's own Persian garden (Direct3D 12, 1080p, the heavy quality) drawn from one fixed camera and one fixed
/// moment, frame after frame with no v-sync. A game's load, not a stress test's: the card runs into its boost the way it does in use, where the
/// hash chain of <see cref="ComputeGpuLoad"/> holds it at the power limit well under its boost clock. The score is the frame rate, and with a still
/// camera it is the same picture every frame, so two profiles are comparable by it (and by the temperature and power read beside it). A frame drawn
/// before and after the run at one moment must be the same picture, or the card computed wrongly. With <c>rayTraced</c> the frame is the DXR 1.1 one (shadows and reflections by rays).
/// </summary>
internal static class SceneGpuLoad
{
    private const int Width = 1920, Height = 1080; private const float CheckTime = 1.234f;

    public static LoadRunResult Run(GraphicsDevice device, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct)
    {
        if (rayTraced && !GpuFeatures.SupportsInlineRayTracing(device)) return new(0, 0, false, $"{device.Name} reports ray-tracing tier {GpuFeatures.RayTracingTier(device)}; DXR 1.1 (inline ray tracing) is required.");
        LoadRunResult? result = null; Exception? fault = null;
        // The window, its messages and the drawing share one thread, as in the tests.
        var thread = new Thread(() => { try { result = RunOnThread(device, duration, settle, rayTraced, ct); } catch (Exception e) { fault = e; } }) { IsBackground = true, Name = "Mazesta tuning scene" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (fault is OperationCanceledException) throw fault;
        return fault is not null ? new(0, 0, true, $"{fault.GetType().Name}: {fault.Message}") : result!;
    }

    private static LoadRunResult RunOnThread(GraphicsDevice device, TimeSpan duration, TimeSpan settle, bool rayTraced, CancellationToken ct)
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
            view.Present(0); frames++;
            if (clock.Elapsed >= settle) { if (countedFrom == TimeSpan.Zero) countedFrom = clock.Elapsed; else counted++; }
        }
        view.Drain();
        long errors = SceneView.Same(view.CheckFrame(CheckTime), before) ? 0 : 1;
        return new(counted / Math.Max(0.001, (clock.Elapsed - countedFrom).TotalSeconds), errors, false, null);
    }
}
