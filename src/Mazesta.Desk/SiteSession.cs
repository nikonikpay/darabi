using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mazesta.Desk;

/// <summary>The sign-in and every call to the site. The token is kept (sealed with the Windows user's key, in Data next to the exe) and sent as a header; the page only ever gets the answers.</summary>
internal sealed class SiteSession : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _file;
    private Uri _server = new(DeskRules.DefaultServer + "/");
    private string _token = "", _user = "";

    public SiteSession(string dataDir)
    {
        _file = Path.Combine(dataDir, "desk.json");
        Queue = new OfflineQueue(Path.Combine(dataDir, "desk-queue.json")); _cache = new ReadCache(Path.Combine(dataDir, "desk-cache.json"));
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
        if (u != _server) { _server = u; _token = ""; _user = ""; }   // another site: the old sign-in is not for it
        Save(); return true;
    }

    public OfflineQueue Queue { get; }
    private readonly ReadCache _cache;

    /// <summary>One call. <paramref name="data"/> is the site's answer as JSON (an object), or null when the site sent something that is not JSON. With the site out of reach: a read gives the last
    /// good answer (marked "offline"), a new payment or draft invoice waits in the queue (status 202), anything else is refused with the reason.</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> CallAsync(string method, string path, string? body, CancellationToken ct)
    {
        var r = await SendAsync(method, path, body, null, ct);
        if (r.Status == 200 && method == "GET" && r.Raw.Length > 0) { _cache.Put(path, r.Raw); return (r.Status, r.Data, r.Error); }
        if (r.Status != 0 || ct.IsCancellationRequested || !DeskRules.MethodAllowed(method) || !DeskRules.PathAllowed(path)) return (r.Status, r.Data, r.Error);
        if (method == "GET" && _cache.Get(path) is { } old)
        {
            try { if (System.Text.Json.Nodes.JsonNode.Parse(old.Json) is System.Text.Json.Nodes.JsonObject o) { o["offline"] = true; o["offlineAt"] = old.At.ToString("yyyy-MM-dd HH:mm"); using var doc = JsonDocument.Parse(o.ToJsonString()); return (200, doc.RootElement.Clone(), null); } }
            catch (JsonException) { }
        }
        if (OfflineQueue.Queueable(method, path, body))
        {
            var q = Queue.Add(path, string.IsNullOrEmpty(body) ? "{}" : body, _user);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { queued = true, id = q.Id, pending = Queue.Pending }));
            return (202, doc.RootElement.Clone(), null);
        }
        return (0, null, method == "POST" ? "اتصال به سایت نیست و این کار بدون سایت انجام نمی‌شود (قطعی‌کردن، ابطال و حذف شماره و بررسی سایت را لازم دارند). ثبت پرداخت و پیش‌نویس فاکتور تازه در صف می‌ماند." : r.Error);
    }

    /// <summary>Sends what waited while the line was down, oldest first; each call carries its own id so the site books a repeat once.</summary>
    public Task<(int Sent, int Refused, bool Stopped)> FlushAsync(CancellationToken ct) => Queue.FlushAsync(async q =>
    {
        var r = await SendAsync("POST", q.Path, q.Body, q.Id, ct);
        string? err = r.Data is { } d && d.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : r.Error;
        return (r.Status, err);
    }, _user);

    private async Task<(int Status, JsonElement? Data, string? Error, string Raw)> SendAsync(string method, string path, string? body, string? opId, CancellationToken ct)
    {
        if (!DeskRules.MethodAllowed(method) || !DeskRules.PathAllowed(path)) return (0, null, "درخواست مجاز نیست.", "");
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
            return (status, data, data is null ? $"پاسخ نامعتبر از سایت ({status}). نشانی سایت را بررسی کنید." : null, data is null ? "" : text);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (0, null, ct.IsCancellationRequested ? "لغو شد." : "اتصال به سایت برقرار نشد: " + e.Message, "");
        }
    }

    /// <summary>Signs in; on success the token is kept and the answer (without it) returned.</summary>
    public async Task<(int Status, JsonElement? Data, string? Error)> LoginAsync(string user, string password, CancellationToken ct)
    {
        string body = JsonSerializer.Serialize(new { username = user, password, device = Environment.MachineName });
        var r = await CallAsync("POST", "staff/login", body, ct);
        if (r.Status == 200 && r.Data is { } d && d.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } tok)
        {
            _token = tok; _user = user.Trim().ToLowerInvariant(); Save();
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(d.EnumerateObject().Where(p => p.Name != "token").ToDictionary(p => p.Name, p => p.Value)));
            return (200, doc.RootElement.Clone(), null);
        }
        return r;
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        if (_token.Length > 0) await CallAsync("POST", "staff/logout", null, ct);
        _token = ""; _user = ""; Save(); _cache.Clear();   // the next person may see less than this one did
    }

    public void Dispose() => _http.Dispose();
}
