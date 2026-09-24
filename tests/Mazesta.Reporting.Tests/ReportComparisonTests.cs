using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class ReportComparisonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static TestEntry Test(string id, ReportOutcome o) => new(id, id, o, T0, T0.AddMinutes(1), 60, 0, null, new Dictionary<string, string>());
    private static SensorSummary Sensor(string id, double min, double avg, double max) => new(id, "CPU", "Package", "Temperature", "°C", min, avg, max, 10, []);
    private static HardwareInventory Machine(string cpu = "i9-14900K", string board = "Z790", string[]? serials = null) =>
        HardwareInventory.Empty with { Cpu = new CpuInfo(cpu, HardwareVendor.Intel, 24, 32, 3200, null), Motherboard = new MotherboardInfo("ASUS", board, "1.0", null), Storage = (serials ?? ["S1"]).Select(s => new StorageDeviceInfo("Disk", s, "SSD", "NVMe", null, null, null)).ToList() };
    private static SessionReport Report(HardwareInventory machine, params TestEntry[] tests) => SessionReport.Create("x", "1", T0, tests, [], machine);
    private static SessionReport ReportWithSensors(HardwareInventory machine, params SensorSummary[] sensors) => SessionReport.Create("x", "1", T0, [], sensors, machine);

    private static SessionReport Bench(HardwareInventory machine, params (string Metric, double Value)[] metrics)
        => SessionReport.CreateBenchmark("x", "1", T0, [new BenchmarkEntry("bench.storage", "Storage", T0, [.. metrics.Select(m => new BenchmarkMetricEntry(m.Metric, m.Value, "MB/s"))], null, T0)], [], machine);

    [Fact] public void Benchmark_numbers_are_compared_by_benchmark_and_metric_and_a_missing_side_is_never_zero()
    {
        var cmp = ReportComparison.Compare(Bench(Machine(), ("Read", 2000), ("Write", 500)), Bench(Machine(), ("Read", 3400)));
        var read = cmp.Benchmarks.Single(b => b.Metric == "Read"); var write = cmp.Benchmarks.Single(b => b.Metric == "Write");
        Assert.Equal((2000, 3400, 1400), (read.Before, read.After, read.Delta));
        Assert.Equal(500, write.Before); Assert.Null(write.After); Assert.Null(write.Delta);
    }
    [Fact] public void The_comparison_page_shows_both_sides_the_change_and_what_was_not_measured()
    {
        var before = Bench(Machine(), ("Read", 2000), ("Write", 500)); var after = Bench(Machine(), ("Read", 3400));
        string html = ReportHtml.WriteComparison(before, after, ReportComparison.Compare(before, after), wording: ReportText.English);
        Assert.Contains("Before and after service", html); Assert.Contains("+1400 MB/s", html); Assert.Contains("(+70.0%)", html); Assert.Contains("not measured", html);
        Assert.DoesNotContain(">0 MB/s<", html);
    }

    [Fact] public void Same_machine_is_comparable()
    {
        var cmp = ReportComparison.Compare(Report(Machine(), Test("cpu", ReportOutcome.Failed)), Report(Machine(), Test("cpu", ReportOutcome.Passed)));
        Assert.True(cmp.IsComparable); Assert.Null(cmp.Reason);
        Assert.Equal((ReportOutcome.Failed, ReportOutcome.Passed), (cmp.Tests[0].Before, cmp.Tests[0].After));
    }

    [Fact] public void Different_cpu_is_not_comparable()
    {
        var cmp = ReportComparison.Compare(Report(Machine(cpu: "i9-14900K")), Report(Machine(cpu: "Ryzen 9 9900X")));
        Assert.False(cmp.IsComparable); Assert.NotNull(cmp.Reason); Assert.Empty(cmp.Tests); Assert.Empty(cmp.Sensors);
    }

    [Fact] public void Different_motherboard_is_not_comparable()
        => Assert.False(ReportComparison.Compare(Report(Machine(board: "Z790")), Report(Machine(board: "Z890"))).IsComparable);

    [Fact] public void Different_storage_serials_are_not_comparable()
        => Assert.False(ReportComparison.Compare(Report(Machine(serials: ["S1"])), Report(Machine(serials: ["S2"]))).IsComparable);

    [Fact] public void Unknown_cpu_on_either_side_is_not_comparable()
        => Assert.False(ReportComparison.Compare(Report(HardwareInventory.Empty), Report(Machine())).IsComparable);

    [Fact] public void A_test_present_on_only_one_side_shows_the_missing_side_as_null_not_a_fake_outcome()
    {
        var cmp = ReportComparison.Compare(Report(Machine(), Test("cpu", ReportOutcome.Passed), Test("ram", ReportOutcome.Passed)), Report(Machine(), Test("cpu", ReportOutcome.Passed)));
        var ram = cmp.Tests.Single(t => t.Id == "ram");
        Assert.Equal(ReportOutcome.Passed, ram.Before); Assert.Null(ram.After);
    }

    [Fact] public void A_sensor_missing_on_one_side_is_null_not_zero()
    {
        var cmp = ReportComparison.Compare(ReportWithSensors(Machine(), Sensor("cpu/temp", 40, 55, 70)), ReportWithSensors(Machine()));
        var s = cmp.Sensors.Single();
        Assert.Equal(40, s.MinBefore); Assert.Null(s.MinAfter); Assert.Null(s.MinDelta);
        Assert.NotEqual(0, s.MinBefore); // guards against a future refactor collapsing null to 0
    }

    [Fact] public void Sensor_delta_is_after_minus_before()
    {
        var cmp = ReportComparison.Compare(ReportWithSensors(Machine(), Sensor("cpu/temp", 40, 50, 60)), ReportWithSensors(Machine(), Sensor("cpu/temp", 45, 55, 65)));
        var s = cmp.Sensors.Single();
        Assert.Equal(5, s.MinDelta); Assert.Equal(5, s.AverageDelta); Assert.Equal(5, s.MaxDelta);
    }
}
