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

    private sealed class Scripted(params BenchmarkResult?[] results) : IBenchmark   // null: throws
    {
        private int _next;
        public TestDefinition Definition { get; } = new(new TestId("bench.x"), "Bench_Cpu_Single", 5);
        public Mazesta.Core.Hardware.HardwareKind Component => Mazesta.Core.Hardware.HardwareKind.Cpu;
        public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => results[_next++] is { } r ? Task.FromResult(r) : throw new InvalidOperationException("boom");
    }
    private static BenchmarkResult Done(BenchmarkStatus status, double value) => new(new TestId("bench.x"), status, T0, T0, status == BenchmarkStatus.Completed ? [new("k", value, "")] : [], null);

    [Fact] public async Task The_runner_keeps_the_latest_completed_run_and_a_run_that_measured_nothing_replaces_nothing()
    {
        var runner = new BenchmarkRunner([new Scripted(Done(BenchmarkStatus.Completed, 1), Done(BenchmarkStatus.Completed, 2), Done(BenchmarkStatus.Cancelled, 0), null)], new FakeClock(T0), null);
        var finished = new List<RecordedBenchmark>(); runner.Finished += finished.Add;
        for (int i = 0; i < 4; i++) await runner.RunAsync(runner.Benchmarks[0], 5, new Dictionary<string, string>());
        Assert.Equal(2, runner.Completed().Single().Result.Metrics[0].Value);
        Assert.Equal(BenchmarkStatus.Error, runner.Last(new TestId("bench.x"))!.Status);   // the benchmark threw: reported, never lost, and not as the part failing
        Assert.Equal(4, finished.Count); Assert.Null(runner.Running);
    }

    [Fact] public async Task The_runner_starts_nothing_while_a_test_queue_holds_the_gate_and_says_so()
    {
        var gate = new WorkloadGate();
        var runner = new BenchmarkRunner([new Scripted(Done(BenchmarkStatus.Completed, 1), Done(BenchmarkStatus.Completed, 2))], new FakeClock(T0), null, gate);
        using (gate.TryEnter(Workload.Tests))
        {
            Assert.Null(await runner.RunAsync(runner.Benchmarks[0], 5, new Dictionary<string, string>()));
            Assert.Empty(await runner.RunQueueAsync([new(runner.Benchmarks[0], 5, new Dictionary<string, string>())]));
            Assert.Equal(Workload.Tests, runner.BlockedBy);
            Assert.False(runner.IsBusy);
        }
        Assert.NotNull(await runner.RunAsync(runner.Benchmarks[0], 5, new Dictionary<string, string>()));
        Assert.Null(runner.BlockedBy); Assert.Null(gate.Holder);   // the run gave the gate back
    }

    [Fact] public async Task A_run_carries_a_copy_of_the_options_it_started_with()
    {
        var runner = new BenchmarkRunner([new Scripted(Done(BenchmarkStatus.Completed, 1))], new FakeClock(T0), null);
        var live = new Dictionary<string, string> { ["gpu"] = "card A" };
        RecordedBenchmark? run = null; runner.Finished += r => { run = r; live["gpu"] = "card B"; };   // the page changes its pick as the run ends
        await runner.RunAsync(runner.Benchmarks[0], 5, live);
        Assert.Equal("card A", run!.Options!["gpu"]);
    }

    [Fact] public async Task Memory_without_enough_free_ram_is_Unsupported_not_a_zero_result()
    {
        var r = await new MemoryBenchmark(new FixedProbe(8L << 30, 1L << 30)).RunAsync(Request(4), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Unsupported, r.Status); Assert.Empty(r.Metrics);
    }
    [Fact] public async Task Memory_reports_four_positive_bandwidths()
    {
        var r = await new MemoryBenchmark(new FixedProbe(64L << 30, 48L << 30)).RunAsync(Request(4), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status);
        foreach (var key in new[] { "Bench_Mem_Write", "Bench_Mem_Read", "Bench_Mem_Copy", "Bench_Mem_CopyAll", "Bench_Mem_StreamCopy", "Bench_Mem_StreamScale", "Bench_Mem_StreamAdd", "Bench_Mem_StreamTriad", "Bench_Mem_Latency" })
            Assert.True(Value(r, key) > 0, key);
        Assert.Contains("results checked", r.Detail);
    }

    [Fact] public void Stream_kernels_leave_the_values_plain_arithmetic_predicts()
    {
        using var a = new Mazesta.Diagnostics.Memory.NativeBlock(1 << 20); using var b = new Mazesta.Diagnostics.Memory.NativeBlock(1 << 20); using var c = new Mazesta.Diagnostics.Memory.NativeBlock(1 << 20);
        var r = MemoryBenchmark.Stream(a, b, c, TimeSpan.Zero, null, CancellationToken.None);
        Assert.Null(r.Problem); Assert.Equal(3, r.Runs); Assert.InRange(r.Error, 0, 1e-15);
        Assert.All(r.Best, x => Assert.True(x > 0));
    }

    [Fact] public void The_latency_chain_is_one_cycle_through_every_line()
    {
        var block = new byte[64 * 1000]; MemoryBenchmark.Chain(block, 1);
        var seen = new HashSet<uint>(); uint at = 0;
        for (int k = 0; k < 1000; k++) { Assert.True(seen.Add(at)); at = BitConverter.ToUInt32(block, (int)at * 64); }
        Assert.Equal(1000, seen.Count); Assert.Equal(0u, at);   // back at the start only after visiting all 1000 lines
        Assert.Equal(0u, MemoryBenchmark.Chase(block, 1000)); Assert.NotEqual(0u, MemoryBenchmark.Chase(block, 999));
    }

    [Fact] public async Task A_short_read_is_an_io_error_not_a_finished_operation()
    {
        using var file = StorageFile.Create(_dir, StorageFile.LengthOf(16), asynchronous: true, writeThrough: false);
        using var buffer = new Mazesta.Diagnostics.Memory.NativeBlock(StorageFile.Block);
        await Assert.ThrowsAsync<IOException>(async () => await StorageBenchmark.ReadAll(file, buffer.Memory, file.Length - StorageFile.Sector, CancellationToken.None));   // runs off the end
    }

    [Fact] public async Task Storage_reports_every_sequential_and_random_speed_and_leaves_no_file_behind()
    {
        var options = new TestOptions(StorageBenchmark.Spec, new Dictionary<string, string> { [StorageExecutor.DriveOption] = _dir, [StorageExecutor.FileMbOption] = "32" });
        var r = await new StorageBenchmark(TimeSpan.FromMilliseconds(50)).RunAsync(Request(2, options), CancellationToken.None);
        Assert.Equal(BenchmarkStatus.Completed, r.Status); Assert.Equal(20, StorageBenchmark.Spec.DefaultDurationSeconds);
        foreach (var key in new[] { "Bench_Storage_SeqWrite", "Bench_Storage_SeqRead", "Bench_Storage_Rand4kQ32Read", "Bench_Storage_Rand4kQ1Read", "Bench_Storage_Rand4kLatency", "Bench_Storage_Rand4kQ32Write", "Bench_Storage_Mixed", "Bench_Storage_MixedIops" }) Assert.True(Value(r, key) > 0, key);
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

    /// <summary>A run keeps how it was set up: its length, the workload's version and the options that change the work, never the device option
    /// (the drive or GPU is the part, recorded apart).</summary>
    [Fact] public void A_run_records_its_length_version_and_work_options()
    {
        var setup = BenchmarkRunner.Setup(StorageBenchmark.Spec, 20, new Dictionary<string, string> { [StorageExecutor.DriveOption] = "D:\\", [StorageExecutor.FileMbOption] = "256" }).ToList();
        Assert.Contains(setup, s => s.Key == "Bench_Set_Duration" && s.Value == "20 s");
        Assert.Contains(setup, s => s.Key == "Bench_Set_Version" && s.Value == "2");
        Assert.Contains(setup, s => s.Key == "Test_Option_FileMb" && s.Value == "256");
        Assert.DoesNotContain(setup, s => s.Key == "Test_Option_Drive");
        Assert.All(setup, s => Assert.Equal(BenchmarkDetails.RunGroup, s.Group));
    }
}
