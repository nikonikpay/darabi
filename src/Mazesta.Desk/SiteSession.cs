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
    // answers come compressed (the site compresses when asked: a first copy is mostly text). A redirect is not followed: it would carry the sign-in token to wherever it points
    private readonly HttpClient _http = new(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = 64 * 1024 * 1024 };
    private readonly string _file;
    private Uri _server = new(DeskRules.DefaultServer + "/");
    private string _token = "", _user = "";

    public SiteSession(string dataDir)
    {
        _file = Path.Combine(dataDir, "desk.json");
        Queue = new OfflineQueue(Path.Combine(dataDir, "desk-queue.json")); _cache = new LocalStore(Path.Combine(dataDir, "desk-store.db")); _replica = new Replica(Path.Combine(dataDir, "desk-replica.db"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MazestaDesk/1");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            if (doc.RootElement.TryGetProperty("server", out var s) && DeskRules.Server(s.GetString()) is { } u) _server = u;
            if (doc.RootElement.TryGetProperty("user", out var us) && us.GetString() is { } who) _user = who;
            if (doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } sealedToken) _token = Unseal(sealedToken);
            if (doc.RootElement.TryGetProperty("proof", out var p) && p.GetString() is { Length: > 0 } sealedProof) _proof = JsonSerializer.Deserialize<Proof>(Unseal(sealedProof));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException) { }
    }

    public Uri Server => _server;
    private string Origin => _server.GetLeftPart(UriPartial.Authority);
    public bool SignedIn => _token.Length > 0 || _pass is not null;
    private bool SignedOut => !SignedIn && _user.Length > 0;   // someone signed out (or was put out): what is kept is for nobody until a sign-in

    /// <summary>What lets the last person who signed in with the site get in without it: a salted slow hash of the password (never the password), sealed in desk.json.</summary>
    private sealed record Proof(string User, byte[] Salt, byte[] Hash, DateTime At);
    private Proof? _proof;
    private string? _pass;   // the password of a sign-in made without the site and with no token, kept in memory only until the site answers and gives one
    // ponytail: 30 days from the last sign-in with the site; a person switched off on the site can still open this PC's copy that long if it never connects
    private static readonly TimeSpan OfflineFor = TimeSpan.FromDays(30);
    private static byte[] Hash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);

    private static string Seal(string s) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
    private static string Unseal(string s) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser));

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temp = _file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { server = _server.GetLeftPart(UriPartial.Authority), user = _user, token = _token.Length == 0 ? "" : Seal(_token), proof = _proof is null ? "" : Seal(JsonSerializer.Serialize(_proof)) }));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public bool SetServer(string typed)
    {
        if (DeskRules.Server(typed) is not { } u) return false;
        if (u != _server) { _server = u; _token = ""; _user = ""; _pass = null; _proof = null; ForgetReads(); }   // another site: the old sign-in and its answers are not for it
        Save(); return true;
    }

    public OfflineQueue Queue { get; }
    private readonly LocalStore _cache;
    private int _epoch;                                              // counts the writes made: an answer kept before one of them is not served again without asking
    private readonly ConcurrentDictionary<string, int> _putEpoch = [];
    private DateTime _warmed = DateTime.MinValue;
    private readonly List<string> _conflicts = []; private readonly object _gate = new();
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    private readonly Replica _replica;
    private volatile bool _noGzip;
    private volatile Task _after = Task.CompletedTask; private volatile string _afterRecord = "\n";

    private void ForgetReads() { _cache.Clear(); _putEpoch.Clear(); _replica.Clear(); _noSync = false; _noGzip = false; _lastAsked = DateTime.MinValue; }

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

    /// <summary>One call. <paramref name="data"/> is the site's answer as JSON (an object), or null when the site sent something that is not JSON. A list or record the PC's copy can answer is answered
    /// from it without asking the site; another read answered a minute ago with nothing written since is not asked again.
    /// With the site out of reach: a stock, customer or supplier search is answered from the PC's own lists, any other read gives the last good answer (marked "offline"), a new record or a change waits in the queue
    /// (status 202), and anything else is refused with the reason.</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> CallAsync(string method, string path, string? body, CancellationToken ct)
    {
        bool read = method == "GET";
        if (SignedOut) return (401, null, "اول وارد شوید.");
        // right after a save, what follows from it (the stock, a new product, a task that started) is still on its way: any read but the saved record itself waits for it
        if (read && !_after.IsCompleted && !path.StartsWith(_afterRecord, StringComparison.Ordinal)) await Task.WhenAny(_after, Task.Delay(TimeSpan.FromSeconds(8), ct));
        if (read && FromCopy(path) is { } copied) return (200, WithPending(path, copied), null);
        if (read && !VolatileRoute().IsMatch(path) && _cache.Get(path) is { } hit && DateTime.Now - hit.At < Fresh && _putEpoch.TryGetValue(path, out int ep) && ep == Volatile.Read(ref _epoch) && Parse(hit.Json) is { } cached) return (200, WithPending(path, cached), null);
        var r = await SendAsync(method, path, body, null, ct);
        if (!read && r.Status is >= 200 and < 300)
        {
            if (path.StartsWith("staff/lock", StringComparison.Ordinal)) return (r.Status, await CheckStaleAsync(body, r.Data, ct), r.Error);   // (the lock renewals every 45 s are not a change of anything)
            Interlocked.Increment(ref _epoch);
            if (CopyUsable)
            {
                _replica.Written(path, r.Data);   // the saved record shows at once; what follows from it (stock, totals) is asked for right away
                _afterRecord = r.Data is { } saved && saved.TryGetProperty("id", out var sid) && path.Split('/') is { Length: >= 2 } seg ? $"staff/{seg[1]}/{sid.GetRawText()}" : "\n";
                _after = Task.Run(() => SyncAsync(ct), ct);
            }
        }
        if (!read && r.Status == 409 && CopyUsable) await SyncAsync(ct);   // someone saved first: their version is fetched, so opening it again shows it
        if (r.Status == 200 && read && r.Raw.Length > 0) { _cache.Put(path, r.Raw); _putEpoch[path] = Volatile.Read(ref _epoch); Learn(path, r.Data); if (r.Data is { } fresh) KeepExtras(path, fresh); return (r.Status, WithPending(path, r.Data), r.Error); }
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
    public async Task<(int Sent, int Refused, bool Stopped)> FlushAsync(CancellationToken ct)
    {
        var f = await SendQueueAsync(ct);
        if (f.Sent > 0 && CopyUsable) await SyncAsync(ct);
        return f;
    }

    private Task<(int Sent, int Refused, bool Stopped)> SendQueueAsync(CancellationToken ct) => Queue.FlushAsync(async q =>
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
                if (r.Status == 0) { _warmed = DateTime.MinValue; return; }   // no line: not this hour's copy, the next tick tries again
                if (r.Status != 200 || r.Data is not { } d|| !d.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) break;
                _cache.Upsert(kind, items);
                if (kind == "suppliers" || items.GetArrayLength() < 50 || (d.TryGetProperty("total", out var t) && t.TryGetInt32(out int total) && page * 50 >= total)) break;
            }
    }

    /// <summary>One request. A 401 after a sign-in made without the site signs in with it now and sends again; a sign-in the site rejects ends here (and no longer works without the site).</summary>
    private async Task<(int Status, JsonElement? Data, string? Error, string Raw, string Conflict)> SendAsync(string method, string path, string? body, string? opId, CancellationToken ct)
    {
        string used = _token;
        var r = await RawAsync(method, path, body, opId, ct);
        if (r.Status != 401) return r;
        bool rejected = used.Length > 0;
        if (_token == used && _pass is { } pass)
        {
            var s = await SignInAsync(_user, pass, ct);
            if (s.Status is not (200 or 401 or 403)) return (0, null, s.Error, "", "");   // the site cannot sign anyone in just now: the call waits
            rejected = s.Status != 200;
        }
        if (_token.Length > 0 && _token != used) return await RawAsync(method, path, body, opId, ct);
        if (rejected) { _token = ""; _pass = null; _proof = null; Save(); }
        return r;
    }

    private async Task<(int Status, JsonElement? Data, string? Error, string Raw, string Conflict)> RawAsync(string method, string path, string? body, string? opId, CancellationToken ct)
    {
        if (!DeskRules.MethodAllowed(method) || !DeskRules.PathAllowed(path)) return (0, null, "درخواست مجاز نیست.", "", "");
        using var req = new HttpRequestMessage(new HttpMethod(method), new Uri(_server, DeskRules.ApiRoot + path));
        if (_token.Length > 0) req.Headers.TryAddWithoutValidation("X-MZC-Token", _token);
        if (opId is not null) req.Headers.TryAddWithoutValidation("X-MZC-Op", opId);
        // a large save goes gzipped, once the site has shown (by answering staff/sync) that its plugin opens it
        bool zip = method == "POST" && !_noGzip && body is { Length: > 1400 } && _replica.Known;
        if (zip)
        {
            using var ms = new MemoryStream(); using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(Encoding.UTF8.GetBytes(body!));
            req.Content = new ByteArrayContent(ms.ToArray()); req.Content.Headers.ContentType = new("application/octet-stream"); req.Headers.TryAddWithoutValidation("X-MZC-Gzip", "1");
        }
        else if (method == "POST") req.Content = new StringContent(string.IsNullOrEmpty(body) ? "{}" : body, Encoding.UTF8, "application/json");
        try
        {
            using var res = await _http.SendAsync(req, ct);
            string text = await res.Content.ReadAsStringAsync(ct);
            JsonElement? data = null;
            try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.ValueKind == JsonValueKind.Object) data = doc.RootElement.Clone(); } catch (JsonException) { }
            int status = (int)res.StatusCode;
            // a firewall in front of the site that will not take the compressed form: the same save goes as it is, and the rest too
            if (zip && (status == 415 || (data is null && status is >= 400 and < 500))) { _noGzip = true; return await RawAsync(method, path, body, opId, ct); }
            if (status is >= 300 and < 400) return (status, null, $"نشانی سایت به «{res.Headers.Location}» تغییر مسیر می‌دهد؛ همان نشانی را در تنظیمات اتصال بنویسید.", "", "");
            string conflict = res.Headers.TryGetValues("X-MZC-Conflict", out var cv) ? cv.FirstOrDefault() ?? "" : "";
            return (status, data, data is null ? $"پاسخ نامعتبر از سایت ({status}). نشانی سایت را بررسی کنید." : null, data is null ? "" : text, conflict);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (0, null, ct.IsCancellationRequested ? "لغو شد." : "اتصال به سایت برقرار نشد: " + e.Message, "", "");
        }
    }

    /// <summary>Signs in; on success the token is kept and the answer (without it) returned. With the site out of reach, the person who last signed in here gets in with the same password (marked "offline").</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> LoginAsync(string user, string password, CancellationToken ct)
    {
        string who = user.Trim().ToLowerInvariant();
        var r = await SignInAsync(who, password, ct);
        if ((r.Status == 0 || r.Status >= 500) && !ct.IsCancellationRequested) return OfflineLogin(who, password);
        if (r.Status is 401 or 403 && _proof?.User == who) { _proof = null; Save(); }   // a password changed or an account switched off on the site stops working here too
        return r;
    }

    private async Task<(int Status, JsonElement? Data, string? Error)> SignInAsync(string who, string password, CancellationToken ct)
    {
        var r = await RawAsync("POST", "staff/login", JsonSerializer.Serialize(new { username = who, password, device = Environment.MachineName }), null, ct);
        if (r.Status != 200) return (r.Status, r.Data, r.Error);
        if (r.Data is not { } d || !d.TryGetProperty("token", out var t) || t.GetString() is not { Length: > 0 } tok) return (0, null, "پاسخ نامعتبر از سایت. نشانی سایت را بررسی کنید.");
        if (who != _user) ForgetReads();   // someone else than last time: they may see less than that person did
        string me = JsonSerializer.Serialize(d.EnumerateObject().Where(p => p.Name != "token").ToDictionary(p => p.Name, p => p.Value));
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        _token = tok; _user = who; _pass = null; _proof = new Proof(who, salt, Hash(password, salt), DateTime.UtcNow); Save(); _warmed = DateTime.MinValue; _noSync = false; _noGzip = false; _lastAsked = DateTime.MinValue;
        _cache.Put("staff/me", me); Interlocked.Increment(ref _epoch);   // who this is, for a start without the site
        return (200, Parse(me), null);
    }

    private (int Status, JsonElement? Data, string? Error) OfflineLogin(string who, string password)
    {
        if (_proof is not { } p || p.User != who || DateTime.UtcNow - p.At > OfflineFor || _cache.Get("staff/me") is not { } kept || JsonNode.Parse(kept.Json) is not JsonObject me)
            return (0, null, "اتصال به سایت نیست. بدون اتصال فقط کاربری وارد می‌شود که در ۳۰ روز گذشته یک بار با اتصال روی همین کامپیوتر وارد شده باشد.");
        if (!CryptographicOperations.FixedTimeEquals(Hash(password, p.Salt), p.Hash)) return (401, null, "اتصال به سایت نیست و گذرواژه با آخرین ورود روی این کامپیوتر یکی نیست.");
        _user = who; _pass = _token.Length == 0 ? password : null; Save();
        me["offline"] = true;
        return (200, Parse(me.ToJsonString()), null);
    }

    /// <summary>Signs out. The PC's own copy stays (sealed, and for nobody until a sign-in), so the same person can get in again without the site; someone else signing in clears it.</summary>
    public async Task LogoutAsync(CancellationToken ct)
    {
        _pass = null;
        if (_token.Length > 0) await RawAsync("POST", "staff/logout", null, null, ct);
        _token = ""; Save();
    }

    public void Dispose() { _http.Dispose(); _cache.Dispose(); _replica.Dispose(); }
}
