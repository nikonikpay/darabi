using System.Text.Json.Nodes;
using Mazesta.Core.Crashes; using Mazesta.Core.Health.Checkup; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Services; using Mazesta.Diagnostics.Crashes; using Mazesta.Persistence; using Mazesta.Persistence.Updates; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

/// <summary>
/// Sends the anonymous usage statistics (see <see cref="UsageLog"/>) to the shop's site while the app runs, when the setting is on (it is, until the user switches it off):
/// the lines of the log not sent yet, a few at a time, with the installation's random id, the app's version and the names of the parts, then the app's own log and crash records (<see cref="LogShipper"/>, scrubbed). No key, no account, no name of the
/// computer or its user, no file, path or text anyone typed. A line is marked sent only when the site took it; a failure (no internet, no plugin) is simply tried again
/// later, never shown, and never holds up anything. One timer, every ten minutes; nothing at all runs while the setting is off.
/// </summary>
internal sealed class UsageUploader : IDisposable
{
    private const int PerRequest = 150;
    private readonly UsageLog _log; private readonly AppConfig _config; private readonly SiteClient _site; private readonly InventoryCache _inventory; private readonly string _version, _edition; private readonly ILogger _logger;
    private readonly System.Threading.Timer _timer; private int _sending; private readonly LogShipper _logs; private readonly CheckupService _checkup; private readonly HardwareDetailsCache _details; private JsonObject? _health;

    public UsageUploader(UsageLog log, AppConfig config, SiteClient site, InventoryCache inventory, string version, bool staff, ILogger logger, LogShipper logs, CheckupService checkup, HardwareDetailsCache details)
    {
        _logs = logs; _checkup = checkup; _details = details; _log = log; _config = config; _site = site; _inventory = inventory; _version = version; _edition = staff ? "company" : "users"; _logger = logger;
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
        // each drive's own health verdict and wear counter, left out where the drive gives none
        var drives = inv.Storage.Where(d => Name(d.FriendlyName) is not null).Select(d => { var o = new JsonObject { ["n"] = Name(d.FriendlyName) }; if (Name(d.HealthStatus) is { } h) o["health"] = h; if (d.WearPercent is { } w) o["wear"] = w; return (JsonNode)o; }).ToArray();
        if (drives.Length > 0) m["drives"] = new JsonArray(drives);
        if (await HealthAsync().ConfigureAwait(false) is { } health) m["health"] = health.DeepClone();
        return m;
    }

    /// <summary>
    /// Once a run: Windows' blue screens and sudden power losses (codes and times only) and the setup checks of the checkup page (the RAM's profile and
    /// channels, the power plan, the drives' health and links), so the shop sees a system's problems beside its parts. The setup is read only once the
    /// hardware details are in (the SPD chips need the sensor driver): until then nothing is kept, and the next send tries again.
    /// </summary>
    private async Task<JsonObject?> HealthAsync()
    {
        if (_health is not null) return _health;
        if (!_details.GetAsync().IsCompletedSuccessfully) return null;
        var h = new JsonObject();
        try
        {
            var c = await Task.Run(CrashReader.Read).ConfigureAwait(false);
            if (c.LogReadable || c.DumpsReadable)
            {
                h["bsod"] = c.Crashes.Count; h["powerLosses"] = c.PowerLosses.Count;
                h["bsodRecent"] = new JsonArray([.. c.Crashes.Take(5).Select(x => (JsonNode)new JsonObject { ["at"] = x.At.ToString("O"), ["code"] = $"0x{x.Code:X8}", ["name"] = BugCheckCatalog.Find(x.Code)?.Name })]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { _logger.LogDebug("Usage statistics: crash history not read: {Message}", e.Message); }
        var setup = await _checkup.SetupAsync().ConfigureAwait(false);
        h["setup"] = new JsonArray([.. setup.Select(f =>
        {
            var o = new JsonObject { ["c"] = f.Code.ToString(), ["l"] = f.Level.ToString(), ["p"] = f.Part.ToString() };
            if (f.Subject is { Length: > 0 } sub) o["s"] = sub;
            if (f.Measures.Count > 0) { var mm = new JsonObject(); foreach (var x in f.Measures) mm[x.Key] = Math.Round(x.Value, 1); o["m"] = mm; }
            return (JsonNode)o;
        })]);
        return _health = h;
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
                if (lines.Count == 0) return await SendLogsAsync().ConfigureAwait(false);
                var body = new JsonObject { ["install"] = _log.InstallId, ["app"] = _version, ["edition"] = _edition, ["language"] = _config.Language, ["machine"] = await MachineAsync().ConfigureAwait(false),
                    ["events"] = new JsonArray([.. lines.Select(l => JsonNode.Parse(l.Json)!)]) };
                await _site.SendUsageAsync(body.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
                _log.MarkSent(lines[^1].Seq);
                if (lines.Count < PerRequest) return await SendLogsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SiteException or System.Text.Json.JsonException or IOException)
        {
            _logger.LogDebug("Usage statistics not sent now: {Message}", e.Message);
        }
        finally { Interlocked.Exchange(ref _sending, 0); }
        return false;
    }

    /// <summary>The app's log and its crash records not sent yet (see <see cref="Mazesta.Persistence.LogShipper"/>), a piece a call while there are more.</summary>
    private async Task<bool> SendLogsAsync()
    {
        for (int i = 0; i < 20 && _logs.Next() is { } p; i++)
        {
            var body = new JsonObject { ["install"] = _log.InstallId, ["app"] = _version, ["file"] = p.File, ["text"] = p.Text, ["crashes"] = p.Crashes };
            await _site.SendLogsAsync(body.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
            p.Commit();
        }
        return true;
    }

    public void Dispose() => _timer.Dispose();
}
