using System.Text.Json.Nodes;
using Mazesta.Desktop.Composition; using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// Sends the anonymous usage statistics (see <see cref="UsageLog"/>) to the shop's site while the app runs, when the setting is on (it is, until the user switches it off):
/// the lines of the log not sent yet, a few at a time, with the installation's random id, the app's version and the names of the parts. No key, no account, no name of the
/// computer or its user, no file, path or text anyone typed. A line is marked sent only when the site took it; a failure (no internet, no plugin) is simply tried again
/// later, never shown, and never holds up anything. One timer, every ten minutes; nothing at all runs while the setting is off.
/// </summary>
internal sealed class UsageUploader : IDisposable
{
    private const int PerRequest = 150;
    private readonly UsageLog _log; private readonly AppConfig _config; private readonly SiteClient _site; private readonly InventoryCache _inventory; private readonly string _version, _edition; private readonly ILogger _logger;
    private readonly System.Threading.Timer _timer; private int _sending;

    public UsageUploader(UsageLog log, AppConfig config, SiteClient site, InventoryCache inventory, string version, bool staff, ILogger logger)
    {
        _log = log; _config = config; _site = site; _inventory = inventory; _version = version; _edition = staff ? "company" : "users"; _logger = logger;
        _timer = new(_ => _ = SendAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));
    }

    /// <summary>The part names - never the computer's own name - the way the shared benchmark results give them.</summary>
    private async Task<JsonObject> MachineAsync()
    {
        var inv = await _inventory.GetAsync().ConfigureAwait(false);
        var m = new JsonObject();
        static string? Name(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        if (Name(inv.Cpu?.Name) is { } cpu) m["cpu"] = cpu;
        if (inv.Cpu is { } c) { if (c.PhysicalCores is { } pc) m["cores"] = pc; if (c.LogicalProcessors is { } lp) m["threads"] = lp; }
        var gpus = inv.Gpus.Select(g => Name(g.Name)).OfType<string>().ToArray();
        if (gpus.Length > 0) m["gpus"] = new JsonArray([.. gpus.Select(g => (JsonNode)g)]);
        if (inv.Gpus.FirstOrDefault(g => g.DriverVersion is not null)?.DriverVersion is { } drv) m["gpuDriver"] = drv;
        if (Name(inv.Motherboard?.Product) is { } board) m["board"] = $"{Name(inv.Motherboard?.Manufacturer)} {board}".Trim();
        var mods = inv.MemoryModules.Where(x => x.CapacityBytes > 0).Select(x => $"{Math.Round(x.CapacityBytes!.Value / 1073741824.0)} GB {x.TypeName} {x.ConfiguredSpeedMts ?? x.SpeedMts} MT/s".Replace("  ", " ").Trim()).ToArray();
        if (mods.Length > 0) m["ramModules"] = new JsonArray([.. mods.Select(x => (JsonNode)x)]);   // (no part numbers or serials)
        var disks = inv.Storage.Where(d => Name(d.FriendlyName) is not null).Select(d => $"{Name(d.FriendlyName)} {(d.SizeBytes is { } z ? Math.Round(z / 1e9) + " GB" : "")} {d.MediaType}".Trim()).ToArray();
        if (disks.Length > 0) m["storage"] = new JsonArray([.. disks.Select(x => (JsonNode)x)]);
        if (inv.TotalPhysicalMemoryBytes is { } b) m["ramGb"] = Math.Round(b / 1073741824.0);
        if (inv.Os is { } os) { m["os"] = $"{os.Caption} {os.Version}".Trim(); }
        return m;
    }

    /// <summary>Sends what is not sent yet. <paramref name="force"/> is the user's own request (the report button): it sends even with the automatic switch off.
    /// Returns whether nothing is left unsent (false: the site did not take it, or another send was running).</summary>
    public async Task<bool> SendAsync(bool force = false)
    {
        if (!(force || _config.UsageReport) || Interlocked.Exchange(ref _sending, 1) == 1) return false;
        try
        {
            while (force || _config.UsageReport)
            {
                var lines = _log.Unsent(PerRequest);
                if (lines.Count == 0) return true;
                var body = new JsonObject { ["install"] = _log.InstallId, ["app"] = _version, ["edition"] = _edition, ["language"] = _config.Language, ["machine"] = await MachineAsync().ConfigureAwait(false),
                    ["events"] = new JsonArray([.. lines.Select(l => JsonNode.Parse(l.Json)!)]) };
                await _site.SendUsageAsync(body.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
                _log.MarkSent(lines[^1].Seq);
                if (lines.Count < PerRequest) return true;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException or System.Text.Json.JsonException or IOException)
        {
            _logger.LogDebug("Usage statistics not sent now: {Message}", e.Message);
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
        return false;
    }

    public void Dispose() => _timer.Dispose();
}
