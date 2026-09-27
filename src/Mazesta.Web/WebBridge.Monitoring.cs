using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    private void RegisterMonitoring()
    {
        var engine = _sp.GetRequiredService<PollingEngine>(); var inventory = _sp.GetRequiredService<InventoryCache>();

        // One compact array per poll: [id, value or null, quality] and, for sensors with readings, [id, min, avg, max] since the monitor started.
        // A reading that is not good is sent as null with its quality, so the page can show why, and never as 0.
        void OnSnapshot(SensorSnapshot s)
        {
            if (!_visible) return;
            var readings = s.Readings.Select(r => new object?[] { r.Id.Value, r.Quality == DataQuality.Ok ? r.Value : null, r.Quality.ToString() }).ToList();
            var stats = s.Readings.Select(r => (r.Id, St: engine.Statistics.Get(r.Id))).Where(x => x.St.Count > 0)
                .Select(x => new object?[] { x.Id.Value, x.St.Min, x.St.Average, x.St.Max }).ToList();
            Push("snapshot", new { t = s.Timestamp.ToUnixTimeMilliseconds(), r = readings, s = stats });
        }
        engine.SnapshotPublished += OnSnapshot; _cleanup.Add(() => engine.SnapshotPublished -= OnSnapshot);

        // The recorded history of one sensor: seconds from the history's epoch and values (NaN is a gap), with "now" on the same axis.
        Method("history.get", p =>
        {
            var id = new SensorId(Str(p, "id")); var raw = engine.History.GetRaw(id);
            return new { sec = raw.Seconds, val = raw.Values.Select(v => float.IsNaN(v) ? (float?)null : v), now = engine.History.SecondsSinceEpoch(DateTimeOffset.UtcNow) };
        });

        Method("monitor.popout", p => { if (_window is MainWindow { WebEnvironment: { } env }) ChartWindow.Show(env, engine, Str(p, "id"), _log); return null; });

        MethodAsync("inventory.get", async _ =>
        {
            HardwareInventory inv = await inventory.GetAsync().ConfigureAwait(true);
            return new
            {
                sections = SystemInfoViewModel.Describe(inv).Select(Section),
                components = new[] { HardwareKind.Cpu, HardwareKind.Gpu, HardwareKind.Storage, HardwareKind.Network }
                    .ToDictionary(k => k.ToString(), k => SystemInfoViewModel.Component(inv, k).Select(Section)),
                cpu = inv.Cpu?.Name?.Trim(), gpus = inv.Gpus.Select(g => g.Name?.Trim()), board = inv.Motherboard is { } b ? $"{b.Manufacturer} {b.Product}".Trim() : null,
                bios = inv.Bios?.Version, ramBytes = inv.TotalPhysicalMemoryBytes, os = inv.Os?.Caption, errors = inv.Errors,
            };
        });
    }

    private static object Section(InfoSection s) => new { title = s.Title, rows = s.Rows.Select(r => new { label = r.Label, value = r.Value }) };
}
