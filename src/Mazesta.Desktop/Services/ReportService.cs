using System.IO; using System.Reflection;
using Mazesta.Core.Time; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring; using Mazesta.Persistence; using Mazesta.Reporting;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Watches the (singleton) test engine and, when a queue finishes, writes its report: JSON (the data) and HTML (the
/// human report) into the reports folder. It records only what the engine reports and what the monitor measured in the
/// run's own time window; tests the queue asked for but that never ran are listed as not run. Every completed benchmark run is
/// saved too, as a report of its own (numbers and the sensors over its run, no verdict), so the result outlives the page.
/// </summary>
public sealed class ReportService
{
    private readonly PollingEngine _polling; private readonly InventoryCache _inventory; private readonly BenchmarkRunner _benchmarks; private readonly CheckupService _checkup; private readonly AppConfig _config; private readonly IClock _clock; private readonly ILogger _log;
    private readonly object _lock = new(); private IReadOnlyList<QueuedTest> _queue = []; private readonly Dictionary<TestId, TestRunResult> _results = [];
    private DateTimeOffset _sessionStart; private string _serviceNumber = "";   // the job the session was started for, even if the field changes meanwhile
    public ReportStore Store { get; }
    /// <summary>Every report is headed with the company's name (it was a setting, "shop name", before 0.8).</summary>
    private static string Brand => Loc.Get("Web_Company_Title");
    /// <summary>Where the PDF printer (WebView2) keeps its profile - inside the portable Data folder.</summary>
    public string BrowserDataDir { get; }
    public event Action<StoredReport>? ReportCreated;

    public ReportService(TestEngine engine, PollingEngine polling, InventoryCache inventory, BenchmarkRunner benchmarks, CheckupService checkup, AppConfig config, AppPaths paths, IClock clock, ILogger<ReportService> log)
    {
        _polling = polling; _inventory = inventory; _benchmarks = benchmarks; _checkup = checkup; _config = config; _clock = clock; _log = log; Store = new(paths.ReportsDir); BrowserDataDir = Path.Combine(paths.CacheDir, "report-browser");
        engine.SessionStarted += q => { lock (_lock) { _queue = q; _results.Clear(); _sessionStart = _clock.UtcNow; _serviceNumber = _config.ServiceNumber; } };
        engine.TestCompleted += (id, r) => { lock (_lock) _results[id] = r; };
        engine.StateChanged += s => { if (s == TestEngineState.Stopped) _ = Task.Run(CreateReportAsync); };
        // A run on its own gets its report; a queue gets one report of all its completed runs when it ends. A run that measured nothing has nothing to report.
        benchmarks.Finished += b => { if (b.Result.Status == BenchmarkStatus.Completed && !benchmarks.InQueue) _ = Task.Run(() => CreateBenchmarkReportAsync([b])); };
        benchmarks.QueueFinished += runs => { var done = runs.Where(r => r.Result.Status == BenchmarkStatus.Completed).ToList(); if (done.Count > 0) _ = Task.Run(() => CreateBenchmarkReportAsync(done)); };
    }

    private async Task CreateReportAsync()
    {
        try
        {
            IReadOnlyList<QueuedTest> queue; Dictionary<TestId, TestRunResult> results; DateTimeOffset start; string service;
            lock (_lock) { queue = _queue; results = new(_results); start = _sessionStart; service = _serviceNumber; }
            if (results.Count == 0) return;   // nothing ran (cancelled before the first test): there is nothing to report

            var tests = queue.Select(q => ToEntry(q, results.GetValueOrDefault(q.Definition.Id), start)).ToList();
            var machine = await _inventory.GetAsync().ConfigureAwait(false);
            var sensors = SensorSummarizer.Summarize(_polling, tests.Min(t => t.StartedAt), tests.Max(t => t.FinishedAt));
            var benchmarks = _benchmarks.Completed().Select(ToEntry).ToList();
            var peaks = SensorSummarizer.Peaks(_polling, sensors, tests.Where(t => t.Outcome != ReportOutcome.NotRun).Select(t => (t.StartedAt, t.FinishedAt)));
            var findings = await Setup().ConfigureAwait(false);
            findings.AddRange(_checkup.Runs().SelectMany(c => c.All).Select(CheckupText.Entry));
            Save(SessionReport.Create(Brand, AppVersion, _clock.UtcNow, tests, sensors, machine, benchmarks: benchmarks, serviceNumber: service) with { Peaks = peaks, Findings = findings.Count > 0 ? findings : null });
        }
        catch (Exception e) { _log.LogError(e, "Creating the test report failed"); }
    }

    private async Task CreateBenchmarkReportAsync(IReadOnlyList<RecordedBenchmark> runs)
    {
        try
        {
            var machine = await _inventory.GetAsync().ConfigureAwait(false);
            var sensors = SensorSummarizer.Summarize(_polling, runs.Min(r => r.Result.StartedAt), runs.Max(r => r.Result.FinishedAt));
            var peaks = SensorSummarizer.Peaks(_polling, sensors, runs.Select(r => (r.Result.StartedAt, r.Result.FinishedAt)));
            var findings = await Setup().ConfigureAwait(false);
            foreach (var run in runs) findings.AddRange((await _checkup.ForRunAsync(run).ConfigureAwait(false)).Select(CheckupText.Entry));
            Save(SessionReport.CreateBenchmark(Brand, AppVersion, _clock.UtcNow, [.. runs.Select(ToEntry)], sensors, machine, _config.ServiceNumber) with { Peaks = peaks, Findings = findings.Count > 0 ? findings : null });
        }
        catch (Exception e) { _log.LogError(e, "Saving the benchmark report failed"); }
    }

    /// <summary>Writes the one-page summary of a saved report (its verdict, each test's result and the highest temperatures measured while it
    /// ran) into the report's folder and returns its HTML path. It is made from the report alone, never from the machine as it is now.</summary>
    /// <summary>The report and its one-page summary as the shop's site keeps it: the same page as <see cref="CreateSummary"/>, without the
    /// embedded font (the site's readers have their own), so it stays a few kilobytes.</summary>
    public (SessionReport Report, string Html, string? Full) SummaryForSite(StoredReport stored)
    {
        var report = Store.Load(stored) ?? throw new IOException("The report could not be read.");
        string lang = Loc.IsRtl ? "fa" : "en";
        // The whole report goes with it so the site shows both; one too large for the site's limit is left out (the summary alone is sent).
        string full = ReportHtml.Write(report, null, ReportText.For(lang));
        return (report, SummaryHtml.Write(ReportSummary.Of(report), null, SummaryText.For(lang), ReportText.For(lang)), full.Length <= MaxFullForSite ? full : null);
    }
    /// <summary>The site keeps a whole report up to this many characters (its plugin's limit is 3 MB).</summary>
    private const int MaxFullForSite = 2_500_000;

    public string CreateSummary(StoredReport stored)
    {
        var report = Store.Load(stored) ?? throw new IOException("The report could not be read.");
        string lang = Loc.IsRtl ? "fa" : "en";
        return Store.SaveSummary(stored, SummaryHtml.Write(ReportSummary.Of(report), Font.Value, SummaryText.For(lang), ReportText.For(lang)));
    }

    /// <summary>Writes the machine's specifications as HTML and JSON (the whole inventory) and returns the HTML path; the JSON sits next to it.</summary>
    public async Task<string> SaveSpecsAsync()
    {
        var inv = await _inventory.GetAsync().ConfigureAwait(false);
        var now = _clock.UtcNow;
        var sections = ViewModels.SystemInfoViewModel.Describe(inv).Select(s => new SpecSection(s.Title, [.. s.Rows.Select(r => new SpecRow(r.Label, r.Value))])).ToList();
        string html = SpecSheet.WriteHtml(sections, inv.Errors, now, Brand, AppVersion, Loc.Get("System_Export_Title"), Loc.Get("System_Export_Footer"), Loc.IsRtl, Font.Value);
        return Store.SaveSpecs(now, html, SpecSheet.WriteJson(inv, now, Brand, AppVersion));
    }

    /// <summary>The setup's checkup for a report (memory, power plan, drive links); a report is written all the same when it cannot be read.</summary>
    private async Task<List<FindingEntry>> Setup()
    {
        try { return [.. (await _checkup.SetupAsync().ConfigureAwait(false)).Select(CheckupText.Entry)]; }
        catch (Exception e) { _log.LogWarning(e, "The setup's checkup failed; the report goes without it"); return []; }
    }

    private static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    private void Save(SessionReport report)
    {
        var wording = ReportText.For(Loc.IsRtl ? "fa" : "en");   // the report follows the app's language
        var stored = Store.Save(report, ReportHtml.Write(report, Font.Value, wording), ReportPlainText.Write(report, wording));
        _log.LogInformation("Report saved: {Folder} ({Verdict})", stored.Folder, report.Verdict);
        ReportCreated?.Invoke(stored);
    }

    /// <summary>Writes the before/after page of two saved reports into the later one's folder and returns its path, or the reason the two
    /// cannot be compared (different machines).</summary>
    public (string? Path, MachineMismatch? Refused) Compare(StoredReport before, StoredReport after)
    {
        SessionReport b = Store.Load(before) ?? throw new IOException("The earlier report could not be read."), a = Store.Load(after) ?? throw new IOException("The later report could not be read.");
        var comparison = ReportComparison.Compare(b, a);
        if (!comparison.IsComparable) return (null, comparison.Mismatch);
        string path = Path.Combine(after.Folder, $"comparison-{b.Id[..8]}.html");
        File.WriteAllText(path, ReportHtml.WriteComparison(b, a, comparison, Font.Value, ReportText.For(Loc.IsRtl ? "fa" : "en")), new System.Text.UTF8Encoding(false));
        return (path, null);
    }

    private static BenchmarkEntry ToEntry(RecordedBenchmark b)
        => new(b.Definition.Id.Value, Loc.Get(b.Definition.NameKey), b.Result.FinishedAt, [.. b.Result.Metrics.Select(m => new BenchmarkMetricEntry(Loc.Get(m.Key), m.Value, m.Unit, m.Key))], Detail(b.Result), b.Result.StartedAt);

    /// <summary>A run's detail for its report: how it was set up (length, workload version, options, resolution), then what the benchmark itself said.</summary>
    private static string? Detail(BenchmarkResult r)
    {
        string setup = string.Join(" · ", (r.Setup ?? []).Select(s => $"{Loc.Get(s.Key)}: {s.Value}"));
        return setup.Length == 0 ? r.Detail : string.IsNullOrWhiteSpace(r.Detail) ? setup : setup + "\n" + r.Detail;
    }

    private static TestEntry ToEntry(QueuedTest q, TestRunResult? r, DateTimeOffset sessionStart)
    {
        var options = (q.Options ?? new Dictionary<string, string>()).Where(o => !string.IsNullOrWhiteSpace(o.Value)).ToDictionary(o => o.Key, o => o.Value);
        string name = Loc.Get(q.Definition.NameKey);
        if (r is null) return new(q.Definition.Id.Value, name, ReportOutcome.NotRun, sessionStart, sessionStart, 0, 0, null, options);
        var finished = r.FinishedAt ?? r.StartedAt;
        return new(q.Definition.Id.Value, name, r.Outcome switch { TestOutcome.Passed => ReportOutcome.Passed, TestOutcome.Failed => ReportOutcome.Failed, TestOutcome.Cancelled => ReportOutcome.Cancelled, TestOutcome.Unsupported => ReportOutcome.Unsupported,
            TestOutcome.Error => ReportOutcome.Error, TestOutcome.Inconclusive => ReportOutcome.Inconclusive, _ => ReportOutcome.NotRun },
            r.StartedAt, finished, (finished - r.StartedAt).TotalSeconds, r.ErrorCount, r.Detail, options, ViewModels.TestAdvice.For(q.Definition.Id.Value, r.Outcome));
    }

    // Read once: a report is now saved after every benchmark run, not only after a test session.
    private static readonly Lazy<ReportFont?> Font = new(LoadFont);
    private static ReportFont? LoadFont()
    {
        try
        {
            byte[] Read(string file) { using var s = typeof(ReportService).Assembly.GetManifestResourceStream("Fonts." + file)!; using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
            return new("IRANSansXFaNum", Read("IRANSansXFaNum-Regular.ttf"), Read("IRANSansXFaNum-Bold.ttf"));
        }
        catch (Exception e) when (e is IOException or NullReferenceException or InvalidOperationException) { return null; }   // the report is still complete, just in the system font
    }
}
