using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
namespace Mazesta.Desk;

/// <summary>
/// This PC's own copy of the site's records (everything the signed-in person may see), kept current by the numbered changes the site sends (staff/sync). The lists and records the pages
/// read are answered from it, so they open at once and without the site. Each record is gzipped and sealed (AES-GCM) with a key that only this Windows user can open.
/// One person and one site at a time: another of either empties it. A damaged file is started again - the site holds the real data.
/// </summary>
internal sealed partial class Replica : IDisposable
{
    private readonly SqliteConnection _db; private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<long, Row>> _mem = [];
    private readonly AesGcm _aes;
    private sealed class Row(JsonObject doc) { public JsonObject Doc = doc; public string? Hay; }

    public Replica(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _db = Open(file);
        byte[]? key = null;
        if (Meta("key") is { } k) { try { key = ProtectedData.Unprotect(Convert.FromBase64String(k), null, DataProtectionScope.CurrentUser); } catch (Exception e) when (e is CryptographicException or FormatException) { } }
        if (key is not { Length: 32 })
        {
            Clear(); key = RandomNumberGenerator.GetBytes(32);
            SetMeta("key", Convert.ToBase64String(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)));
        }
        _aes = new AesGcm(key, 16);
    }

    private static SqliteConnection Open(string file)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var c = new SqliteConnection("Data Source=" + file); c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; CREATE TABLE IF NOT EXISTS rec (kind TEXT NOT NULL, id INTEGER NOT NULL, blob BLOB NOT NULL, PRIMARY KEY (kind, id)); CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);";
                cmd.ExecuteNonQuery(); return c;
            }
            catch (SqliteException) when (attempt == 0) { SqliteConnection.ClearAllPools(); try { File.Delete(file); } catch (IOException) { } }
        }
    }

    private SqliteCommand Cmd(string sql, SqliteTransaction? tx, params (string, object)[] args)
    {
        var c = _db.CreateCommand(); c.CommandText = sql; c.Transaction = tx;
        foreach (var (k, v) in args) c.Parameters.AddWithValue(k, v);
        return c;
    }

    private string? Meta(string k) { lock (_lock) { using var c = Cmd("SELECT v FROM meta WHERE k = $k", null, ("$k", k)); return c.ExecuteScalar() as string; } }
    private void SetMeta(string k, string v, SqliteTransaction? tx = null) { lock (_lock) { using var c = Cmd("REPLACE INTO meta (k, v) VALUES ($k, $v)", tx, ("$k", k), ("$v", v)); c.ExecuteNonQuery(); } }

    /* ---------- what is kept ---------- */

    public string Cursor => Meta("cursor") ?? "";
    /// <summary>The first copy came in whole.</summary>
    public bool Ready => Meta("ready") == "1";
    /// <summary>The site has answered staff/sync at least once (its plugin keeps changes).</summary>
    public bool Known => Meta("known") == "1";
    public long Seq => long.TryParse(Meta("seq"), out long n) ? n : 0;
    public string Epoch => Meta("epoch") ?? "";
    public string Flag => Meta("flag") ?? "";
    public DateTime? LastSync => DateTime.TryParse(Meta("last"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
    public void Touch() => SetMeta("last", DateTime.Now.ToString("o"));
    public bool OwnedBy(string user, string server) => Meta("owner") == user + "|" + server;

    /// <summary>Takes the copy over for this person and site; anyone else's is thrown away first.</summary>
    public void Own(string user, string server)
    {
        lock (_lock) { if (OwnedBy(user, server)) return; Clear(); SetMeta("owner", user + "|" + server); }
    }

    public void Clear()
    {
        lock (_lock) { using var c = Cmd("DELETE FROM rec; DELETE FROM meta WHERE k <> 'key';", null); c.ExecuteNonQuery(); _mem.Clear(); }
    }

    private byte[] Seal(string json)
    {
        using var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(Encoding.UTF8.GetBytes(json));
        byte[] plain = ms.ToArray(), blob = new byte[12 + 16 + plain.Length];
        RandomNumberGenerator.Fill(blob.AsSpan(0, 12));
        _aes.Encrypt(blob.AsSpan(0, 12), plain, blob.AsSpan(28), blob.AsSpan(12, 16));
        return blob;
    }

    private string? Unseal(byte[] blob)
    {
        if (blob.Length < 28) return null;
        byte[] plain = new byte[blob.Length - 28];
        try
        {
            _aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain);
            using var gz = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress); using var r = new StreamReader(gz, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException) { return null; }
    }

    private Dictionary<long, Row> Load(string kind)
    {
        if (_mem.TryGetValue(kind, out var have)) return have;
        var all = new Dictionary<long, Row>();
        using var c = Cmd("SELECT id, blob FROM rec WHERE kind = $k", null, ("$k", kind));
        using var r = c.ExecuteReader();
        while (r.Read()) if (Unseal((byte[])r["blob"]) is { } j && JsonNode.Parse(j) is JsonObject o) all[r.GetInt64(0)] = new Row(o);
        return _mem[kind] = all;
    }

    private void Put(string kind, long id, string json, SqliteTransaction? tx)
    {
        using var c = Cmd("REPLACE INTO rec (kind, id, blob) VALUES ($k, $i, $b)", tx, ("$k", kind), ("$i", id), ("$b", Seal(json))); c.ExecuteNonQuery();
        if (_mem.TryGetValue(kind, out var m) && JsonNode.Parse(json) is JsonObject o) m[id] = new Row(o);
    }

    private void Delete(string kind, long id, SqliteTransaction? tx)
    {
        using var c = Cmd("DELETE FROM rec WHERE kind = $k AND id = $i", tx, ("$k", kind), ("$i", id)); c.ExecuteNonQuery();
        if (_mem.TryGetValue(kind, out var m)) m.Remove(id);
    }

    public int Count(string kind) { lock (_lock) { using var c = Cmd("SELECT COUNT(*) FROM rec WHERE kind = $k", null, ("$k", kind)); return Convert.ToInt32(c.ExecuteScalar()); } }

    /// <summary>One answer of staff/sync, written whole or not at all. Returns how many records it carried, and whether the site has more to give.</summary>
    public (int Received, bool More) Apply(JsonElement a)
    {
        lock (_lock)
        {
            int n = 0; bool more = a.GetProperty("more").GetBoolean();
            try
            {
                using var tx = _db.BeginTransaction();
                void Run(string sql, params (string, object)[] args) { using var c = Cmd(sql, tx, args); c.ExecuteNonQuery(); }
                if (a.GetProperty("reset").GetBoolean()) { Run("DELETE FROM rec; DELETE FROM meta WHERE k IN ('ready', 'seq', 'cursor');"); _mem.Clear(); }
                foreach (var k in a.GetProperty("drop").EnumerateArray()) { string kind = k.GetString() ?? ""; Run("DELETE FROM rec WHERE kind = $k", ("$k", kind)); _mem.Remove(kind); }
                // an empty set arrives as [] from some PHP answers: only an object has kinds
                if (a.GetProperty("up") is { ValueKind: JsonValueKind.Object } up)
                    foreach (var kind in up.EnumerateObject())
                        foreach (var doc in kind.Value.EnumerateArray()) if (doc.TryGetProperty("id", out var id) && id.TryGetInt64(out long i)) { Put(kind.Name, i, doc.GetRawText(), tx); n++; }
                if (a.GetProperty("del") is { ValueKind: JsonValueKind.Object } del)
                    foreach (var kind in del.EnumerateObject())
                        foreach (var id in kind.Value.EnumerateArray()) if (id.TryGetInt64(out long i)) Delete(kind.Name, i, tx);
                SetMeta("cursor", a.GetProperty("cursor").GetString() ?? "", tx);
                SetMeta("seq", a.GetProperty("seq").GetRawText(), tx);
                SetMeta("epoch", a.GetProperty("epoch").GetString() ?? "", tx);
                SetMeta("flag", a.TryGetProperty("flag", out var f) ? f.GetString() ?? "" : "", tx);
                SetMeta("kinds", a.GetProperty("kinds").GetRawText(), tx);
                SetMeta("known", "1", tx);
                if (!more) { SetMeta("ready", "1", tx); SetMeta("last", DateTime.Now.ToString("o"), tx); }
                tx.Commit();
            }
            catch { _mem.Clear(); throw; }   // what was half read into memory is read again from the file
            return (n, more);
        }
    }

    public bool Holds(string kind) => Meta("kinds") is { } k && k.Contains('"' + kind + '"', StringComparison.Ordinal);

    /* ---------- what the pages save ---------- */

    private static readonly Dictionary<string, string> Writes = new()
    {
        ["staff/customers"] = "customers", ["staff/products"] = "products", ["staff/builds"] = "builds", ["staff/builds/status"] = "builds", ["staff/jobs"] = "jobs", ["staff/jobs/status"] = "jobs",
        ["staff/purchases"] = "purchases", ["staff/invoices"] = "invoices", ["staff/invoices/finalize"] = "invoices", ["staff/invoices/void"] = "invoices", ["staff/invoices/copy"] = "invoices", ["staff/invoices/from-build"] = "invoices",
        ["staff/payments"] = "payments", ["staff/cheques"] = "cheques", ["staff/ships"] = "ships", ["staff/shortages"] = "shortages", ["staff/tasks"] = "tasks", ["staff/tasks/act"] = "tasks", ["staff/flows"] = "flows",
    };

    /// <summary>What the site answered to a save is in the copy at once (a page read right after it shows it); what follows from it comes with the next changes.</summary>
    public void Written(string path, JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } d) return;
        lock (_lock)
        {
            if (path is "staff/invoices/delete" or "staff/payments/delete" or "staff/flows/delete")
            {
                if (d.TryGetProperty("deleted", out var gone) && gone.TryGetInt64(out long g)) Delete(path.Split('/')[1], g, null);
                return;
            }
            if (!Writes.TryGetValue(path, out var kind) || !Holds(kind) || !d.TryGetProperty("id", out var id) || !id.TryGetInt64(out long n) || n <= 0) return;
            var doc = Load(kind).TryGetValue(n, out var old) ? (JsonObject)old.Doc.DeepClone() : [];
            foreach (var p in d.EnumerateObject()) doc[p.Name] = JsonNode.Parse(p.Value.GetRawText());   // a short answer (a status change) keeps the rest of the record
            doc.Remove("warnings"); doc.Remove("duplicate");
            Put(kind, n, doc.ToJsonString(), null);
        }
    }

    /// <summary>The kept record's own "updated" stamp, or null.</summary>
    public string? Stamp(string kind, long id) { lock (_lock) return Load(kind).TryGetValue(id, out var r) ? r.Doc["updated"]?.ToString() : null; }

    public void Dispose() { _db.Dispose(); _aes.Dispose(); SqliteConnection.ClearAllPools(); }
}
