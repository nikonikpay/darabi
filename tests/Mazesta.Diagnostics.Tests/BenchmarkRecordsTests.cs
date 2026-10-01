using Xunit; using Mazesta.Diagnostics.Benchmarks;
namespace Mazesta.Diagnostics.Tests;

public class BenchmarkRecordsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-records-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private static BenchmarkResult Cpu(double gflops, int minutes = 0, BenchmarkStatus status = BenchmarkStatus.Completed)
        => new(CpuBenchmark.Multi.Id, status, T0, T0.AddMinutes(minutes), status == BenchmarkStatus.Completed ? [new("Bench_Cpu_Gflops", gflops, "GFLOPS"), new("Bench_Cpu_Power", 90, "W")] : [], null);

    [Fact] public void The_first_complete_run_sets_the_record()
    {
        var c = new BenchmarkRecords(_dir).Offer("pc", "PC", "bench.cpu.multi", Cpu(100))!;
        Assert.True(c.Saved); Assert.Null(c.Previous); Assert.Null(c.ChangePercent);
    }
    [Fact] public void A_slower_run_is_compared_but_not_kept_even_after_a_restart()
    {
        var r = new BenchmarkRecords(_dir);
        r.Offer("pc", "PC", "bench.cpu.multi", Cpu(100));
        var slower = r.Offer("pc", "PC", "bench.cpu.multi", Cpu(80, 5))!;
        Assert.False(slower.Saved); Assert.Equal(-20, slower.ChangePercent!.Value, 6); Assert.Equal(100, slower.Previous!.Value);
        Assert.Equal(100, new BenchmarkRecords(_dir).Best("pc", "bench.cpu.multi")!.Value);
    }
    [Fact] public void A_faster_run_replaces_the_record_and_survives_a_restart()
    {
        var r = new BenchmarkRecords(_dir);
        r.Offer("pc", "PC", "bench.cpu.multi", Cpu(100));
        var faster = r.Offer("pc", "PC", "bench.cpu.multi", Cpu(110, 5))!;
        Assert.True(faster.Saved); Assert.Equal(10, faster.ChangePercent!.Value, 6);
        var kept = new BenchmarkRecords(_dir).Best("pc", "bench.cpu.multi")!;
        Assert.Equal(110, kept.Value); Assert.Equal(T0.AddMinutes(5), kept.At); Assert.Equal(2, kept.Metrics.Count);
    }
    [Fact] public void An_equal_run_keeps_the_older_record()
    {
        var r = new BenchmarkRecords(_dir);
        r.Offer("pc", "PC", "bench.cpu.multi", Cpu(100));
        Assert.False(r.Offer("pc", "PC", "bench.cpu.multi", Cpu(100, 5))!.Saved);
        Assert.Equal(T0, r.Best("pc", "bench.cpu.multi")!.At);
    }
    [Fact] public void Cancelled_or_headless_runs_are_never_ranked()
    {
        var r = new BenchmarkRecords(_dir);
        Assert.Null(r.Offer("pc", "PC", "bench.cpu.multi", Cpu(0, status: BenchmarkStatus.Cancelled)));
        Assert.Null(r.Offer("pc", "PC", "bench.cpu.multi", new(CpuBenchmark.Multi.Id, BenchmarkStatus.Completed, T0, T0, [new("Bench_Cpu_Power", 90, "W")], null)));
        Assert.Null(r.Best("pc", "bench.cpu.multi")); Assert.False(File.Exists(Path.Combine(_dir, "benchmarks", "records.json")));
    }
    [Fact] public void Systems_and_options_keep_separate_records()
    {
        var r = new BenchmarkRecords(_dir);
        r.Offer("pc-a", "A", "bench.cpu.multi", Cpu(100));
        Assert.True(r.Offer("pc-b", "B", "bench.cpu.multi", Cpu(50))!.Saved);
        Assert.Equal("bench.storage|drive=D:\\|size=1024|v=2", BenchmarkRecords.RecordKey("bench.storage", new Dictionary<string, string> { ["size"] = "1024", ["drive"] = "D:\\" }));
        Assert.Equal("bench.cpu.multi", BenchmarkRecords.RecordKey("bench.cpu.multi", new Dictionary<string, string>()));
    }
    [Fact] public void A_record_of_an_earlier_workload_is_kept_but_not_compared()
    {
        var r = new BenchmarkRecords(_dir);
        var mem = new BenchmarkResult(MemoryBenchmark.Spec.Id, BenchmarkStatus.Completed, T0, T0, [new("Bench_Mem_Read", 50, "GB/s")], null);
        r.Offer("pc", "PC", "bench.memory", mem);   // as version 2 and earlier kept it, with no version in the key
        var c = r.Offer("pc", "PC", BenchmarkRecords.RecordKey("bench.memory", null), mem with { Metrics = [new("Bench_Mem_Read", 40, "GB/s")] })!;
        Assert.Null(c.Previous); Assert.True(c.Saved);
        Assert.Equal(50, new BenchmarkRecords(_dir).Best("pc", "bench.memory")!.Value);
    }
    [Fact] public void Lower_is_better_flips_the_sign()
    {
        var old = new BenchmarkRecord(T0, "lat", 100, "µs", []); var now = old with { Value = 80 };
        var c = BenchmarkRecords.Compare(now, old, higherIsBetter: false);
        Assert.True(c.Saved); Assert.Equal(20, c.ChangePercent!.Value, 6);
    }
    [Fact] public void A_damaged_file_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "benchmarks"));
        File.WriteAllText(Path.Combine(_dir, "benchmarks", "records.json"), "{ not json");
        new BenchmarkRecords(_dir).Offer("pc", "PC", "bench.cpu.multi", Cpu(100));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "benchmarks"), "records.json.damaged-*"));
    }
}
