using System.Text.Json; using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

/// <summary>The checkup's findings in a saved report: shown most urgent first, with their words and numbers, and absent without a trace in reports
/// that have none (every report saved before the checkup existed).</summary>
public class ReportFindingsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static SessionReport Report(IReadOnlyList<FindingEntry>? findings) => SessionReport.CreateBenchmark("Mazesta", "1.0", T0,
        [new BenchmarkEntry("bench.cpu.multi", "CPU multi", T0, [new("GFLOPS", 412, "GFLOPS")], null, T0.AddMinutes(-1))], [], HardwareInventory.Empty) with { Findings = findings };
    private static readonly FindingEntry[] Two =
    [
        new("Good", "Power plan fine", "It lets the CPU boost.", null, null, [new("Maximum processor state", "100 %")]),
        new("Problem", "CPU held at its ceiling", "The clock fell while the temperature sat flat.", "Check the cooler.", "Ryzen 9 7950X", [new("Clock drop", "14.0 %")]),
    ];

    [Fact] public void Html_shows_the_findings_with_the_problem_first()
    {
        string html = ReportHtml.Write(Report(Two), wording: ReportText.English);
        Assert.Contains(ReportText.English.Checkup, html);
        Assert.True(html.IndexOf("CPU held at its ceiling", StringComparison.Ordinal) < html.IndexOf("Power plan fine", StringComparison.Ordinal));
        Assert.Contains("Check the cooler.", html); Assert.Contains("14.0 %", html); Assert.Contains("class=\"finding Problem\"", html);
    }

    [Fact] public void Plain_text_carries_the_level_and_the_numbers()
    {
        string text = ReportPlainText.Write(Report(Two), ReportText.Persian);
        Assert.Contains($"[{ReportText.Persian.LevelProblem}] CPU held at its ceiling · Ryzen 9 7950X", text); Assert.Contains("Clock drop: 14.0 %", text);
    }

    [Fact] public void A_report_without_findings_has_no_checkup_section() => Assert.DoesNotContain(ReportText.English.Checkup + "</h2>", ReportHtml.Write(Report(null), wording: ReportText.English));

    [Fact] public void Findings_survive_a_save_and_a_reload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mazesta-rep-" + Guid.NewGuid().ToString("N")); var store = new ReportStore(dir);
        try
        {
            var r = Report(Two);
            var saved = store.Save(r, ReportHtml.Write(r), ReportPlainText.Write(r));
            var back = store.Load(saved)!;
            Assert.Equal(JsonSerializer.Serialize(r.Findings), JsonSerializer.Serialize(back.Findings));
        }
        finally { Directory.Delete(dir, true); }
    }
}
