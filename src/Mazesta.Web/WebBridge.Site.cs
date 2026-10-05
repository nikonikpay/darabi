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
        string? pairCode = null; CancellationTokenSource? pairing = null;
        // A value in the key's place that is not the site's key is dropped, not kept on disk and never sent.
        // The key is Mazesta's own: a users' copy neither keeps nor sends one (a settings file carried over from a company copy may hold it).
        if (!Staff && _config.SiteKey.Length > 0) { _config.SiteKey = ""; _store.Save(_config); }
        if (_config.SiteKey.Length > 0 && !SiteClient.IsKey(_config.SiteKey)) { _config.SiteKey = ""; _store.Save(_config); _log.LogWarning("The stored site key was not a site key; it was removed"); }
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
            hasKey = _config.SiteKey.Length > 0, busy, error, api = AppUpdater.Api.Host, pairCode,
            checkedAt = checkedAt?.ToString("yyyy/MM/dd HH:mm", Loc.Culture),
            status = status is null ? null : new { version = status.Version, key = status.Key, open = status.OpenUploads, sharing = status.Sharing, reports = status.Reports, runs = status.Runs, pending = status.Pending },
            unsent = Unsent(), shareLink = ShareLink(),
        };
        // The page the site made of this computer's results, once they were shared (the site keeps one page a computer, under one link).
        string shareFile = Path.Combine(_paths.DataRoot, "benchmarks", "site-share.json");
        string? ShareLink()
        {
            try { return File.Exists(shareFile) && JsonNode.Parse(File.ReadAllText(shareFile))?["link"]?.GetValue<string>() is { } link && ShopFeed.IsShopLink(link) ? link : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
        }
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
            StaffOnly();
            string typed = Str(p, "value").Trim();
            // Only the site's own key is kept or sent: another secret pasted here by mistake (it happened with the update-signing key) goes nowhere.
            if (typed.Length > 0 && (typed = SiteClient.FindKey(typed) ?? "").Length == 0) { error = Loc.Get("Site_Err_NotAKey"); return State(); }
            _config.SiteKey = typed; _store.Save(_config);
            await Check();
            return State();
        });

        // Connecting without typing the key: the site's dashboard opens in the browser, a manager signed in there approves this copy (both sides
        // show the same short code), and the key comes back to the app by itself.
        async Task Pair(string secret, int seconds, CancellationToken ct)
        {
            try
            {
                var until = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 60, 900));
                while (DateTimeOffset.UtcNow < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(true);
                    var claim = await site.PairClaimAsync(secret, ct).ConfigureAwait(true);
                    if (claim.State == "waiting") continue;
                    if (claim.State == "ok" && SiteClient.IsKey(claim.Key)) { _config.SiteKey = claim.Key!; _store.Save(_config); pairCode = null; pairing = null; await Check(); return; }
                    break;
                }
                error = Loc.Get("Site_Pair_Expired");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (Expected(e) || e is JsonException) { error = Say(e); }
            pairCode = null; pairing = null; PushSoon("site", State);
        }
        MethodAsync("site.pair", async p =>
        {
            StaffOnly();
            pairing?.Cancel(); pairing = null; pairCode = null; error = null;
            if (Str(p, "cmd") == "cancel") return State();
            try
            {
                string secret = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                var start = await site.PairStartAsync(secret, Environment.MachineName, CancellationToken.None).ConfigureAwait(true);
                if (!ShopFeed.IsShopLink(start.Url)) { error = Loc.Format("Site_Err_Site", "link"); return State(); }
                pairCode = start.Code; pairing = new CancellationTokenSource();
                Open(start.Url);
                _ = Pair(secret, start.Seconds, pairing.Token);
            }
            catch (Exception e) when (Expected(e) || e is JsonException) { error = Say(e); }
            return State();
        });

        // A report's summary, as the summary button prints it, kept on the site under the report's own id (sent again, it replaces itself).
        MethodAsync("site.report", async p =>
        {
            StaffOnly();
            if (_config.SiteKey.Length == 0) return new { error = Loc.Get("Site_Err_NoKey") };
            var stored = reports.Store.List().FirstOrDefault(r => r.Id == Str(p, "id")) ?? throw new ArgumentException("unknown report");
            try
            {
                var (report, html, full) = await Task.Run(() => reports.SummaryForSite(stored)).ConfigureAwait(true);
                // The company's copy sends only reports that carry a service job: the site's list is by service number.
                if (string.IsNullOrWhiteSpace(report.ServiceNumber)) return new { error = Loc.Get("Site_Err_NoService") };
                string machine = string.Join(" · ", new[] { report.Machine.Cpu?.Name }.Concat(report.Machine.Gpus.Select(g => g.Name)).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => BenchmarkPeers.PartName(n)));
                string summary = report.Kind == ReportKind.Benchmark ? string.Join(" · ", (report.Benchmarks ?? []).Select(b => b.Name))
                    : Loc.Format("Reports_RowCounts", report.Counts.Total, report.Counts.Passed, report.Counts.Failed, report.Counts.Cancelled + report.Counts.Unsupported + report.Counts.NotRun);
                var receipt = await site.SendReportAsync(_config.SiteKey, new SiteReport(report.Id, Loc.Get("Site_Report_Title"), report.CreatedAt, report.Kind.ToString(), report.Verdict?.ToString(),
                    machine, report.ServiceNumber, summary, app, html, full, Reporting.DeviceLabel.Of(report.Machine), report.Machine.Computer?.IsPortable, report.ServiceNotes), CancellationToken.None).ConfigureAwait(true);
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

        // Anyone's latest results, to pass on: this computer's newest run of each benchmark (as it is set up now), with the parts' names. No key:
        // it is the user's own to share. The computer's name is not sent.
        MethodAsync("site.share", async _ =>
        {
            if (_benchRunLog is not { } log || _benchSystemHash?.Invoke() is not { } hash) return new { error = Loc.Get("Site_Err_NoRuns") };
            var latest = log.Recent(hash, 2000).Where(r => BenchmarkRecords.Headline(r.Benchmark)?.Version is not { } v || v == r.Version)
                .GroupBy(r => r.Table).Select(g => g.First()).OrderBy(r => r.Benchmark, StringComparer.Ordinal).Take(SiteClient.RunsPerShare).ToList();
            if (latest.Count == 0) return new { error = Loc.Get("Site_Share_None") };
            var names = latest.Select(r => r.Benchmark).Distinct().ToDictionary(b => b, b => _benchVm?.Rows.FirstOrDefault(x => x.Benchmark.Definition.Id.Value == b)?.Name ?? b);
            var higher = names.Keys.ToDictionary(b => b, b => BenchmarkRecords.Headline(b)?.HigherIsBetter ?? true);
            busy = true; PushSoon("site", State);
            try
            {
                var inv = await _sp.GetRequiredService<Desktop.Composition.InventoryCache>().GetAsync().ConfigureAwait(true);
                static string? Name(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : BenchmarkPeers.PartName(raw);
                var machine = new SiteMachine(Name(inv.Cpu?.Name), Name(inv.Gpus.FirstOrDefault()?.Name), inv.TotalPhysicalMemoryBytes is { } b ? Math.Round(b / 1073741824.0) : null,
                    inv.Os is { } os ? $"{os.Caption} {os.Version}".Trim() : null);
                // Under the name the user chose in the settings; without one, the one name every unnamed user gets (the company's copies: its own).
                string owner = Staff ? Loc.Get("Web_Company_Title") : _config.DisplayName.Trim() is { Length: > 0 } chosen ? chosen : Loc.Get("Site_Share_Anonymous");
                var receipt = await site.ShareAsync([.. latest.Select(r => JsonNode.Parse(BenchmarkPeers.WriteRun(r with { Machine = "" }))!)], names, higher, machine with { Name = owner }, app, CancellationToken.None).ConfigureAwait(true);
                if (!ShopFeed.IsShopLink(receipt.Link)) return new { error = Loc.Format("Site_Err_Site", "link") };
                try { Directory.CreateDirectory(Path.GetDirectoryName(shareFile)!); File.WriteAllText(shareFile, JsonSerializer.Serialize(new { link = receipt.Link, at = DateTimeOffset.Now })); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // shared all the same; the link is in the answer
                _log.LogInformation("Shared {Count} benchmark results: {Link}", receipt.Rows, receipt.Link);
                return new { ok = true, link = receipt.Link, rows = receipt.Rows };
            }
            catch (Exception e) when (Expected(e) || e is JsonException) { _log.LogInformation("Results not shared: {Message}", e.Message); return new { error = Say(e) }; }
            finally { busy = false; PushSoon("site", State); }
        });

        // This copy's runs the site has not had yet, in batches, with the shop's marks; then the lists the site built from them.
        MethodAsync("site.runs", async _ =>
        {
            StaffOnly();
            if (_benchRunLog is not { } log) return new { error = Loc.Get("Site_Err_NoRuns") };
            var sent = Sent(); var todo = log.All().Where(r => !sent.Contains(r.Id)).ToList();
            var higher = todo.Select(r => r.Benchmark).Distinct().ToDictionary(b => b, b => BenchmarkRecords.Headline(b)?.HigherIsBetter ?? true);
            // The benchmarks' names as the app shows them, so the site's own pages name a list in words and not by its id.
            var names = (_benchVm?.Rows ?? []).ToDictionary(x => x.Benchmark.Definition.Id.Value, x => x.Name);
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
                        _config.SiteKey.Length > 0 && marks.Count > 0 ? JsonNode.Parse(BenchmarkPeers.WriteMarks(marks)) : null, CancellationToken.None, names).ConfigureAwait(true);
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
