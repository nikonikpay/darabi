namespace Mazesta.Reporting;

/// <summary>A saved report as the list shows it. Benchmarks names the benchmark runs it holds (a benchmark report has no test counts to summarise).</summary>
public sealed record StoredReport(string Folder, string Id, DateTimeOffset CreatedAt, ReportKind Kind, ReportVerdict? Verdict, ReportCounts Counts, string ShopName, IReadOnlyList<string> Benchmarks)
{
    internal static StoredReport Of(string folder, SessionReport r) => new(folder, r.Id, r.CreatedAt, r.Kind, r.Verdict, r.Counts, r.ShopName, [.. (r.Benchmarks ?? []).Select(b => b.Name)]);
    public string JsonPath => Path.Combine(Folder, ReportStore.JsonName);
    public string HtmlPath => Path.Combine(Folder, ReportStore.HtmlName);
    public string PdfPath => Path.Combine(Folder, ReportStore.PdfName);
    public string TextPath => Path.Combine(Folder, ReportStore.TextName);
    public string SummaryPath => Path.Combine(Folder, ReportStore.SummaryName);
}

/// <summary>One folder per report under the reports directory: <c>report.json</c> (the source of truth), <c>report.html</c>, <c>report.txt</c> and, once exported, <c>report.pdf</c>.</summary>
public sealed class ReportStore(string directory)
{
    public const string JsonName = "report.json", HtmlName = "report.html", TextName = "report.txt", PdfName = "report.pdf", SummaryName = "summary.html";

    public StoredReport Save(SessionReport report, string html, string? text = null)
    {
        string folder = Path.Combine(directory, $"{report.CreatedAt.ToLocalTime():yyyyMMdd-HHmmss}-{report.Id[..8]}");
        Directory.CreateDirectory(folder);
        WriteAtomic(Path.Combine(folder, JsonName), ReportJson.Write(report)); WriteAtomic(Path.Combine(folder, HtmlName), html); if (text is not null) WriteAtomic(Path.Combine(folder, TextName), text);
        return StoredReport.Of(folder, report);
    }

    /// <summary>Newest first; a folder whose JSON is missing or unreadable is skipped rather than failing the list.</summary>
    public IReadOnlyList<StoredReport> List()
    {
        if (!Directory.Exists(directory)) return [];
        var list = new List<StoredReport>();
        foreach (var folder in Directory.EnumerateDirectories(directory))
        {
            try { if (File.Exists(Path.Combine(folder, JsonName)) && ReportJson.Read(File.ReadAllText(Path.Combine(folder, JsonName))) is { } r) list.Add(StoredReport.Of(folder, r)); }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        }
        return [.. list.OrderByDescending(r => r.CreatedAt)];
    }

    public SessionReport? Load(StoredReport stored) => ReportJson.Read(File.ReadAllText(stored.JsonPath));
    /// <summary>The report's text file, written from its JSON first when it has none (reports saved before plain text existed).</summary>
    public string EnsureText(StoredReport stored, ReportText wording)
    {
        if (!File.Exists(stored.TextPath) && Load(stored) is { } report) WriteAtomic(stored.TextPath, ReportPlainText.Write(report, wording));
        return stored.TextPath;
    }
    public void Delete(StoredReport stored) { if (Directory.Exists(stored.Folder)) Directory.Delete(stored.Folder, recursive: true); }

    /// <summary>A report's one-page summary lives in the report's own folder, rewritten each time (it is made from the report, which does not
    /// change). Returns the HTML file's path; the PDF is printed next to it.</summary>
    public string SaveSummary(StoredReport stored, string html)
    {
        WriteAtomic(stored.SummaryPath, html);
        return stored.SummaryPath;
    }

    /// <summary>The specifications of the machine, exported by hand: <c>specs/specs-{time}.html</c> and <c>.json</c> under the reports folder
    /// (a folder without report.json, so the report list skips it). Returns the HTML path; the PDF is printed next to it.</summary>
    public string SaveSpecs(DateTimeOffset createdAt, string html, string json)
    {
        string folder = Path.Combine(directory, "specs"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"specs-{createdAt.ToLocalTime():yyyyMMdd-HHmmss}.html");
        WriteAtomic(path, html); WriteAtomic(Path.ChangeExtension(path, ".json"), json);
        return path;
    }

    private static void WriteAtomic(string path, string content) { string tmp = path + ".tmp"; File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false)); File.Move(tmp, path, overwrite: true); }
}
