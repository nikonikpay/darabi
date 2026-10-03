using System.IO; using System.Net; using System.Net.Http; using System.Reflection; using System.Text.Json; using System.Text.Json.Nodes;
using Mazesta.Desktop.Localization; using Mazesta.Desktop.Services; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Persistence.Updates; using Mazesta.Reporting;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// The link to the shop's site (its Mazesta Connect plugin): the key this copy holds and what the site said to it, a report's one-page summary
    /// sent to be kept and printed there, and this copy's benchmark runs sent to the site's bank, from which every copy's comparison lists are
    /// then built. Nothing is sent on its own: each send is a button. What was sent is remembered beside the data it came from (the report's
    /// folder, the run log's folder), so the page can say so and a run is not offered twice.
    /// </summary>
    private void RegisterSite()
    {
        var updater = AppUpdater.Get(_paths, _log); var site = updater.Site;
        var reports = _sp.GetRequiredService<ReportService>();
        string app = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        SiteStatus? status = null; string? error = null; bool busy = false; DateTimeOffset? checkedAt = null;
        string sentFile = Path.Combine(_paths.DataRoot, "benchmarks", "site-sent.json");

        HashSet<string> Sent()
        {
            try { return File.Exists(sentFile) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(sentFile)) ?? [] : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
        }
        // What went wrong, in the technician's words: the key, the plugin missing, or the line.
        string Say(Exception e) => e switch
        {
            SiteException { Status: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => Loc.Get("Site_Err_Key"),
            SiteException { Status: HttpStatusCode.NotFound } => Loc.Get("Site_Err_NoPlugin"),
            SiteException s => Loc.Format("Site_Err_Site", s.Message),
            _ => Loc.Format("Site_Err_Offline", e.Message),
        };
        static bool Expected(Exception e) => e is HttpRequestException or TaskCanceledException or SiteException or IOException or UnauthorizedAccessException;

        object State() => new
        {
            hasKey = _config.SiteKey.Length > 0, busy, error, api = AppUpdater.Api.Host,
            checkedAt = checkedAt?.ToString("yyyy/MM/dd HH:mm", Loc.Culture),
            status = status is null ? null : new { version = status.Version, key = status.Key, open = status.OpenUploads, reports = status.Reports, runs = status.Runs, pending = status.Pending },
            unsent = Unsent(),
        };
        int Unsent() { if (_benchRunLog is not { } log) return 0; var sent = Sent(); return log.All().Count(r => !sent.Contains(r.Id)); }
        async Task Check()
        {
            busy = true; error = null; PushSoon("site", State);
            try { status = await site.StatusAsync(_config.SiteKey, CancellationToken.None).ConfigureAwait(true); checkedAt = DateTimeOffset.Now; }
            catch (Exception e) when (Expected(e)) { status = null; error = Say(e); }
            finally { busy = false; PushSoon("site", State); }
        }

        Method("site.state", _ => State());
        MethodAsync("site.check", async _ => { await Check(); return State(); });
        MethodAsync("site.key", async p =>
        {
            _config.SiteKey = Str(p, "value").Trim(); _store.Save(_config);
            await Check();
            return State();
        });

        // A report's summary, as the summary button prints it, kept on the site under the report's own id (sent again, it replaces itself).
        MethodAsync("site.report", async p =>
        {
            if (_config.SiteKey.Length == 0) return new { error = Loc.Get("Site_Err_NoKey") };
            var stored = reports.Store.List().FirstOrDefault(r => r.Id == Str(p, "id")) ?? throw new ArgumentException("unknown report");
            try
            {
                var (report, html) = await Task.Run(() => reports.SummaryForSite(stored)).ConfigureAwait(true);
                string machine = string.Join(" · ", new[] { report.Machine.Cpu?.Name }.Concat(report.Machine.Gpus.Select(g => g.Name)).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => BenchmarkPeers.PartName(n)));
                string summary = report.Kind == ReportKind.Benchmark ? string.Join(" · ", (report.Benchmarks ?? []).Select(b => b.Name))
                    : Loc.Format("Reports_RowCounts", report.Counts.Total, report.Counts.Passed, report.Counts.Failed, report.Counts.Cancelled + report.Counts.Unsupported + report.Counts.NotRun);
                var receipt = await site.SendReportAsync(_config.SiteKey, new SiteReport(report.Id, Loc.Get("Site_Report_Title"), report.CreatedAt, report.Kind.ToString(), report.Verdict?.ToString(),
                    machine, report.ServiceNumber, summary, app, html), CancellationToken.None).ConfigureAwait(true);
                try { File.WriteAllText(Path.Combine(stored.Folder, SiteReceiptName), JsonSerializer.Serialize(new { url = receipt.Url, link = receipt.Link, at = DateTimeOffset.Now })); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // sent all the same; only the mark on the row is lost
                _log.LogInformation("Report {Id} sent to the site ({Url})", report.Id, receipt.Url);
                return new { ok = true, url = receipt.Url, link = receipt.Link, updated = receipt.Updated };
            }
            catch (Exception e) when (Expected(e) || e is JsonException) { _log.LogInformation("Report not sent: {Message}", e.Message); return new { error = Say(e) }; }
        });
        Method("site.open", p =>
        {
            // Only the shop's own pages are opened, whatever a receipt file says.
            string url = Str(p, "url");
            if (!ShopFeed.IsShopLink(url)) throw new ArgumentException("not the shop's site");
            Open(url); return null;
        });

        // This copy's runs the site has not had yet, in batches, with the shop's marks; then the lists the site built from them.
        MethodAsync("site.runs", async _ =>
        {
            if (_benchRunLog is not { } log) return new { error = Loc.Get("Site_Err_NoRuns") };
            var sent = Sent(); var todo = log.All().Where(r => !sent.Contains(r.Id)).ToList();
            var higher = todo.Select(r => r.Benchmark).Distinct().ToDictionary(b => b, b => BenchmarkRecords.Headline(b)?.HigherIsBetter ?? true);
            var marks = log.Marks.All();
            int added = 0, known = 0, rejected = 0; bool pending = false;
            busy = true; PushSoon("site", State);
            try
            {
                if (todo.Count == 0 && marks.Count == 0) return new { ok = true, added, known, rejected, pending, nothing = true };
                // With nothing new to send, one empty batch still carries the marks (a star given or taken back since the last upload).
                BenchmarkRun[][] batches = todo.Count == 0 ? [[]] : todo.Chunk(SiteClient.RunsPerRequest).ToArray();
                foreach (var batch in batches)
                {
                    var r = await site.SendRunsAsync(_config.SiteKey.Length > 0 ? _config.SiteKey : null, [.. batch.Select(x => JsonNode.Parse(BenchmarkPeers.WriteRun(x))!)], higher,
                        _config.SiteKey.Length > 0 && marks.Count > 0 ? JsonNode.Parse(BenchmarkPeers.WriteMarks(marks)) : null, CancellationToken.None).ConfigureAwait(true);
                    added += r.Added; known += r.Known; rejected += r.Rejected; pending |= r.Pending;
                    foreach (var x in batch) sent.Add(x.Id);
                    try { Directory.CreateDirectory(Path.GetDirectoryName(sentFile)!); File.WriteAllText(sentFile, JsonSerializer.Serialize(sent)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the site takes a run once anyway
                }
                _log.LogInformation("Benchmark runs sent to the site: {Added} new, {Known} known, {Rejected} refused", added, known, rejected);
                await updater.SyncSiteListsAsync().ConfigureAwait(true);
                return new { ok = true, added, known, rejected, pending, nothing = false };
            }
            catch (Exception e) when (Expected(e) || e is JsonException) { _log.LogInformation("Runs not sent: {Message}", e.Message); return new { error = Say(e) }; }
            finally { busy = false; PushSoon("site", State); }
        });
    }

    /// <summary>In a report's folder once its summary was sent: where it is on the site.</summary>
    internal const string SiteReceiptName = "site.json";
    private static string? SiteUrlOf(string reportFolder)
    {
        try
        {
            string file = Path.Combine(reportFolder, SiteReceiptName);
            return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file))?["url"]?.GetValue<string>() is { } url && ShopFeed.IsShopLink(url) ? url : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }
}
