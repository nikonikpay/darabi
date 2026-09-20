using System.IO; using System.Reflection; using System.Windows;
using Mazesta.Core.Time; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Monitoring; using Mazesta.Persistence; using Mazesta.Reporting;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Services;

/// <summary>
/// Watches the (singleton) test engine and, when a queue finishes, writes its report: JSON (the data) and HTML (the
/// human report) into the reports folder. It records only what the engine reports and what the monitor measured in the
/// run's own time window; tests the queue asked for but that never ran are listed as not run.
/// </summary>
public sealed class ReportService
{
    private readonly PollingEngine _polling; private readonly InventoryCache _inventory; private readonly AppConfig _config; private readonly IClock _clock; private readonly ILogger _log;
    private readonly object _lock = new(); private IReadOnlyList<QueuedTest> _queue = []; private readonly Dictionary<TestId, TestRunResult> _results = [];
    private DateTimeOffset _sessionStart;
    public ReportStore Store { get; }
    public event Action<StoredReport>? ReportCreated;

    public ReportService(TestEngine engine, PollingEngine polling, InventoryCache inventory, AppConfig config, AppPaths paths, IClock clock, ILogger<ReportService> log)
    {
        _polling = polling; _inventory = inventory; _config = config; _clock = clock; _log = log; Store = new(paths.ReportsDir);
        engine.SessionStarted += q => { lock (_lock) { _queue = q; _results.Clear(); _sessionStart = _clock.UtcNow; } };
        engine.TestCompleted += (id, r) => { lock (_lock) _results[id] = r; };
        engine.StateChanged += s => { if (s == TestEngineState.Stopped) _ = Task.Run(CreateReportAsync); };
    }

    private async Task CreateReportAsync()
    {
        try
        {
            IReadOnlyList<QueuedTest> queue; Dictionary<TestId, TestRunResult> results; DateTimeOffset start;
            lock (_lock) { queue = _queue; results = new(_results); start = _sessionStart; }
            if (results.Count == 0) return;   // nothing ran (cancelled before the first test): there is nothing to report

            var tests = queue.Select(q => ToEntry(q, results.GetValueOrDefault(q.Definition.Id), start)).ToList();
            var machine = await _inventory.GetAsync().ConfigureAwait(false);
            var sensors = SensorSummarizer.Summarize(_polling, tests.Min(t => t.StartedAt), tests.Max(t => t.FinishedAt));
            var report = SessionReport.Create(_config.ShopName, Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "", _clock.UtcNow, tests, sensors, machine);
            var stored = Store.Save(report, ReportHtml.Write(report, LoadFont()));
            _log.LogInformation("Report saved: {Folder} ({Verdict})", stored.Folder, report.Verdict);
            ReportCreated?.Invoke(stored);
        }
        catch (Exception e) { _log.LogError(e, "Creating the test report failed"); }
    }

    private static TestEntry ToEntry(QueuedTest q, TestRunResult? r, DateTimeOffset sessionStart)
    {
        var options = (q.Options ?? new Dictionary<string, string>()).Where(o => !string.IsNullOrWhiteSpace(o.Value)).ToDictionary(o => o.Key, o => o.Value);
        string name = Loc.Get(q.Definition.NameKey);
        if (r is null) return new(q.Definition.Id.Value, name, ReportOutcome.NotRun, sessionStart, sessionStart, 0, 0, null, options);
        var finished = r.FinishedAt ?? r.StartedAt;
        return new(q.Definition.Id.Value, name, r.Outcome switch { TestOutcome.Passed => ReportOutcome.Passed, TestOutcome.Failed => ReportOutcome.Failed, TestOutcome.Cancelled => ReportOutcome.Cancelled, TestOutcome.Unsupported => ReportOutcome.Unsupported, _ => ReportOutcome.NotRun },
            r.StartedAt, finished, (finished - r.StartedAt).TotalSeconds, r.ErrorCount, r.Detail, options);
    }

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
