using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
namespace Mazesta.Desk;

/// <summary>What the program shows about its copy.</summary>
internal sealed record SyncState(bool Supported, bool Known, bool Ready, bool Syncing, int Received, string LastSync, int AgeMinutes, long Seq, string Error);

/// <summary>Keeping the PC's copy current. Every minute the site's small static file is read (WordPress is not started for it): only when its number has moved is staff/sync asked,
/// which gives just the records changed since. Every ten minutes it is asked anyway, so a cached file cannot hide anything.</summary>
internal sealed partial class SiteSession
{
    private static readonly TimeSpan FullEvery = TimeSpan.FromMinutes(10), NoFlagEvery = TimeSpan.FromMinutes(3);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private volatile bool _syncing, _noSync;
    private int _received;
    private string _syncError = "";
    private DateTime _lastAsked = DateTime.MinValue;

    public SyncState State
    {
        get
        {
            bool mine = _replica.OwnedBy(_user, Origin);
            var last = mine ? _replica.LastSync : null;
            return new SyncState(!_noSync, mine && _replica.Known, mine && _replica.Ready, _syncing, _received, last?.ToString("yyyy-MM-dd HH:mm") ?? "",
                last is { } l ? (int)Math.Max(0, (DateTime.Now - l).TotalMinutes) : -1, mine ? _replica.Seq : 0, _syncError);
        }
    }

    private bool CopyUsable => !_noSync && _replica.OwnedBy(_user, Origin) && _replica.Ready;

    /// <summary>Asks the site for what changed (everything, the first time) until it has no more. True when the copy is current.</summary>
    public async Task<bool> SyncAsync(CancellationToken ct)
    {
        if (!SignedIn || _noSync) return false;
        await _syncGate.WaitAsync(ct);
        try
        {
            _syncing = true; _received = 0;
            string user = _user, origin = Origin;
            _replica.Own(user, origin);
            for (int i = 0; i < 20000; i++)
            {
                string cursor = _replica.Cursor;
                var r = await SendAsync("GET", cursor.Length == 0 ? "staff/sync" : "staff/sync?c=" + cursor, null, null, ct);
                if (!SignedIn || user != _user || origin != Origin) return false;   // signed out or switched meanwhile
                if (r.Status == 404) { _noSync = true; _syncError = ""; return false; }   // a plugin from before 2.17: the program works as it did (the old hourly lists)
                if (r.Status != 200 || r.Data is not { } d) { _syncError = r.Error ?? (r.Data is { } e && e.TryGetProperty("error", out var m) ? m.GetString() ?? "" : "کد " + r.Status); return false; }
                if (!d.TryGetProperty("cursor", out _)) { _noSync = true; return false; }
                var (got, more) = _replica.Apply(d);
                Interlocked.Add(ref _received, got); Interlocked.Increment(ref _epoch);
                if (!more) { _lastAsked = DateTime.Now; _syncError = ""; return true; }
            }
            return false;
        }
        catch (Exception e) when (e is SqliteException or JsonException or InvalidOperationException or KeyNotFoundException or IOException)
        {
            _syncError = "نسخهٔ این کامپیوتر به‌روز نشد: " + e.Message; return false;
        }
        finally { _syncing = false; _syncGate.Release(); }
    }

    /// <summary>The minute's check.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!SignedIn || _syncing) return;
        if (_noSync) { await WarmAsync(ct); return; }
        bool usable = CopyUsable; string flag = usable ? _replica.Flag : "";
        if (!usable || DateTime.Now - _lastAsked >= (flag.Length > 0 ? FullEvery : NoFlagEvery)) { await SyncAsync(ct); return; }
        if (flag.Length == 0 || !Uri.TryCreate(flag, UriKind.Absolute, out var u) || u.Host != _server.Host || u.Scheme != _server.Scheme) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(u) { Query = "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() }.Uri);
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) { if (DateTime.Now - _lastAsked >= NoFlagEvery) await SyncAsync(ct); return; }
            using var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (d.RootElement.GetProperty("seq").GetInt64() > _replica.Seq || d.RootElement.GetProperty("epoch").GetString() != _replica.Epoch) await SyncAsync(ct);
            else _replica.Touch();   // nothing newer on the site: the copy is current as of now
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { }
    }

    /// <summary>What a list answer holds beside its rows, from the site's last answer of it.</summary>
    private JsonObject? Extras(string what)
    {
        if (_cache.Get("x:" + what) is not { } kept || JsonNode.Parse(kept.Json) is not JsonObject o) return null;
        return o;
    }

    private void KeepExtras(string path, JsonElement data)
    {
        if (!path.StartsWith("staff/shortages?", StringComparison.Ordinal) && path != "staff/shortages") return;
        if (JsonNode.Parse(data.GetRawText()) is not JsonObject o) return;
        foreach (var k in new[] { "total", "page", "per", "items", "offline", "offlineAt" }) o.Remove(k);
        _cache.Put("x:shortages", o.ToJsonString());
    }

    private JsonElement? FromCopy(string path)
    {
        if (!CopyUsable) return null;
        var config = _cache.Get("staff/config") is { } c ? JsonNode.Parse(c.Json) as JsonObject : null;
        try { return _replica.Answer(path, Extras, config); }
        catch (Exception e) when (e is SqliteException or JsonException or InvalidOperationException) { return null; }
    }

    private static readonly Dictionary<string, string> LockKinds = new() { ["build"] = "builds", ["job"] = "jobs", ["invoice"] = "invoices", ["customer"] = "customers", ["product"] = "products", ["ship"] = "ships", ["shortage"] = "shortages" };

    /// <summary>A record taken for editing whose stamp on the site differs from the copy's: the copy is brought up to date and the answer says so ("stale"), so the page is drawn again.</summary>
    private async Task<JsonElement?> CheckStaleAsync(string? body, JsonElement? answer, CancellationToken ct)
    {
        if (!CopyUsable || answer is not { } a || !a.TryGetProperty("updated", out var up) || up.GetString() is not { Length: > 0 } stamp || string.IsNullOrEmpty(body)) return answer;
        try
        {
            using var b = JsonDocument.Parse(body);
            if (!b.RootElement.TryGetProperty("entity", out var e) || !LockKinds.TryGetValue(e.GetString() ?? "", out var kind) || !b.RootElement.TryGetProperty("id", out var i) || !i.TryGetInt64(out long id)) return answer;
            if (_replica.Stamp(kind, id) is not { } mine || mine == stamp) return answer;
            if (!await SyncAsync(ct) || _replica.Stamp(kind, id) != stamp) return answer;
            var o = JsonNode.Parse(a.GetRawText())!.AsObject(); o["stale"] = true; using var d = JsonDocument.Parse(o.ToJsonString()); return d.RootElement.Clone();
        }
        catch (JsonException) { return answer; }
    }
}
