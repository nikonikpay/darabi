using Mazesta.Desktop.Localization; using Mazesta.Core.Time; using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring.Tests.Fakes; using Xunit;
namespace Mazesta.Desktop.Tests;

public class BenchmarksViewModelTests
{
    private sealed class Fake(BenchmarkStatus status, params BenchmarkMetric[] metrics) : IBenchmark
    {
        public TestDefinition Definition { get; } = new(new TestId("bench.fake"), "Bench_Cpu_Single", 5);
        public Mazesta.Core.Hardware.HardwareKind Component => Mazesta.Core.Hardware.HardwareKind.Cpu;
        public Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct) => Task.FromResult(new BenchmarkResult(Definition.Id, status, request.Clock.UtcNow, request.Clock.UtcNow, metrics, "d"));
    }
    private static (BenchmarksViewModel vm, BenchmarkRunner runner) Build(IBenchmark b)
    {
        var runner = new BenchmarkRunner([b], new FakeClock(DateTimeOffset.UnixEpoch), null);
        return (new BenchmarksViewModel(runner, a => { a(); return null!; }), runner);
    }

    [Fact] public async Task A_completed_run_lists_its_metrics_and_is_kept_for_the_next_report()
    {
        var (vm, runner) = Build(new Fake(BenchmarkStatus.Completed, new("Bench_Cpu_Gflops", 12.3456, "GFLOPS"), new("Bench_Cpu_PerThread", 250.4, "GFLOPS")));
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Equal(["12.35 GFLOPS", "250 GFLOPS"], vm.Rows[0].Metrics.Take(2).Select(m => m.Value)); Assert.Contains(vm.Rows[0].Metrics, m => m.Name == Loc.Get("Bench_Set_Duration")); Assert.Single(runner.Completed()); Assert.False(vm.IsRunning);
    }
    [Fact] public async Task An_unsupported_run_shows_no_numbers_and_is_not_kept()
    {
        var (vm, runner) = Build(new Fake(BenchmarkStatus.Unsupported));
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Empty(vm.Rows[0].Metrics); Assert.Empty(runner.Completed());
    }
    [Fact] public async Task A_duration_outside_the_allowed_range_is_refused_without_running()
    {
        var (vm, runner) = Build(new Fake(BenchmarkStatus.Completed, new BenchmarkMetric("Bench_Cpu_Gflops", 1, "GFLOPS")));
        vm.Rows[0].DurationText = "1"; await vm.RunCommand.ExecuteAsync(vm.Rows[0]);
        Assert.Empty(runner.Completed()); Assert.Contains("4", vm.Rows[0].StatusText);
    }
    [Fact] public void A_component_page_lists_only_that_parts_benchmarks()
    {
        var runner = new BenchmarkRunner([new Fake(BenchmarkStatus.Completed)], new FakeClock(DateTimeOffset.UnixEpoch), null);
        Assert.Single(new BenchmarksViewModel(runner, a => { a(); return null!; }, Mazesta.Core.Hardware.HardwareKind.Cpu).Rows);
        Assert.Empty(new BenchmarksViewModel(runner, a => { a(); return null!; }, Mazesta.Core.Hardware.HardwareKind.Gpu).Rows);
    }
    [Fact] public async Task A_page_opened_again_shows_the_last_result()
    {
        var (vm, runner) = Build(new Fake(BenchmarkStatus.Completed, new BenchmarkMetric("Bench_Cpu_Gflops", 7, "GFLOPS")));
        await vm.RunCommand.ExecuteAsync(vm.Rows[0]); vm.Dispose();
        var again = new BenchmarksViewModel(runner, a => { a(); return null!; });
        Assert.Equal(["7.00 GFLOPS"], again.Rows[0].Metrics.Take(1).Select(m => m.Value)); Assert.False(again.IsRunning);
    }
}
