using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Composition; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private void RegisterMonitoring()
    {
        var engine = _sp.GetRequiredService<PollingEngine>();

        // One compact array per poll: [id, value or null, quality] and, for sensors with readings, [id, min, avg, max] since the monitor started.
        // A reading that is not good is sent as null with its quality, so the page can show why, and never as 0.
        void OnSnapshot(SensorSnapshot s)
        {
            if (!_visible || _quiet) return;
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

        // A part's full specification (kind: Cpu, Gpu, Memory, Storage, Network, Motherboard) or, with no kind, every part: read once, after the
        // sensor driver is up, since the memory modules' SPD is read over its SMBus. Until that read is done, the last start's read of the same parts
        // is answered with cached = true, and "hardwareFresh" tells the page to ask again.
        var snapshot = _sp.GetRequiredService<HardwareSnapshot>();
        void OnFresh() => Push("hardwareFresh", null);
        snapshot.Fresh += OnFresh; _cleanup.Add(() => snapshot.Fresh -= OnFresh);
        MethodAsync("specs.get", async p =>
        {
            var (inv, d, cached) = await snapshot.DetailsAsync().ConfigureAwait(true);
            string kind = Str(p, "kind");
            var cards = kind.Length == 0 ? PartSpecs.All(inv, d) : PartSpecs.For(Enum.Parse<HardwareKind>(kind), inv, d);
            return new { cards = cards.Select(Card), errors = d.Errors, cached };
        });

        MethodAsync("inventory.get", async _ =>
        {
            var (inv, cached) = await snapshot.InventoryAsync().ConfigureAwait(true);
            return new
            {
                cached,
                sections = SystemInfoViewModel.Describe(inv).Select(Section),
                components = new[] { HardwareKind.Cpu, HardwareKind.Gpu, HardwareKind.Storage, HardwareKind.Network }
                    .ToDictionary(k => k.ToString(), k => SystemInfoViewModel.Component(inv, k).Select(Section)),
                cpu = inv.Cpu?.Name?.Trim(), gpus = inv.Gpus.Select(g => g.Name?.Trim()), board = inv.Motherboard is { } b ? $"{b.Manufacturer} {b.Product}".Trim() : null,
                bios = inv.Bios?.Version, ramBytes = inv.TotalPhysicalMemoryBytes, os = inv.Os?.Caption, errors = inv.Errors,
                drives = inv.Storage.Select(d => new { name = d.FriendlyName, health = SystemInfoViewModel.DriveHealth(d.HealthStatus, d.WearPercent), status = d.HealthStatus }),
            };
        });
    }

    /// <summary>A specification card for the page: its rows (the folded ones marked), its table with the row in use, and its note.</summary>
    private static object Card(SpecCard c) => new
    {
        title = c.Title, rows = c.Rows.Select(r => new { label = r.Label, value = r.Value, more = r.More }), note = c.Note,
        table = c.Table is { } t ? new { headers = t.Headers, rows = t.Rows, highlight = t.Highlight } : null,
    };

    private static object Section(InfoSection s) => new { title = s.Title, rows = s.Rows.Select(r => new { label = r.Label, value = r.Value }) };
}
