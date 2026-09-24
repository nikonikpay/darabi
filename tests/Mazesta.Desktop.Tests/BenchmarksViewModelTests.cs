using Mazesta.Core.Time; using Mazesta.Desktop.Services; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;

public class BenchmarksViewModelTests
{
    private sealed class Fake(BenchmarkStatus status, params BenchmarkMetric[] metrics) : IBenchmark
    {
        public TestDefinition Definition { get; } = new(new TestId("bench.fake"), "Bench_Cpu_Single", 5);
        public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => Task.FromResult(new BenchmarkResult(Definition.Id, status, request.Clock.UtcNow, request.Clock.UtcNow, metrics, "d"));
    }
    private static (BenchmarksViewModel vm, BenchmarkResults results) Build(IBenchmark b)
    {
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var e = new PollingEngine(new FakeSensorProvider(), c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); var results = new BenchmarkResults();
        return (new BenchmarksViewModel([b], e, c, results, a => { a(); return null!; }), results);
    }

    [Fact] public async Task A_completed_run_lists_its_metrics_and_is_kept_for_the_next_report()
    {
        var (vm, results) = Build(new Fake(BenchmarkStatus.Completed, new("Bench_Cpu_Gflops", 12.3456, "GFLOPS"), new("Bench_Cpu_PerThread", 250.4, "GFLOPS")));
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Equal(["12.35 GFLOPS", "250 GFLOPS"], vm.Rows[0].Metrics.Select(m => m.Value)); Assert.Single(results.Snapshot()); Assert.False(vm.IsRunning);
    }
    [Fact] public async Task An_unsupported_run_shows_no_numbers_and_is_not_kept()
    {
        var (vm, results) = Build(new Fake(BenchmarkStatus.Unsupported));
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Empty(vm.Rows[0].Metrics); Assert.Empty(results.Snapshot());
    }
    [Fact] public async Task A_duration_outside_the_allowed_range_is_refused_without_running()
    {
        var (vm, results) = Build(new Fake(BenchmarkStatus.Completed, new BenchmarkMetric("Bench_Cpu_Gflops", 1, "GFLOPS")));
        vm.Rows[0].DurationText = "1"; await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Empty(results.Snapshot()); Assert.Contains("4", vm.Rows[0].StatusText);
    }
    [Fact] public async Task A_completed_run_is_announced_once_so_it_is_saved_as_a_report_and_a_failed_one_is_not()
    {
        var (vm, results) = Build(new Fake(BenchmarkStatus.Completed, new BenchmarkMetric("Bench_Cpu_Gflops", 1, "GFLOPS")));
        var announced = new List<RecordedBenchmark>(); results.Recorded += announced.Add;
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]); results.Record(vm.Rows[0].Benchmark.Definition, BenchmarkResult.Failed(new TestId("bench.fake"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "x"));
        Assert.Single(announced);
    }
    [Fact] public void Only_the_latest_run_of_each_benchmark_is_kept()
    {
        var r = new BenchmarkResults(); var def = new TestDefinition(new TestId("x"), "Bench_Cpu_Single", 5);
        r.Record(def, new(def.Id, BenchmarkStatus.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [new BenchmarkMetric("a", 1, "")], null));
        r.Record(def, new(def.Id, BenchmarkStatus.Completed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), [new BenchmarkMetric("a", 2, "")], null));
        Assert.Equal(2, r.Snapshot().Single().Result.Metrics[0].Value);
    }
}
