using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public sealed class PartHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static TestEntry Test(string id, ReportOutcome o) => new(id, id + " name", o, T0, T0.AddMinutes(2), 120, 0, null, new Dictionary<string, string>());
    private static BenchmarkEntry Bench(string id, params (string N, double V, string U, string K)[] m)
        => new(id, id + " name", T0.AddMinutes(3), [.. m.Select(x => new BenchmarkMetricEntry(x.N, x.V, x.U, x.K))], null, T0.AddMinutes(2));
    private static SessionReport Report(IEnumerable<TestEntry> tests, IEnumerable<BenchmarkEntry>? benchmarks = null)
        => SessionReport.Create("x", "1", T0.AddMinutes(10), [.. tests], [], HardwareInventory.Empty, benchmarks: benchmarks is null ? null : [.. benchmarks]);

    [Fact] public void A_part_gets_its_own_tests_and_benchmark_figures_and_no_other_parts()
    {
        var r = Report([Test("memory.pattern", ReportOutcome.Passed), Test("cpu.matrix", ReportOutcome.Failed)],
            [Bench("bench.memory", ("Write", 10, "GB/s", "Bench_Mem_Write"), ("Read", 13, "GB/s", "Bench_Mem_Read"), ("Latency", 119, "ns", "Bench_Mem_Latency"), ("Copy", 12, "GB/s", "Bench_Mem_Copy")), Bench("bench.cpu.multi", ("GFLOPS", 23, "GFLOPS", "Bench_Cpu_Gflops"))]);
        var ram = PartHistory.Of("ram", r)!;
        Assert.Equal(["memory.pattern name", "bench.memory name"], ram.Lines.Select(l => l.Name));
        Assert.Equal(ReportOutcome.Passed, ram.Lines[0].Outcome); Assert.Null(ram.Lines[1].Outcome);
        Assert.Equal(["Write", "Read", "Latency"], ram.Lines[1].Figures.Select(f => f.Name));   // three at most, the conditions left out
        Assert.Equal(["cpu.matrix name", "bench.cpu.multi name"], PartHistory.Of("cpu", r)!.Lines.Select(l => l.Name));
    }
    [Fact] public void A_part_nothing_ran_on_has_no_summary_not_an_empty_one()
    {
        var r = Report([Test("cpu.matrix", ReportOutcome.Passed), Test("memory.pattern", ReportOutcome.NotRun)]);
        Assert.Null(PartHistory.Of("storage", r)); Assert.Null(PartHistory.Of("ram", r));   // a test that did not run is not a record
        Assert.Null(PartHistory.Of("board", r)); Assert.False(PartHistory.Knows("os"));
    }
    [Fact] public void The_newest_report_that_tested_the_part_is_the_one_used()
    {
        var newer = Report([Test("cpu.matrix", ReportOutcome.Passed)]); var older = Report([Test("storage.smart", ReportOutcome.Passed)]);
        Assert.NotNull(PartHistory.Latest("storage", [newer, older])); Assert.Equal(older.CreatedAt, PartHistory.Latest("storage", [newer, older])!.At);
    }
}
