using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class ReportSummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly StorageDeviceInfo Nvme = new("MSI M390 1TB", "S1", "SSD", "NVMe", 1_000_204_886_016, "EDFM00.1", "Healthy", 3);
    private static readonly StorageDeviceInfo Hdd = new("WDC WD20PURZ", "W2", "HDD", "SATA", 2_000_398_934_016, "01.01A01", "Healthy");
    private static TestEntry Test(string name, ReportOutcome o, int fromMin, int toMin) => new(name, name, o, T0.AddMinutes(fromMin), T0.AddMinutes(toMin), (toMin - fromMin) * 60, 0, null, new Dictionary<string, string>());
    // A trace sample every minute from T0: the CPU peaks in the first test, the GPU at 71 in the second.
    private static SensorSummary Sensor(string id, string hw, string name, string? role, params double[] perMinute)
        => new(id, hw, name, "Temperature", "°C", perMinute.Min(), perMinute.Average(), perMinute.Max(), perMinute.Length, [.. perMinute.Select((v, i) => new TracePoint(i * 60, v))], role);
    private static SessionReport Report(IReadOnlyList<WindowPeak>? peaks = null) => SessionReport.Create("مازستا", "1.0.0", T0.AddMinutes(20),
        [Test("CPU stress", ReportOutcome.Passed, 0, 5), Test("GPU 3-D scene", ReportOutcome.Failed, 6, 10), Test("Memory", ReportOutcome.NotRun, 10, 10)],
        [Sensor("cpu", "Ryzen 9 3950X", "Core (Tctl/Tdie)", "CpuTctlTdie", 60, 70, 88, 80, 74, 60, 55, 62, 64, 66, 60),
         Sensor("pkg", "Ryzen 9 3950X", "CPU Package", "CpuPackageTemp", 58, 68, 86, 78, 72, 58, 53, 60, 62, 64, 58),
         Sensor("gpu", "GTX 950", "GPU Core", null, 40, 41, 42, 43, 44, 45, 50, 60, 71, 65, 50),
         Sensor("load", "GTX 950", "GPU Core", null, 1, 2) with { Kind = "Load" }],
        HardwareInventory.Empty with { Storage = [Nvme, Hdd] }, serviceNumber: "S-1405-0042") with { Peaks = peaks };

    [Fact] public void Each_test_shows_the_highest_temperature_reached_while_it_ran_from_the_trace_of_an_older_report()
    {
        var s = ReportSummary.Of(Report());
        Assert.True(s.FromTrace);
        Assert.Equal(["CPU stress", "GPU 3-D scene"], s.Rows.Select(r => r.Name));   // a test that never ran has no temperatures to show
        Assert.Equal(86, s.Rows[0].Peaks[HeatPart.Cpu]);   // the package sensor, not Tctl/Tdie beside it
        Assert.Equal(71, s.Rows[1].Peaks[HeatPart.Gpu]); Assert.Equal(45, s.Rows[0].Peaks[HeatPart.Gpu]);
        var gpu = s.Peaks.Single(p => p.Part == HeatPart.Gpu);
        Assert.Equal((71.0, "GPU 3-D scene", "GTX 950"), (gpu.MaxC, gpu.During, gpu.Device));
        Assert.Equal([HeatPart.Cpu, HeatPart.Gpu], s.Columns);
    }

    [Fact] public void Recorded_peaks_are_used_over_the_trace_when_the_report_has_them()
    {
        var s = ReportSummary.Of(Report([new(T0, T0.AddMinutes(5), "pkg", 91.5)]));
        Assert.False(s.FromTrace); Assert.Equal(91.5, s.Rows[0].Peaks[HeatPart.Cpu]);
        Assert.False(s.Rows[1].Peaks.ContainsKey(HeatPart.Cpu));   // no recorded peak in that window: absent, never 0 or a guess
    }

    [Fact] public void The_sheet_is_a5_right_to_left_with_the_verdict_peaks_and_drive_health_percent()
    {
        string html = SummaryHtml.Write(ReportSummary.Of(Report()));
        Assert.Contains("dir=\"rtl\"", html); Assert.Contains("size:A5", html);
        Assert.Contains(ReportText.Persian.VerdictFailed, html); Assert.Contains("86 °C", html); Assert.Contains("71 °C", html);
        Assert.Contains("سالم (97%)", html); Assert.Contains(">سالم<", html);   // an HDD has no wear counter: no percentage invented
        Assert.Contains("S-1405-0042", html); Assert.Contains(SummaryText.Persian.TraceNote, html);
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("http", html.Replace("http-equiv", ""));
    }

    [Fact] public void A_report_without_temperatures_says_so() =>
        Assert.Contains(SummaryText.Persian.NoTemps, SummaryHtml.Write(ReportSummary.Of(SessionReport.Create("x", "1", T0, [], [], HardwareInventory.Empty))));

    [Fact] public void English_wording_is_left_to_right() => Assert.Contains("dir=\"ltr\"", SummaryHtml.Write(ReportSummary.Of(Report()), wording: SummaryText.English));
}
