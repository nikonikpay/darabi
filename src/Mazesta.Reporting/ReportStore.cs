namespace Mazesta.Reporting;

public sealed record StoredReport(string Folder, string Id, DateTimeOffset CreatedAt, ReportVerdict Verdict, ReportCounts Counts, string ShopName)
{
    public string JsonPath => Path.Combine(Folder, ReportStore.JsonName);
    public string HtmlPath => Path.Combine(Folder, ReportStore.HtmlName);
    public string PdfPath => Path.Combine(Folder, ReportStore.PdfName);
}

/// <summary>One folder per report under the reports directory: <c>report.json</c> (the source of truth), <c>report.html</c> and, once exported, <c>report.pdf</c>.</summary>
public sealed class ReportStore(string directory)
{
    public const string JsonName = "report.json", HtmlName = "report.html", PdfName = "report.pdf";

    public StoredReport Save(SessionReport report, string html)
    {
        string folder = Path.Combine(directory, $"{report.CreatedAt.ToLocalTime():yyyyMMdd-HHmmss}-{report.Id[..8]}");
        Directory.CreateDirectory(folder);
        WriteAtomic(Path.Combine(folder, JsonName), ReportJson.Write(report)); WriteAtomic(Path.Combine(folder, HtmlName), html);
        return new(folder, report.Id, report.CreatedAt, report.Verdict, report.Counts, report.ShopName);
    }

    /// <summary>Newest first; a folder whose JSON is missing or unreadable is skipped rather than failing the list.</summary>
    public IReadOnlyList<StoredReport> List()
    {
        if (!Directory.Exists(directory)) return [];
        var list = new List<StoredReport>();
        foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            try { if (File.Exists(Path.Combine(folder, JsonName)) && ReportJson.Read(File.ReadAllText(Path.Combine(folder, JsonName))) is { } r) list.Add(new(folder, r.Id, r.CreatedAt, r.Verdict, r.Counts, r.ShopName)); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        return [.. list.OrderByDescending(r => r.CreatedAt)];
    }

    public SessionReport? Load(StoredReport stored) => ReportJson.Read(File.ReadAllText(stored.JsonPath));
    public void Delete(StoredReport stored) { if (Directory.Exists(stored.Folder)) Directory.Delete(stored.Folder, recursive: true); }

    private static void WriteAtomic(string path, string content) { string tmp = path + ".tmp"; File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false)); File.Move(tmp, path, overwrite: true); }
}
