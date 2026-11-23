using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mazesta.Desk;

/// <summary>A call that could not reach the site and waits to be sent: its own id (the site books a repeated id once), the route, the body, and the site's refusal when it said no.</summary>
public sealed record QueuedCall(string Id, string Path, string Body, DateTime At, string? Error = null);

/// <summary>Calls made while the site cannot be reached, kept in one file next to the program (sealed with the Windows user's key; no database, nothing to install). Only a new payment and a
/// draft invoice may wait: finalising, voiding and deleting need the site's numbering and checks, so they are refused offline instead of queued.</summary>
public sealed class OfflineQueue
{
    private readonly string _file; private readonly object _lock = new(); private List<QueuedCall> _items = [];

    public OfflineQueue(string file)
    {
        _file = file;
        try { _items = JsonSerializer.Deserialize<List<QueuedCall>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser))) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException) { _items = []; }
    }

    /// <summary>True for a NEW payment or a NEW draft invoice (an existing record may have been changed by someone else meanwhile, so changing one needs the site).</summary>
    public static bool Queueable(string method, string path, string? body)
    {
        if (method != "POST" || path is not ("staff/payments" or "staff/invoices")) return false;
        try
        {
            using var d = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!d.RootElement.TryGetProperty("id", out var id)) return true;
            return id.ValueKind == JsonValueKind.Null || (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long n) && n == 0);
        }
        catch (JsonException) { return false; }
    }

    public int Pending { get { lock (_lock) return _items.Count(x => x.Error is null); } }
    public IReadOnlyList<QueuedCall> Refused { get { lock (_lock) return _items.Where(x => x.Error is not null).ToList(); } }

    public QueuedCall Add(string path, string body)
    {
        var q = new QueuedCall(Guid.NewGuid().ToString("N"), path, body, DateTime.Now);
        lock (_lock) { _items.Add(q); Save(); }
        return q;
    }

    /// <summary>Drops a refused call the person has seen.</summary>
    public void Discard(string id) { lock (_lock) { _items.RemoveAll(x => x.Id == id); Save(); } }

    /// <summary>Sends the waiting calls oldest first with <paramref name="send"/> (status, error). A call that got through leaves the queue; one the site refused (4xx) stays, marked, for the
    /// person to see; the run stops at the first call that could not reach the site (status 0), whose sign-in ran out (401) or that failed on the site's side (5xx): the rest wait, in order.</summary>
    public async Task<(int Sent, int Refused, bool Stopped)> FlushAsync(Func<QueuedCall, Task<(int Status, string? Error)>> send)
    {
        int sent = 0, refused = 0;
        while (true)
        {
            QueuedCall? next; lock (_lock) next = _items.FirstOrDefault(x => x.Error is null);
            if (next is null) return (sent, refused, false);
            var (status, error) = await send(next);
            if (status is 0 or 401 || status >= 500) return (sent, refused, true);
            lock (_lock)
            {
                int at = _items.FindIndex(x => x.Id == next.Id);
                if (at >= 0) { if (status is >= 200 and < 300) { _items.RemoveAt(at); sent++; } else { _items[at] = next with { Error = error ?? ("کد " + status) }; refused++; } }
                Save();
            }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temp = _file + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_items)), null, DataProtectionScope.CurrentUser));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>The last good answer of each staff read, so a list opened before the line dropped can still be opened (marked as old). Sealed like the queue; small and bounded.</summary>
public sealed class ReadCache
{
    private const int MaxEntries = 150, MaxBytes = 4_000_000;
    private sealed record Row(string Json, DateTime At);
    private readonly string _file; private readonly object _lock = new(); private Dictionary<string, Row> _map = [];

    public ReadCache(string file)
    {
        _file = file;
        try { _map = JsonSerializer.Deserialize<Dictionary<string, Row>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser))) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException) { _map = []; }
    }

    public void Put(string path, string json)
    {
        lock (_lock)
        {
            _map[path] = new Row(json, DateTime.Now);
            while (_map.Count > MaxEntries || _map.Values.Sum(v => v.Json.Length) > MaxBytes) _map.Remove(_map.OrderBy(p => p.Value.At).First().Key);
            Save();
        }
    }

    public void Clear() { lock (_lock) { _map.Clear(); Save(); } }

    public (string Json, DateTime At)? Get(string path) { lock (_lock) return _map.TryGetValue(path, out var v) ? (v.Json, v.At) : null; }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string temp = _file + ".tmp";
            File.WriteAllBytes(temp, ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_map)), null, DataProtectionScope.CurrentUser));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
