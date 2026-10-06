using System.IO; using System.Threading; using Mazesta.Core.Hardware; using Mazesta.Core.Tray; using Mazesta.Desktop.Services; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private volatile bool _quiet;

    /// <summary>
    /// While a benchmark (or a queue of them) runs, the app does as little else as it can, so what is measured is the machine and not the app:
    /// the page gets no live sensor data (it shows the run's progress only), the monitor reads only the CPU, GPU and memory it needs for the
    /// run's clocks and temperatures plus whatever the overlay shows, the tray puts its checks off, and the update check waits. The overlay
    /// stays as it is. Everything comes back when the run ends.
    /// </summary>
    private void RegisterQuiet()
    {
        var runner = _sp.GetRequiredService<BenchmarkRunner>(); var engine = _sp.GetRequiredService<PollingEngine>(); var overlay = _sp.GetRequiredService<OverlayService>();
        EventWaitHandle? busy = null;
        try { busy = new EventWaitHandle(false, EventResetMode.ManualReset, OverlaySignals.BenchmarkBusy); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException) { _log.LogWarning(e, "The benchmark signal for the tray is not available"); }
        AppUpdater.Get(_paths, _log).Quiet = () => _quiet;

        HardwareKind[] needed = [HardwareKind.Cpu, HardwareKind.Gpu, HardwareKind.Memory];
        void Apply(bool on)
        {
            _quiet = on;
            if (on)
            {
                var keep = overlay.ShownKinds().Concat(needed).ToHashSet();
                engine.Skip(Enum.GetValues<HardwareKind>().Where(k => !keep.Contains(k)).ToHashSet());
                busy?.Set();
            }
            else { engine.Skip(null); busy?.Reset(); }
            _log.LogInformation("Benchmark quiet mode {State}; not polled: {Kinds}", on ? "on" : "off", engine.Skipped is { } s ? string.Join(", ", s) : "-");
            Push("quiet", on);
        }
        void OnBusy(bool on) => _window.Dispatcher.BeginInvoke(() => Apply(on));
        runner.BusyChanged += OnBusy;
        // The overlay shown or hidden during a run changes what the monitor must keep reading.
        void OnOverlay(bool _) { if (_quiet) Apply(true); }
        overlay.VisibilityChanged += OnOverlay;
        _cleanup.Add(() =>
        {
            runner.BusyChanged -= OnBusy; overlay.VisibilityChanged -= OnOverlay;
            if (_quiet) { engine.Skip(null); _quiet = false; }
            busy?.Reset(); busy?.Dispose();
        });
        if (runner.IsBusy) Apply(true);
        Method("app.quiet", _ => _quiet);
    }
}
