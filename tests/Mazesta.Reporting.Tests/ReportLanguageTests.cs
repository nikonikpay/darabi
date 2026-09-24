using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

/// <summary>T2 and spec 7.4: the report in the app's language (Persian by default, English left-to-right) and as plain text.</summary>
public class ReportLanguageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static SessionReport Report() => SessionReport.Create("Mazesta", "1.0", T0.AddMinutes(5),
        [new("cpu", "CPU load", ReportOutcome.Failed, T0, T0.AddMinutes(1), 60, 3, "3 wrong results", new Dictionary<string, string> { ["threads"] = "16" })], [],
        HardwareInventory.Empty with { Cpu = new CpuInfo("Ryzen 9 3950X", Mazesta.Core.Hardware.HardwareVendor.Amd, 16, 32, 4700, "AM4") }, "0123456789abcdef");

    [Fact] public void Persian_is_the_default_and_right_to_left()
    {
        string html = ReportHtml.Write(Report());
        Assert.Contains("lang=\"fa\" dir=\"rtl\"", html); Assert.Contains(ReportText.Persian.VerdictFailed, html);
    }
    [Fact] public void English_is_left_to_right_with_english_wording_and_the_same_numbers()
    {
        string html = ReportHtml.Write(Report(), wording: ReportText.English);
        Assert.Contains("lang=\"en\" dir=\"ltr\"", html); Assert.Contains("At least one test failed", html); Assert.Contains("System specification", html);
        Assert.DoesNotContain(ReportText.Persian.Machine, html); Assert.Contains("Ryzen 9 3950X", html);
        Assert.Same(ReportText.English, ReportText.For("en")); Assert.Same(ReportText.Persian, ReportText.For("fa"));
    }
    [Fact] public void Plain_text_carries_the_verdict_tests_evidence_and_machine()
    {
        string text = ReportPlainText.Write(Report(), ReportText.English);
        foreach (var part in new[] { "System test and inspection report", "At least one test failed", "* CPU load: Failed · 1m 0s · Errors: 3", "threads=16", "3 wrong results", "* Processor: Ryzen 9 3950X · 16C/32T · 4.70 GHz", "Report ID: 0123456789abcdef" })
            Assert.Contains(part, text);
    }
    [Fact] public void A_benchmark_report_in_text_says_it_has_no_verdict()
    {
        var bm = new BenchmarkEntry("bench.storage", "Storage", T0.AddMinutes(1), [new("Sequential read", 3456, "MB/s")], null, T0);
        string text = ReportPlainText.Write(SessionReport.CreateBenchmark("Mazesta", "1.0", T0.AddMinutes(2), [bm], [], HardwareInventory.Empty), ReportText.English);
        Assert.Contains(ReportText.English.MeasurementsOnly, text); Assert.Contains("Sequential read: 3456 MB/s", text); Assert.DoesNotContain("Overall result", text);
    }
    [Fact] public void The_store_saves_the_text_and_writes_it_for_older_reports_that_have_none()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-txt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ReportStore(dir);
            var saved = store.Save(Report(), "<html/>", "text"); Assert.Equal("text", File.ReadAllText(saved.TextPath));
            File.Delete(saved.TextPath);
            Assert.Contains("CPU load", File.ReadAllText(store.EnsureText(saved, ReportText.English)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
