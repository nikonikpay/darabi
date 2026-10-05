using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Monitoring; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class ReportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static TestEntry Test(string name, ReportOutcome o, int startSec = 0, int lenSec = 60, long errors = 0, string? detail = null)
        => new(name.ToLowerInvariant(), name, o, T0.AddSeconds(startSec), T0.AddSeconds(startSec + lenSec), lenSec, errors, detail, new Dictionary<string, string>());
    private static SessionReport Report(params TestEntry[] tests) => SessionReport.Create("مازستا", "1.0", T0.AddMinutes(5), tests, [], HardwareInventory.Empty, "0123456789abcdef");

    [Fact] public void All_passed_is_a_pass() => Assert.Equal(ReportVerdict.Passed, Report(Test("a", ReportOutcome.Passed), Test("b", ReportOutcome.Passed)).Verdict);
    [Fact] public void Any_failure_fails_the_report() => Assert.Equal(ReportVerdict.Failed, Report(Test("a", ReportOutcome.Passed), Test("b", ReportOutcome.Failed)).Verdict);
    [Fact] public void A_skipped_test_is_never_presented_as_a_pass() { Assert.Equal(ReportVerdict.Incomplete, Report(Test("a", ReportOutcome.Passed), Test("b", ReportOutcome.Cancelled)).Verdict); Assert.Equal(ReportVerdict.Incomplete, Report(Test("a", ReportOutcome.Unsupported)).Verdict); }
    [Fact] public void An_empty_session_is_incomplete_not_passed() => Assert.Equal(ReportVerdict.Incomplete, Report().Verdict);
    [Fact] public void Counts_and_window_come_from_the_tests() { var r = Report(Test("a", ReportOutcome.Passed, 0, 30, 0), Test("b", ReportOutcome.Failed, 40, 20, 3)); Assert.Equal(new ReportCounts(2, 1, 1, 0, 0, 0, 3), r.Counts); Assert.Equal(60, r.DurationSeconds); }

    [Fact] public void Json_round_trips_and_keeps_persian_readable()
    {
        var r = Report(Test("تست پردازنده", ReportOutcome.Passed, detail: "avg 55.0 °C"));
        string json = ReportJson.Write(r);
        Assert.Contains("تست پردازنده", json); Assert.Contains("\"verdict\": \"passed\"", json);
        var back = ReportJson.Read(json)!; Assert.Equal(r.Id, back.Id); Assert.Equal("تست پردازنده", back.Tests[0].Name); Assert.Equal(ReportOutcome.Passed, back.Tests[0].Outcome);
    }

    [Fact] public void Html_is_self_contained_escaped_and_carries_the_verdict()
    {
        string html = ReportHtml.Write(Report(Test("<b>x</b>", ReportOutcome.Failed, detail: "a <script>alert(1)</script> & b")), new ReportFont("F", [1, 2, 3], [4, 5, 6]));
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("<b>x</b>", html); Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.DoesNotContain("http://", html); Assert.DoesNotContain("https://", html);
        Assert.Contains("dir=\"rtl\"", html); Assert.Contains("verdict Failed", html); Assert.Contains("data:font/ttf;base64,AQID", html);
    }

    [Fact] public void Html_draws_a_chart_for_a_traced_sensor()
    {
        var s = new SensorSummary("cpu/0#t", "CPU", "Package", "Temperature", "°C", 40, 55, 70, 3, [new(0, 40), new(30, 70), new(60, 55)]);
        var r = SessionReport.Create("x", "1", T0, [Test("a", ReportOutcome.Passed)], [s], HardwareInventory.Empty);
        string html = ReportHtml.Write(r); Assert.Contains("<polyline", html); Assert.Contains("70.0 °C", html);
    }

    [Fact] public void Benchmarks_are_listed_with_their_time_and_never_change_the_verdict()
    {
        var bm = new BenchmarkEntry("bench.cpu", "<CPU>", T0, [new("Single", 12.3456, "GFLOPS"), new("Multi", 250.4, "GFLOPS")], "matrix");
        var r = SessionReport.Create("x", "1", T0, [Test("a", ReportOutcome.Passed)], [], HardwareInventory.Empty, benchmarks: [bm]);
        string html = ReportHtml.Write(r);
        Assert.Equal(ReportVerdict.Passed, r.Verdict); Assert.Contains("&lt;CPU&gt;", html); Assert.Contains("12.35 GFLOPS", html); Assert.Contains("250 GFLOPS", html);
        Assert.Equal("Multi", ReportJson.Read(ReportJson.Write(r))!.Benchmarks![0].Metrics[1].Name);
        Assert.DoesNotContain("<h2>" + ReportText.Persian.Benchmarks, ReportHtml.Write(Report(Test("a", ReportOutcome.Passed))));
    }

    [Fact] public void A_report_of_benchmarks_alone_has_no_verdict_no_test_counts_and_spans_the_run()
    {
        var bm = new BenchmarkEntry("bench.gpu.ai", "AI", T0.AddMinutes(1), [new("FP16", 107.3, "TFLOPS")], "DirectML", T0);
        var r = SessionReport.CreateBenchmark("x", "1", T0.AddMinutes(2), [bm], [], HardwareInventory.Empty);
        string html = ReportHtml.Write(r);
        Assert.Equal(ReportKind.Benchmark, r.Kind); Assert.Null(r.Verdict); Assert.Equal(60, r.DurationSeconds);
        var read = ReportJson.Read(ReportJson.Write(r))!; Assert.Equal(ReportKind.Benchmark, read.Kind); Assert.Null(read.Verdict);
        Assert.Equal(ReportKind.TestSession, ReportJson.Read(ReportJson.Write(Report()).Replace("\"kind\"", "\"unused\""))!.Kind);   // reports saved before Kind existed
        Assert.Contains("verdict Benchmark", html); Assert.Contains("107 TFLOPS", html); Assert.DoesNotContain("class=\"cards\"", html); Assert.DoesNotContain(ReportText.Persian.Results, html);
    }

    [Fact] public void Store_saves_lists_newest_first_and_skips_a_damaged_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-rep-" + Guid.NewGuid().ToString("N")); var store = new ReportStore(dir);
        try
        {
            var older = SessionReport.Create("x", "1", T0, [Test("a", ReportOutcome.Passed)], [], HardwareInventory.Empty, "aaaaaaaa11111111");
            var newer = SessionReport.Create("x", "1", T0.AddHours(1), [Test("a", ReportOutcome.Failed)], [], HardwareInventory.Empty, "bbbbbbbb22222222");
            store.Save(older, "<html/>"); var saved = store.Save(newer, "<html/>");
            Directory.CreateDirectory(Path.Combine(dir, "broken")); File.WriteAllText(Path.Combine(dir, "broken", ReportStore.JsonName), "{ not json");
            var list = store.List(); Assert.Equal(["bbbbbbbb22222222", "aaaaaaaa11111111"], list.Select(x => x.Id));
            Assert.True(File.Exists(saved.HtmlPath)); Assert.Equal(ReportVerdict.Failed, store.Load(list[0])!.Verdict);
            store.Delete(list[0]); Assert.Single(store.List());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public void Summary_uses_only_samples_inside_the_window_and_downsamples_the_trace()
    {
        var def = new SensorDefinition(new SensorId("cpu/0#temp"), new HardwareId("cpu/0"), "Package", SensorKind.Temperature, Unit.Celsius, SensorRole.CpuPackageTemp, 0);
        int n = 500; var sec = Enumerable.Range(0, n).ToArray(); var val = Enumerable.Range(0, n).Select(i => (float)i).ToArray(); val[10] = float.NaN;
        var s = SensorSummarizer.Summarize(def, "CPU", new RawSeries(sec, val), 100, 199)!;
        Assert.Equal(100, s.Samples); Assert.Equal(100, s.Min); Assert.Equal(199, s.Max); Assert.Equal(149.5, s.Average);
        var big = SensorSummarizer.Summarize(def, "CPU", new RawSeries(sec, val), 0, 499)!; Assert.InRange(big.Trace.Count, 1, SensorSummarizer.MaxTracePoints);
        Assert.Null(SensorSummarizer.Summarize(def, "CPU", new RawSeries(sec, val), 1000, 2000));
    }

    [Fact] public void A_failed_test_s_advice_is_in_the_report_line_by_line_and_escaped()
    {
        var failed = Test("RAM", ReportOutcome.Failed) with { Advice = "Seen: <wrong> data\nNext: MemTest86" };
        string html = ReportHtml.Write(Report(failed));
        Assert.Contains("<div class=\"advice\">Seen: &lt;wrong&gt; data<br>Next: MemTest86</div>", html);
        Assert.Contains("Next: MemTest86", ReportPlainText.Write(Report(failed)));
    }

    [Fact] public void A_data_folder_the_app_folder_or_the_reports_folder_all_lead_to_the_reports_and_a_wrong_folder_to_none()
    {
        string root = Path.Combine(Path.GetTempPath(), "mazesta-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            string reports = Path.Combine(root, "app", "Data", "reports"); var store = new ReportStore(reports);
            store.Save(SessionReport.Create("x", "1", T0, [Test("a", ReportOutcome.Passed)], [], HardwareInventory.Empty, "aaaaaaaa11111111"), "<html/>");
            Directory.CreateDirectory(Path.Combine(root, "other"));
            Assert.Equal(reports, ReportStore.FindReportsDirectory(reports)); Assert.Equal(reports, ReportStore.FindReportsDirectory(Path.Combine(root, "app", "Data"))); Assert.Equal(reports, ReportStore.FindReportsDirectory(Path.Combine(root, "app")));
            Assert.Null(ReportStore.FindReportsDirectory(Path.Combine(root, "other"))); Assert.Null(ReportStore.FindReportsDirectory(Path.Combine(root, "missing"))); Assert.Null(ReportStore.FindReportsDirectory(""));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact] public void Notes_written_into_a_report_stay_in_its_folder_and_show_in_the_page_and_the_summary_and_drop_the_old_summary()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-rep-" + Guid.NewGuid().ToString("N")); var store = new ReportStore(dir);
        try
        {
            var report = SessionReport.Create("x", "1", T0, [Test("a", ReportOutcome.Passed)], [], HardwareInventory.Empty, "cccccccc33333333");
            var stored = store.Save(report, "<html/>"); store.SaveSummary(stored, "<old/>");
            var changed = report with { ServiceNumber = "1403", ServiceNotes = "Cleaned the fans <b>and</b> repasted" };
            var updated = store.Update(stored, changed, ReportHtml.Write(changed, null, ReportText.Persian), ReportPlainText.Write(changed, ReportText.Persian));
            Assert.Equal(stored.Folder, updated.Folder); Assert.Equal("1403", store.List().Single().ServiceNumber); Assert.False(File.Exists(stored.SummaryPath));
            Assert.Contains("Cleaned the fans &lt;b&gt;and&lt;/b&gt; repasted", File.ReadAllText(stored.HtmlPath)); Assert.Contains("Cleaned the fans", File.ReadAllText(stored.TextPath));
            Assert.Contains(ReportText.Persian.WorkDone, SummaryHtml.Write(ReportSummary.Of(changed), null, SummaryText.Persian, ReportText.Persian));
            Assert.DoesNotContain(ReportText.Persian.WorkDone, SummaryHtml.Write(ReportSummary.Of(report), null, SummaryText.Persian, ReportText.Persian));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact] public void A_laptop_is_named_by_its_model_and_marked_and_a_board_makers_filler_is_not_shown()
    {
        var laptop = HardwareInventory.Empty with { Computer = new ComputerInfo("LENOVO", "82JU", null, "S1", true) };
        Assert.Equal("LENOVO 82JU", ReportFormat.DeviceName(laptop)); Assert.Contains(ReportText.English.Laptop, ReportFormat.MachineRows(laptop, ReportText.English).First().Value);
        Assert.Null(ReportFormat.DeviceName(HardwareInventory.Empty with { Computer = new ComputerInfo("To Be Filled By O.E.M.", "To Be Filled By O.E.M.", null, null, false) }));
        Assert.Equal("LENOVO 82XB (Yoga Slim 7)", ReportFormat.DeviceName(HardwareInventory.Empty with { Computer = new ComputerInfo("LENOVO", "82XB", "Yoga Slim 7", null, true) }));
        Assert.Null(ReportFormat.DeviceName(HardwareInventory.Empty));
    }
}
