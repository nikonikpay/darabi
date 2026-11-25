using System.Text.Json;
using System.Text.RegularExpressions;
namespace Mazesta.Desk;

/// <summary>
/// The daily backup of the site's CRM database on this PC: once a day (and on demand) the program asks the site for a dump (staff/backup/pull, for the manager and the accountant) and keeps the
/// file in the folder chosen on the backup page, newest <c>keep</c> files. The file name comes from the site, so it is checked against the one pattern before it touches the disk.
/// </summary>
internal sealed partial class PcBackup
{
    private readonly string _file; private readonly SiteSession _site; private string _dir = "", _last = "", _error = ""; private int _keep = 14; private DateTime _lastTry = DateTime.MinValue;

    public PcBackup(string dataDir, SiteSession site)
    {
        _file = Path.Combine(dataDir, "desk-backup.json"); _site = site;
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(_file)); var r = d.RootElement;
            if (r.TryGetProperty("dir", out var a)) _dir = a.GetString() ?? ""; if (r.TryGetProperty("last", out var b)) _last = b.GetString() ?? "";
            if (r.TryGetProperty("keep", out var c) && c.TryGetInt32(out int k)) _keep = Math.Clamp(k, 1, 365);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    [GeneratedRegex(@"^mazesta-crm-\d{8}-\d{6}\.sql\.gz$")] private static partial Regex NameOk();

    private void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file, JsonSerializer.Serialize(new { dir = _dir, keep = _keep, last = _last })); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    public object State()
    {
        var files = new List<object>();
        try { if (_dir.Length > 0 && Directory.Exists(_dir)) foreach (var f in new DirectoryInfo(_dir).GetFiles("mazesta-crm-*.sql.gz").OrderByDescending(x => x.Name).Take(30)) files.Add(new { name = f.Name, size = f.Length, at = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new { dir = _dir, keep = _keep, last = _last, error = _error, files };
    }

    /// <summary>'' when the folder is usable (made when missing), else why not.</summary>
    public string Set(string dir, int keep)
    {
        dir = dir.Trim().TrimEnd('\\', '/');
        if (dir.Length == 0 || !Path.IsPathFullyQualified(dir)) return "مسیر کامل پوشه را بنویسید (مثلاً D:\\Backups).";
        try
        {
            Directory.CreateDirectory(dir); string probe = Path.Combine(dir, ".write-test"); File.WriteAllText(probe, "x"); File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return "در این پوشه نمی‌شود نوشت: " + e.Message; }
        _dir = dir; _keep = Math.Clamp(keep, 1, 365); Save(); return "";
    }

    /// <summary>Once a day while signed in: due when a folder is set, today's file is not there yet, and the last try (a failure waits an hour) is not recent.</summary>
    public bool Due => _dir.Length > 0 && _site.SignedIn && _last != DateTime.Now.ToString("yyyy-MM-dd") && DateTime.Now - _lastTry > TimeSpan.FromHours(1);

    public async Task<string> RunAsync(CancellationToken ct)
    {
        if (_dir.Length == 0) return "پوشه را انتخاب کنید.";
        _lastTry = DateTime.Now;
        var r = await _site.CallAsync("POST", "staff/backup/pull", "{}", ct);
        if (r.Status != 200 || r.Data is not { } d) return _error = (r.Data is { } e && e.TryGetProperty("error", out var m) && m.GetString() is { } t ? t : r.Error) ?? "پشتیبان‌گیری از سایت ممکن نشد.";
        string? name = d.TryGetProperty("name", out var n) ? n.GetString() : null, b64 = d.TryGetProperty("data", out var x) ? x.GetString() : null;
        if (name is null || !NameOk().IsMatch(name) || string.IsNullOrEmpty(b64)) return _error = "پاسخ سایت برای پشتیبان درست نبود.";
        try
        {
            byte[] raw = Convert.FromBase64String(b64); string path = Path.Combine(_dir, name), tmp = path + ".part";
            Directory.CreateDirectory(_dir); await File.WriteAllBytesAsync(tmp, raw, ct); File.Move(tmp, path, overwrite: true);
            foreach (var old in new DirectoryInfo(_dir).GetFiles("mazesta-crm-*.sql.gz").OrderByDescending(f => f.Name).Skip(_keep)) old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return _error = "فایل در پوشه نوشته نشد: " + ex.Message; }
        _last = DateTime.Now.ToString("yyyy-MM-dd"); _error = ""; Save(); return "";
    }
}
