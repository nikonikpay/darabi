using Xunit; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Storage; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class BenchmarkTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-bench-tests-" + Guid.NewGuid().ToString("N"));
    public BenchmarkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private sealed class FixedProbe(long total, long available) : IMemoryProbe { public MemoryStatus Read() => new(total, available); }
    private static TestExecutionRequest Request(int seconds, TestOptions? options = null) => new(seconds, new FakeClock(T0), null, null, options);
    private static double Value(BenchmarkResult r, string key) => r.Metrics.Single(m => m.Key == key).Value;

    [Theory, InlineData(false), InlineData(true)] public async Task Cpu_single_and_multi_thread_are_separate_benchmarks_with_positive_throughput(bool allThreads)
    {
        var cpu = new CpuBenchmark(allThreads);
        var r = await cpu.RunAsync(Request(1), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status); Assert.Equal(cpu.Definition.Id, r.Id); Assert.True(Value(r, "Bench_Cpu_Gflops") > 0);
        Assert.Equal(allThreads ? Environment.ProcessorCount : null, r.Metrics.SingleOrDefault(m => m.Key == "Bench_Threads")?.Value);
        Assert.DoesNotContain(r.Metrics, m => m.Key.StartsWith("Bench_Cpu_Clock"));   // no engine: unmeasured stays absent, never 0
    }
    [Fact] public void Cpu_benchmarks_default_to_a_minute() => Assert.All([CpuBenchmark.Single, CpuBenchmark.Multi, MemoryBenchmark.Spec], d => Assert.Equal(60, d.DefaultDurationSeconds));
    [Fact] public async Task A_cancelled_benchmark_returns_no_numbers()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var r = await new CpuBenchmark(allThreads: true).RunAsync(Request(2), cts.Token);
        Assert.Equal(BenchmarkStatus.Cancelled, r.Status); Assert.Empty(r.Metrics);
    }
    [Fact] public async Task Zero_duration_is_Unsupported()
        => Assert.Equal(BenchmarkStatus.Unsupported, (await new CpuBenchmark(allThreads: false).RunAsync(Request(0), CancellationToken.None)).Status);

    [Fact] public async Task Memory_without_enough_free_ram_is_Unsupported_not_a_zero_result()
    {
        var r = await new MemoryBenchmark(new FixedProbe(8L << 30, 1L << 30)).RunAsync(Request(4), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Unsupported, r.Status); Assert.Empty(r.Metrics);
    }
    [Fact] public async Task Memory_reports_four_positive_bandwidths()
    {
        var r = await new MemoryBenchmark(new FixedProbe(64L << 30, 48L << 30)).RunAsync(Request(4), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status);
        foreach (var key in new[] { "Bench_Mem_Write", "Bench_Mem_Read", "Bench_Mem_Copy", "Bench_Mem_CopyAll" }) Assert.True(Value(r, key) > 0, key);
    }

    [Fact] public async Task Storage_reports_every_crystaldiskmark_style_speed_and_leaves_no_file_behind()
    {
        var options = new TestOptions(StorageBenchmark.Spec, new Dictionary<string, string> { [StorageExecutor.DriveOption] = _dir, [StorageExecutor.FileMbOption] = "32" });
        var r = await new StorageBenchmark(TimeSpan.FromMilliseconds(50)).RunAsync(Request(2, options), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status); Assert.Equal(20, StorageBenchmark.Spec.DefaultDurationSeconds);
        foreach (var key in new[] { "Bench_Storage_SeqWrite", "Bench_Storage_SeqRead", "Bench_Storage_Rand4kQ32Read", "Bench_Storage_Rand4kQ1Read", "Bench_Storage_Rand4kLatency", "Bench_Storage_Rand4kQ32Write" }) Assert.True(Value(r, key) > 0, key);
        Assert.Empty(Directory.GetFiles(_dir, ".mazesta-test-*"));
    }
    [Fact] public async Task A_cancelled_storage_benchmark_returns_no_numbers_and_no_file()
    {
        using var cts = new CancellationTokenSource(); cts.CancelAfter(300);
        var options = new TestOptions(StorageBenchmark.Spec, new Dictionary<string, string> { [StorageExecutor.DriveOption] = _dir, [StorageExecutor.FileMbOption] = "32" });
        var r = await new StorageBenchmark(TimeSpan.FromSeconds(30)).RunAsync(Request(2, options), cts.Token);   // cancelled during the rest
        Assert.Equal(BenchmarkStatus.Cancelled, r.Status); Assert.Empty(r.Metrics); Assert.Empty(Directory.GetFiles(_dir, ".mazesta-test-*"));
    }
    [Fact] public async Task Storage_in_a_missing_folder_is_Unsupported()
    {
        var options = new TestOptions(StorageBenchmark.Spec, new Dictionary<string, string> { [StorageExecutor.DriveOption] = Path.Combine(_dir, "nope"), [StorageExecutor.FileMbOption] = "32" });
        Assert.Equal(BenchmarkStatus.Unsupported, (await new StorageBenchmark(TimeSpan.Zero).RunAsync(Request(2, options), CancellationToken.None)).Status);
    }
}
