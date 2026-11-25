using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Mazesta.Desk;

/// <summary>The sign-in and every call to the site. The token is kept (sealed with the Windows user's key, in Data next to the exe) and sent as a header; the page only ever gets the answers.</summary>
internal sealed partial class SiteSession : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _file;
    private Uri _server = new(DeskRules.DefaultServer + "/");
    private string _token = "", _user = "";

    public SiteSession(string dataDir)
    {
        _file = Path.Combine(dataDir, "desk.json");
        Queue = new OfflineQueue(Path.Combine(dataDir, "desk-queue.json")); _cache = new LocalStore(Path.Combine(dataDir, "desk-store.db"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MazestaDesk/1");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            if (doc.RootElement.TryGetProperty("server", out var s) && DeskRules.Server(s.GetString()) is { } u) _server = u;
            if (doc.RootElement.TryGetProperty("user", out var us) && us.GetString() is { } who) _user = who;
            if (doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } sealedToken) _token = Unseal(sealedToken);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException) { }
    }

    public Uri Server => _server;
    private string Origin => _server.GetLeftPart(UriPartial.Authority);
    public bool SignedIn => _token.Length > 0;

    private static string Seal(string s) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
    private static string Unseal(string s) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { server = _server.GetLeftPart(UriPartial.Authority), user = _user, token = _token.Length == 0 ? "" : Seal(_token) }));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public bool SetServer(string typed)
    {
        if (DeskRules.Server(typed) is not { } u) return false;
        if (u != _server) { _server = u; _token = ""; _user = ""; ForgetReads(); }   // another site: the old sign-in and its answers are not for it
        Save(); return true;
    }

    public OfflineQueue Queue { get; }
    private readonly LocalStore _cache;
    private int _epoch;                                              // counts the writes made: an answer kept before one of them is not served again without asking
    private readonly ConcurrentDictionary<string, int> _putEpoch = [];
    private DateTime _warmed = DateTime.MinValue;
    private readonly List<string> _conflicts = []; private readonly object _gate = new();
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    private void ForgetReads() { _cache.Clear(); _putEpoch.Clear(); }

    [GeneratedRegex(@"^staff/(revisions|lock|backup|me|shortages/count)")] private static partial Regex VolatileRoute();
    [GeneratedRegex(@"^staff/(products|customers|suppliers)(\?(.*))?$")] private static partial Regex KeptList();
    [GeneratedRegex(@"^staff/(customers|products|builds|jobs|invoices|ships|shortages)/(\d+)$")] private static partial Regex OneRecord();

    /// <summary>The program's own list of what a desk needs without the site (the stock, the customers, the suppliers) keeps itself current from every list the site answers.</summary>
    private void Learn(string path, JsonElement? data)
    {
        if (KeptList().Match(path) is { Success: true } m && data is { } d && d.TryGetProperty("items", out var items)) _cache.Upsert(m.Groups[1].Value, items);
    }

    /// <summary>A record opened while changes of it wait in the queue shows those changes (marked "pending"), so a save made without the site is not seen to vanish.</summary>
    private JsonElement? WithPending(string path, JsonElement? data)
    {
        if (data is not { } d || OneRecord().Match(path) is not { Success: true } m || JsonNode.Parse(d.GetRawText()) is not JsonObject node) return data;
        var mine = Queue.Waiting("staff/" + m.Groups[1].Value).Where(q => JsonNode.Parse(q.Body) is JsonObject b && b["id"]?.ToString() == m.Groups[2].Value).ToList();
        if (mine.Count == 0) return data;
        var target = node["customer"] as JsonObject ?? node["product"] as JsonObject ?? node; bool any = false;
        foreach (var q in mine)
            foreach (var (k, v) in (JsonObject)JsonNode.Parse(q.Body)!)
                if (v is not null && k is not ("id" or "updated" or "offline_at" or "force") && target[k] is { } cur && (cur is JsonArray && v is JsonArray || cur is JsonObject && v is JsonObject || cur is JsonValue && v is JsonValue)) { target[k] = v.DeepClone(); any = true; }
        if (!any) return data;
        node["pending"] = true; using var doc = JsonDocument.Parse(node.ToJsonString()); return doc.RootElement.Clone();
    }

    private static JsonElement? Parse(string json) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); } catch (JsonException) { return null; } }

    /// <summary>One call. <paramref name="data"/> is the site's answer as JSON (an object), or null when the site sent something that is not JSON. A read answered a minute ago with nothing written since is not asked again.
    /// With the site out of reach: a stock, customer or supplier search is answered from the PC's own copy, any other read gives the last good answer (marked "offline"), a new record or a change waits in the queue
    /// (status 202), and anything else is refused with the reason.</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> CallAsync(string method, string path, string? body, CancellationToken ct)
    {
        bool read = method == "GET";
        if (read && !VolatileRoute().IsMatch(path) && _cache.Get(path) is { } hit && DateTime.Now - hit.At < Fresh && _putEpoch.TryGetValue(path, out int ep) && ep == Volatile.Read(ref _epoch) && Parse(hit.Json) is { } cached) return (200, WithPending(path, cached), null);
        var r = await SendAsync(method, path, body, null, ct);
        if (!read && r.Status is >= 200 and < 300 && !path.StartsWith("staff/lock", StringComparison.Ordinal)) Interlocked.Increment(ref _epoch);   // (the lock renewals every 45 s are not a change of anything)
        if (r.Status == 200 && read && r.Raw.Length > 0) { _cache.Put(path, r.Raw); _putEpoch[path] = Volatile.Read(ref _epoch); Learn(path, r.Data); return (r.Status, WithPending(path, r.Data), r.Error); }
        if (r.Status != 0 || ct.IsCancellationRequested || !DeskRules.MethodAllowed(method) || !DeskRules.PathAllowed(path)) return (r.Status, r.Data, r.Error);
        if (read)
        {
            if (KeptList().Match(path) is { Success: true } m && _cache.List(m.Groups[1].Value, m.Groups[3].Value) is { } local) return (200, local, null);
            if (_cache.Get(path) is { } old)
            {
                try { if (JsonNode.Parse(old.Json) is JsonObject o) { o["offline"] = true; o["offlineAt"] = old.At.ToString("yyyy-MM-dd HH:mm"); using var doc = JsonDocument.Parse(o.ToJsonString()); return (200, WithPending(path, doc.RootElement.Clone()), null); } }
                catch (JsonException) { }
            }
        }
        if (OfflineQueue.Queueable(method, path, body))
        {
            var q = Queue.Add(path, string.IsNullOrEmpty(body) ? "{}" : body, _user, Origin);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { queued = true, id = q.Id, pending = Queue.Pending }));
            return (202, doc.RootElement.Clone(), null);
        }
        return (0, null, method == "POST" ? "اتصال به سایت نیست و این کار بدون سایت انجام نمی‌شود (قطعی‌کردن، ابطال و حذف شماره و بررسی سایت را لازم دارند). ثبت و ویرایش پرونده‌ها، پرداخت و فاکتور پیش‌نویس در صف می‌ماند." : r.Error);
    }

    /// <summary>An edit (a body with an id) goes with the time it was saved, so the site can tell whose change is the later one; a new record needs none.</summary>
    private static string WithTime(QueuedCall q)
    {
        try
        {
            if (JsonNode.Parse(q.Body) is JsonObject o && o["id"] is { } id && long.TryParse(id.ToString(), out long n) && n > 0) { o["offline_at"] = q.At.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"); return o.ToJsonString(); }
        }
        catch (JsonException) { }
        return q.Body;
    }

    /// <summary>Sends what waited while the line was down, oldest first; each call carries its own id so the site books a repeat once, and a change made offline carries its time.</summary>
    public Task<(int Sent, int Refused, bool Stopped)> FlushAsync(CancellationToken ct) => Queue.FlushAsync(async q =>
    {
        var r = await SendAsync("POST", q.Path, WithTime(q), q.Id, ct);
        if (r.Status is >= 200 and < 300)
        {
            Interlocked.Increment(ref _epoch);
            if (r.Conflict.Length > 0) lock (_gate) _conflicts.Add(r.Conflict == "side"
                ? "تغییری که بدون اتصال ثبت کرده بودید قدیمی‌تر از تغییر یک نفر دیگر بود: در «تاریخچه» پرونده نگه داشته شد و حالت فعلی همان تغییر دیگران است؛ از «انتخاب فیلدها» می‌توانید برش دارید."
                : "هم‌زمان با تغییر شما کس دیگری هم همین پرونده را عوض کرده بود؛ تغییر شما دیرتر بود و اعمال شد و نسخهٔ او در «تاریخچه» پرونده مانده است.");
        }
        string? err = r.Data is { } d && d.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : r.Error;
        return (r.Status, err);
    }, _user, Origin);

    /// <summary>What the last sends found (one text each), handed over once.</summary>
    public string[] TakeConflicts() { lock (_gate) { var a = _conflicts.ToArray(); _conflicts.Clear(); return a; } }

    /// <summary>Keeps the PC's own copy of the suppliers, the stock and the customers whole (hourly, in the background, page by page), so a search works with no line.</summary>
    public async Task WarmAsync(CancellationToken ct)
    {
        if (!SignedIn || DateTime.Now - _warmed < TimeSpan.FromMinutes(60)) return;
        _warmed = DateTime.Now;
        foreach (string kind in new[] { "suppliers", "products", "customers" })
            for (int page = 1; page <= 400 && !ct.IsCancellationRequested; page++)
            {
                var r = await SendAsync("GET", $"staff/{kind}?per=50&page={page}", null, null, ct);
                if (r.Status != 200 || r.Data is not { } d || !d.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) break;
                _cache.Upsert(kind, items);
                if (kind == "suppliers" || items.GetArrayLength() < 50 || (d.TryGetProperty("total", out var t) && t.TryGetInt32(out int total) && page * 50 >= total)) break;
            }
    }

    private async Task<(int Status, JsonElement? Data, string? Error, string Raw, string Conflict)> SendAsync(string method, string path, string? body, string? opId, CancellationToken ct)
    {
        if (!DeskRules.MethodAllowed(method) || !DeskRules.PathAllowed(path)) return (0, null, "درخواست مجاز نیست.", "", "");
        using var req = new HttpRequestMessage(new HttpMethod(method), new Uri(_server, DeskRules.ApiRoot + path));
        if (_token.Length > 0) req.Headers.TryAddWithoutValidation("X-MZC-Token", _token);
        if (opId is not null) req.Headers.TryAddWithoutValidation("X-MZC-Op", opId);
        if (method == "POST") req.Content = new StringContent(string.IsNullOrEmpty(body) ? "{}" : body, Encoding.UTF8, "application/json");
        try
        {
            using var res = await _http.SendAsync(req, ct);
            string text = await res.Content.ReadAsStringAsync(ct);
            JsonElement? data = null;
            try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.ValueKind == JsonValueKind.Object) data = doc.RootElement.Clone(); } catch (JsonException) { }
            int status = (int)res.StatusCode;
            if (status == 401 && path != "staff/login") { _token = ""; Save(); }   // the site no longer knows this sign-in
            string conflict = res.Headers.TryGetValues("X-MZC-Conflict", out var cv) ? cv.FirstOrDefault() ?? "" : "";
            return (status, data, data is null ? $"پاسخ نامعتبر از سایت ({status}). نشانی سایت را بررسی کنید." : null, data is null ? "" : text, conflict);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (0, null, ct.IsCancellationRequested ? "لغو شد." : "اتصال به سایت برقرار نشد: " + e.Message, "", "");
        }
    }

    /// <summary>Signs in; on success the token is kept and the answer (without it) returned.</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> LoginAsync(string user, string password, CancellationToken ct)
    {
        string body = JsonSerializer.Serialize(new { username = user, password, device = Environment.MachineName });
        var r = await CallAsync("POST", "staff/login", body, ct);
        if (r.Status == 200 && r.Data is { } d && d.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } tok)
        {
            string who = user.Trim().ToLowerInvariant();
            if (who != _user) ForgetReads();   // a sign-in that ran out, then someone else: they may see less than the last person did
            _token = tok; _user = who; Save(); _warmed = DateTime.MinValue;
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(d.EnumerateObject().Where(p => p.Name != "token").ToDictionary(p => p.Name, p => p.Value)));
            return (200, doc.RootElement.Clone(), null);
        }
        return r;
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        if (_token.Length > 0) await CallAsync("POST", "staff/logout", null, ct);
        _token = ""; _user = ""; Save(); ForgetReads();   // the next person may see less than this one did
    }

    public void Dispose() { _http.Dispose(); _cache.Dispose(); }
}
