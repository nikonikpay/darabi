using Mazesta.Desktop.ViewModels; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks; using Mazesta.Monitoring.Tests.Fakes; using Xunit;
namespace Mazesta.Desktop.Tests;

public class BenchmarkQueueTests
{
    /// <summary>A benchmark that waits until released (or cancelled), so a test can look at the queue while it runs.</summary>
    private sealed class Gate(string id) : IBenchmark
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously); public readonly SemaphoreSlim Release = new(0);
        public TestDefinition Definition { get; } = new(new TestId(id), "Bench_Cpu_Single", 5);
        public Mazesta.Core.Hardware.HardwareKind Component => Mazesta.Core.Hardware.HardwareKind.Cpu;
        public async Task<BenchmarkResult> RunAsync(TestExecutionRequest request, CancellationToken ct)
        {
            Started.TrySetResult();
            try { await Release.WaitAsync(ct); } catch (OperationCanceledException) { return new(Definition.Id, BenchmarkStatus.Cancelled, request.Clock.UtcNow, request.Clock.UtcNow, [], null); }
            return new(Definition.Id, BenchmarkStatus.Completed, request.Clock.UtcNow, request.Clock.UtcNow, [new("Bench_Cpu_Gflops", 1, "GFLOPS")], null);
        }
    }
    private static Func<Action, object> Now => a => { a(); return null!; };

    [Fact] public async Task Ticked_benchmarks_run_one_after_another_in_page_order_and_the_page_stays_busy_between_them()
    {
        var a = new Gate("a"); var b = new Gate("b"); var c = new Gate("c");
        var runner = new BenchmarkRunner([a, b, c], new FakeClock(DateTimeOffset.UnixEpoch), null); var vm = new BenchmarksViewModel(runner, Now);
        IReadOnlyList<RecordedBenchmark>? finished = null; runner.QueueFinished += r => finished = r;
        vm.Rows[2].IsSelected = true; vm.Rows[0].IsSelected = true;
        var run = vm.RunSelectedCommand.ExecuteAsync(null);
        await a.Started.Task; Assert.False(b.Started.Task.IsCompleted); Assert.Equal(Loc("Bench_Status_Queued"), vm.Rows[2].StatusText);
        Assert.True(vm.IsRunning); Assert.Null(await runner.RunAsync(b, 5, new Dictionary<string, string>()));   // nothing else starts meanwhile
        a.Release.Release(); await c.Started.Task; Assert.True(vm.IsRunning); Assert.Equal(Mazesta.Desktop.Localization.Loc.Format("Bench_Queue_Position", 2, 2), vm.QueueText);
        c.Release.Release(); await run;
        Assert.Equal(["a", "c"], finished!.Select(r => r.Definition.Id.Value)); Assert.False(vm.IsRunning); Assert.Equal("", vm.QueueText); Assert.False(b.Started.Task.IsCompleted);
    }

    [Fact] public async Task Cancel_stops_the_running_benchmark_and_skips_the_rest()
    {
        var a = new Gate("a"); var b = new Gate("b");
        var runner = new BenchmarkRunner([a, b], new FakeClock(DateTimeOffset.UnixEpoch), null); var vm = new BenchmarksViewModel(runner, Now);
        vm.SelectAllCommand.Execute(null);
        var run = vm.RunSelectedCommand.ExecuteAsync(null);
        await a.Started.Task; vm.CancelCommand.Execute(null); await run;
        Assert.Equal(Loc("Bench_Status_Cancelled"), vm.Rows[0].StatusText); Assert.Equal(Loc("Bench_Status_Skipped"), vm.Rows[1].StatusText);
        Assert.False(b.Started.Task.IsCompleted); Assert.False(runner.IsBusy);
    }

    [Fact] public async Task One_bad_duration_starts_nothing()
    {
        var a = new Gate("a"); var b = new Gate("b");
        var runner = new BenchmarkRunner([a, b], new FakeClock(DateTimeOffset.UnixEpoch), null); var vm = new BenchmarksViewModel(runner, Now);
        vm.SelectAllCommand.Execute(null); vm.Rows[1].DurationText = "2";
        await vm.RunSelectedCommand.ExecuteAsync(null);
        Assert.False(a.Started.Task.IsCompleted); Assert.False(vm.IsRunning); Assert.Contains("4", vm.Rows[1].StatusText);
    }

    [Fact] public void Run_selected_needs_a_ticked_row() => Assert.False(new BenchmarksViewModel(new BenchmarkRunner([new Gate("a")], new FakeClock(DateTimeOffset.UnixEpoch), null), Now).RunSelectedCommand.CanExecute(null));

    private static string Loc(string key) => Mazesta.Desktop.Localization.Loc.Get(key);
}
