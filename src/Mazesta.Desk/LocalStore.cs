using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
namespace Mazesta.Desk;

/// <summary>
/// The program's own database on this PC (one SQLite file in Data, a library inside the exe: nothing to install). It holds the last good answer of each read (so a page opened before the line
/// dropped still opens) and a copy of the lists a desk needs without the site: the stock's products, the customers and the suppliers, from which a search is answered with no line at all.
/// Every row is sealed with the Windows user's key, like the queue, so the file alone shows nothing. A different person or site clears it (what they may see differs).
/// </summary>
internal sealed class LocalStore : IDisposable
{
    private const int MaxReads = 1500;
    private readonly SqliteConnection _db; private readonly object _lock = new();
    private readonly Dictionary<string, List<(string Name, string Hay, JsonNode Item)>> _mem = [];

    public LocalStore(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _db = Open(file);
    }

    private static SqliteConnection Open(string file)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var c = new SqliteConnection("Data Source=" + file); c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS reads (path TEXT PRIMARY KEY, blob BLOB NOT NULL, at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS docs (kind TEXT NOT NULL, id INTEGER NOT NULL, blob BLOB NOT NULL, PRIMARY KEY (kind, id));";
                cmd.ExecuteNonQuery(); return c;
            }
            catch (SqliteException) when (attempt == 0) { SqliteConnection.ClearAllPools(); try { File.Delete(file); } catch (IOException) { } }   // a damaged file is only a cache: start it again
        }
    }

    private static byte[] Seal(string s) => ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser);
    private static string? Unseal(byte[] b) { try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(b, null, DataProtectionScope.CurrentUser)); } catch (CryptographicException) { return null; } }

    /* ---------- the last good answer of each read ---------- */

    public void Put(string path, string json)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand(); c.CommandText = "REPLACE INTO reads (path, blob, at) VALUES ($p, $b, $a)";
            c.Parameters.AddWithValue("$p", path); c.Parameters.AddWithValue("$b", Seal(json)); c.Parameters.AddWithValue("$a", DateTime.Now.ToString("o")); c.ExecuteNonQuery();
            using var n = _db.CreateCommand(); n.CommandText = "DELETE FROM reads WHERE path IN (SELECT path FROM reads WHERE path NOT IN ('staff/me', 'staff/config') ORDER BY at LIMIT MAX(0, (SELECT COUNT(*) FROM reads) - " + MaxReads + "))"; n.ExecuteNonQuery();
        }
    }

    public (string Json, DateTime At)? Get(string path)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand(); c.CommandText = "SELECT blob, at FROM reads WHERE path = $p"; c.Parameters.AddWithValue("$p", path);
            using var r = c.ExecuteReader(); if (!r.Read()) return null;
            return Unseal((byte[])r["blob"]) is { } j && DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? (j, at) : null;
        }
    }

    public void Clear() { lock (_lock) { using var c = _db.CreateCommand(); c.CommandText = "DELETE FROM reads; DELETE FROM docs;"; c.ExecuteNonQuery(); _mem.Clear(); } }

    /* ---------- the lists kept for work without the site ---------- */

    /// <summary>The items of a list answer (each with an "id") kept for <paramref name="kind"/>, newer copies replacing older.</summary>
    public void Upsert(string kind, JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array) return;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object || !it.TryGetProperty("id", out var id) || !id.TryGetInt64(out long n)) continue;
                using var c = _db.CreateCommand(); c.Transaction = tx; c.CommandText = "REPLACE INTO docs (kind, id, blob) VALUES ($k, $i, $b)";
                c.Parameters.AddWithValue("$k", kind); c.Parameters.AddWithValue("$i", n); c.Parameters.AddWithValue("$b", Seal(it.GetRawText())); c.ExecuteNonQuery();
            }
            tx.Commit(); _mem.Remove(kind);
        }
    }

    public int Count(string kind) { lock (_lock) { using var c = _db.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM docs WHERE kind = $k"; c.Parameters.AddWithValue("$k", kind); return Convert.ToInt32(c.ExecuteScalar()); } }

    /// <summary>Digits to Latin, Arabic letter forms to Persian, lower case: what a person types matches what is stored however it was typed.</summary>
    public static string Norm(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (char ch in s) b.Append(ch switch { >= '۰' and <= '۹' => (char)('0' + ch - '۰'), >= '٠' and <= '٩' => (char)('0' + ch - '٠'), 'ي' => 'ی', 'ك' => 'ک', _ => char.ToLowerInvariant(ch) });
        return b.ToString();
    }

    private static void Flat(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: sb.Append(e.GetString()).Append(' '); break;
            case JsonValueKind.Number: sb.Append(e.GetRawText()).Append(' '); break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Flat(x, sb); break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Flat(p.Value, sb); break;
        }
    }

    private List<(string Name, string Hay, JsonNode Item)> Load(string kind)
    {
        if (_mem.TryGetValue(kind, out var have)) return have;
        var list = new List<(string, string, JsonNode)>();
        using var c = _db.CreateCommand(); c.CommandText = "SELECT blob FROM docs WHERE kind = $k"; c.Parameters.AddWithValue("$k", kind);
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            if (Unseal((byte[])r["blob"]) is not { } json) continue;
            using var d = JsonDocument.Parse(json); var sb = new StringBuilder(); Flat(d.RootElement, sb);
            list.Add((Norm(d.RootElement.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() ?? "" : ""), Norm(sb.ToString()), JsonNode.Parse(json)!));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return _mem[kind] = list;
    }

    /// <summary>A list answer built from the kept copy, as the site would answer it: every word typed must be in the item; page and per are kept; any other parameter that names a field of the item must equal it. Null when nothing is kept.</summary>
    public JsonElement? List(string kind, string query)
    {
        lock (_lock)
        {
            var all = Load(kind); if (all.Count == 0) return null;
            var qs = System.Web.HttpUtility.ParseQueryString(query);
            var words = Norm(qs["q"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var extra = qs.AllKeys.Where(k => k is not null and not ("q" or "page" or "per")).Select(k => (k!, qs[k!] ?? "")).Where(x => x.Item2.Length > 0).ToList();
            var hit = all.Where(x => words.All(w => x.Hay.Contains(w)) && extra.All(f => x.Item is JsonObject o && o.TryGetPropertyValue(f.Item1, out var v) && v is not null ? v.ToString() == f.Item2 : true)).Select(x => x.Item).ToList();
            int per = Math.Clamp(int.TryParse(qs["per"], out int p) ? p : 30, 1, 50), page = Math.Max(1, int.TryParse(qs["page"], out int g) ? g : 1);
            var items = new JsonArray(); foreach (var it in hit.Skip((page - 1) * per).Take(per)) items.Add(JsonNode.Parse(it.ToJsonString()));
            var o2 = new JsonObject { ["total"] = hit.Count, ["page"] = page, ["per"] = per, ["items"] = items, ["offline"] = true, ["offlineAt"] = "نسخهٔ ذخیره‌شده روی این کامپیوتر" };
            using var doc = JsonDocument.Parse(o2.ToJsonString()); return doc.RootElement.Clone();
        }
    }

    public void Dispose() { _db.Dispose(); SqliteConnection.ClearAllPools(); }   // the pool would keep the file open
}
