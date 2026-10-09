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
        var gpus = inv.Gpus.Select(g => Name(g.Name)).OfType<string>().ToArray();
        if (gpus.Length > 0) m["gpus"] = new JsonArray([.. gpus.Select(g => (JsonNode)g)]);
        if (inv.TotalPhysicalMemoryBytes is { } b) m["ramGb"] = Math.Round(b / 1073741824.0);
        if (inv.Os is { } os) { m["os"] = $"{os.Caption} {os.Version}".Trim(); }
        return m;
    }

    public async Task SendAsync()
    {
        if (!_config.UsageReport || Interlocked.Exchange(ref _sending, 1) == 1) return;
        try
        {
            while (_config.UsageReport)
            {
                var lines = _log.Unsent(PerRequest);
                if (lines.Count == 0) return;
                var body = new JsonObject { ["install"] = _log.InstallId, ["app"] = _version, ["edition"] = _edition, ["language"] = _config.Language, ["machine"] = await MachineAsync().ConfigureAwait(false),
                    ["events"] = new JsonArray([.. lines.Select(l => JsonNode.Parse(l.Json)!)]) };
                await _site.SendUsageAsync(body.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
                _log.MarkSent(lines[^1].Seq);
                if (lines.Count < PerRequest) return;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException or System.Text.Json.JsonException or IOException)
        {
            _logger.LogDebug("Usage statistics not sent now: {Message}", e.Message);
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
    }

    public void Dispose() => _timer.Dispose();
}
