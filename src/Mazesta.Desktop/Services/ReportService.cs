using System.IO; using System.Reflection; using System.Windows;
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
    private readonly PollingEngine _polling; private readonly InventoryCache _inventory; private readonly BenchmarkRunner _benchmarks; private readonly AppConfig _config; private readonly IClock _clock; private readonly ILogger _log;
    private readonly object _lock = new(); private IReadOnlyList<QueuedTest> _queue = []; private readonly Dictionary<TestId, TestRunResult> _results = [];
    private DateTimeOffset _sessionStart; private string _serviceNumber = "";   // the job the session was started for, even if the field changes meanwhile
    public ReportStore Store { get; }
    /// <summary>Where the PDF printer (WebView2) keeps its profile - inside the portable Data folder.</summary>
    public string BrowserDataDir { get; }
    public event Action<StoredReport>? ReportCreated;

    public ReportService(TestEngine engine, PollingEngine polling, InventoryCache inventory, BenchmarkRunner benchmarks, AppConfig config, AppPaths paths, IClock clock, ILogger<ReportService> log)
    {
        _polling = polling; _inventory = inventory; _benchmarks = benchmarks; _config = config; _clock = clock; _log = log; Store = new(paths.ReportsDir); BrowserDataDir = Path.Combine(paths.CacheDir, "report-browser");
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
            Save(SessionReport.Create(_config.ShopName, AppVersion, _clock.UtcNow, tests, sensors, machine, benchmarks: _benchmarks.Completed().Select(ToEntry).ToList(), serviceNumber: service));
        }
        catch (Exception e) { _log.LogError(e, "Creating the test report failed"); }
    }

    private async Task CreateBenchmarkReportAsync(IReadOnlyList<RecordedBenchmark> runs)
    {
        try
        {
            var machine = await _inventory.GetAsync().ConfigureAwait(false);
            var sensors = SensorSummarizer.Summarize(_polling, runs.Min(r => r.Result.StartedAt), runs.Max(r => r.Result.FinishedAt));
            Save(SessionReport.CreateBenchmark(_config.ShopName, AppVersion, _clock.UtcNow, [.. runs.Select(ToEntry)], sensors, machine, _config.ServiceNumber));
        }
        catch (Exception e) { _log.LogError(e, "Saving the benchmark report failed"); }
    }

    /// <summary>Writes the customer summary (machine, drive health, temperatures, last test) as of now and returns its HTML path.</summary>
    public async Task<string> CreateSummaryAsync(Mazesta.Core.Providers.IDriveHealthProvider drives)
    {
        var machine = await _inventory.GetAsync().ConfigureAwait(false);
        var health = await Task.Run(drives.Read).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var last = Store.List().FirstOrDefault(r => r.Kind == ReportKind.TestSession);
        var summary = new CustomerSummary(now, _config.ShopName, string.IsNullOrWhiteSpace(_config.ServiceNumber) ? null : _config.ServiceNumber.Trim(), AppVersion, machine,
            CustomerSummaryBuilder.Drives(health, machine.Storage), CustomerSummaryBuilder.Temperatures(_polling, now), last?.Verdict, last?.CreatedAt);
        string lang = Loc.IsRtl ? "fa" : "en";
        return Store.SaveSummary(now, SummaryHtml.Write(summary, Font.Value, SummaryText.For(lang), ReportText.For(lang)));
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
        => new(b.Definition.Id.Value, Loc.Get(b.Definition.NameKey), b.Result.FinishedAt, [.. b.Result.Metrics.Select(m => new BenchmarkMetricEntry(Loc.Get(m.Key), m.Value, m.Unit))], b.Result.Detail, b.Result.StartedAt);

    private static TestEntry ToEntry(QueuedTest q, TestRunResult? r, DateTimeOffset sessionStart)
    {
        var options = (q.Options ?? new Dictionary<string, string>()).Where(o => !string.IsNullOrWhiteSpace(o.Value)).ToDictionary(o => o.Key, o => o.Value);
        string name = Loc.Get(q.Definition.NameKey);
        if (r is null) return new(q.Definition.Id.Value, name, ReportOutcome.NotRun, sessionStart, sessionStart, 0, 0, null, options);
        var finished = r.FinishedAt ?? r.StartedAt;
        return new(q.Definition.Id.Value, name, r.Outcome switch { TestOutcome.Passed => ReportOutcome.Passed, TestOutcome.Failed => ReportOutcome.Failed, TestOutcome.Cancelled => ReportOutcome.Cancelled, TestOutcome.Unsupported => ReportOutcome.Unsupported, _ => ReportOutcome.NotRun },
            r.StartedAt, finished, (finished - r.StartedAt).TotalSeconds, r.ErrorCount, r.Detail, options);
    }

    // Read once: a report is now saved after every benchmark run, not only after a test session.
    private static readonly Lazy<ReportFont?> Font = new(LoadFont);
    private static ReportFont? LoadFont()
    {
        try
        {
            byte[] Read(string file) { using var s = Application.GetResourceStream(new Uri($"pack://application:,,,/Fonts/{file}"))!.Stream; using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
            return new("IRANSansXFaNum", Read("IRANSansXFaNum-Regular.ttf"), Read("IRANSansXFaNum-Bold.ttf"));
        }
        catch (Exception e) when (e is IOException or NullReferenceException or InvalidOperationException) { return null; }   // the report is still complete, just in the system font
    }
}
