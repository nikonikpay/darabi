using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Mazesta.Desk;

/// <summary>A call that could not reach the site and waits to be sent: its own id (the site books a repeated id once), the route, the body, and the site's refusal when it said no.</summary>
public sealed record QueuedCall(string Id, string Path, string Body, DateTime At, string? Error = null, string User = "", string Server = "");

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

    /// <summary>True for what may wait for the site: a NEW payment, and a new record or a change of an existing one of the kinds that keep versions (a person's own version and the other person's both stay in the
    /// record's history, so a change made offline loses nothing). Finalising, voiding and deleting need the site's numbering and checks; changing a payment would move money, so those are refused.</summary>
    public static bool Queueable(string method, string path, string? body)
    {
        bool versioned = path is "staff/invoices" or "staff/customers" or "staff/products" or "staff/builds" or "staff/jobs" or "staff/ships" or "staff/shortages";
        if (method != "POST" || !(versioned || path == "staff/payments")) return false;
        try
        {
            using var d = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
            if (d.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (versioned) return true;
            if (!d.RootElement.TryGetProperty("id", out var id)) return true;
            return id.ValueKind == JsonValueKind.Null || (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long n) && n == 0);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The calls waiting to go (not refused) to this exact route, oldest first.</summary>
    public IReadOnlyList<QueuedCall> Waiting(string path) { lock (_lock) return _items.Where(x => x.Error is null && x.Path == path).ToList(); }

    public int Pending { get { lock (_lock) return _items.Count(x => x.Error is null); } }
    public IReadOnlyList<QueuedCall> Refused { get { lock (_lock) return _items.Where(x => x.Error is not null).ToList(); } }

    public QueuedCall Add(string path, string body, string user = "", string server = "")
    {
        var q = new QueuedCall(Guid.NewGuid().ToString("N"), path, body, DateTime.Now, null, user, server);
        lock (_lock) { _items.Add(q); Save(); }
        return q;
    }

    /// <summary>Drops a refused call the person has seen.</summary>
    public void Discard(string id) { lock (_lock) { _items.RemoveAll(x => x.Id == id); Save(); } }

    /// <summary>Sends the waiting calls oldest first with <paramref name="send"/> (status, error). A call that got through leaves the queue; one the site refused (4xx) stays, marked, for the
    /// person to see; the run stops at the first call that could not reach the site (status 0), whose sign-in ran out (401) or that failed on the site's side (5xx): the rest wait, in order.</summary>
    public async Task<(int Sent, int Refused, bool Stopped)> FlushAsync(Func<QueuedCall, Task<(int Status, string? Error)>> send, string owner = "", string server = "")
    {
        int sent = 0, refused = 0;
        while (true)
        {
            // a call goes out only under the person who made it (the site checks the roles of whoever is signed in) and only to the site it was made for (a test site and the
            // real one may share a user name); an old call with no name or site goes under anyone
            QueuedCall? next; lock (_lock) next = _items.FirstOrDefault(x => x.Error is null && (x.User == "" || x.User == owner) && (x.Server == "" || x.Server == server));
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
