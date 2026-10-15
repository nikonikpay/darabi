using Xunit; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class StageCycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly TestDefinition Vector = new(new TestId("cpu.vector"), "Test_Cpu_Vector", 60, [new TestOption("width", "x", TestOptionKind.Text, "auto")]);
    private static readonly TestDefinition Fft = new(new TestId("cpu.fft"), "Test_Cpu_Fft", 60);

    private static (StageCycleExecutor Cycle, List<string> Seen) Make(Func<TestExecutionRequest, TestOutcome> vector)
    {
        var seen = new List<string>();
        var tests = new ITestExecutor[]
        {
            new FakeTestExecutor(Vector, (r, _) => { string w = r.Options!.Get("width"); seen.Add($"vector:{w}:{r.DurationSeconds}"); var o = vector(r); return Task.FromResult(new TestRunResult(Vector.Id, o, T0, T0, o == TestOutcome.Failed ? 3 : 0, $"width {w}")); }),
            new FakeTestExecutor(Fft, (r, _) => { seen.Add($"fft:{r.DurationSeconds}"); return Task.FromResult(new TestRunResult(Fft.Id, TestOutcome.Passed, T0, T0, 0, "fft")); }),
        }.ToDictionary(t => t.Definition.Id.Value);
        return (new StageCycleExecutor(CpuCycle.Definition, CpuCycle.Presets, tests), seen);
    }

    private static TestOptions Custom(string stages, int stageSeconds) => new(CpuCycle.Definition, new Dictionary<string, string>
        { [StageCycleExecutor.PresetOption] = StageCycleExecutor.Custom, [StageCycleExecutor.StagesOption] = stages, [StageCycleExecutor.StageSecondsOption] = stageSeconds.ToString() });

    [Fact] public async Task Stages_go_round_with_their_own_options_until_the_time_is_up()
    {
        var (cycle, seen) = Make(_ => TestOutcome.Passed);
        var r = await cycle.RunAsync(new(50, new FakeClock(T0), null, null, Custom("cpu.vector:width=sse cpu.fft", 20)), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome);
        Assert.Equal(["vector:sse:20", "fft:20", "vector:sse:10"], seen);
    }

    [Fact] public async Task A_stage_the_cpu_cannot_run_is_skipped_not_failed_and_a_fault_names_its_stage()
    {
        var (cycle, seen) = Make(r => r.Options!.Get("width") switch { "avx512" => TestOutcome.Unsupported, "avx2" => TestOutcome.Failed, _ => TestOutcome.Passed });
        var r = await cycle.RunAsync(new(30, new FakeClock(T0), null, null, Custom("cpu.vector:width=avx512 cpu.vector:width=avx2 cpu.fft", 10)), CancellationToken.None);
        Assert.Equal(TestOutcome.Failed, r.Outcome); Assert.Equal(6, r.ErrorCount);   // the skipped stage took no time, so AVX2 ran twice
        Assert.Contains("cpu.vector:width=avx512: skipped", r.Detail); Assert.Contains("cpu.vector:width=avx2: Failed, 6 error(s), 2 run(s)", r.Detail);
        Assert.DoesNotContain(seen, s => s.StartsWith("vector:avx512") && seen.Count(x => x == s) > 1);   // tried once, then left out
    }

    [Fact] public async Task No_stage_that_can_run_is_unsupported()
    {
        var (cycle, _) = Make(_ => TestOutcome.Unsupported);
        Assert.Equal(TestOutcome.Unsupported, (await cycle.RunAsync(new(30, new FakeClock(T0), null, null, Custom("cpu.vector:width=avx512", 10)), CancellationToken.None)).Outcome);
    }

    [Theory] [InlineData("cpu.gpu")] [InlineData("cpu.vector:depth=3")] [InlineData("cpu.vector:width")] [InlineData("cpu.cycle")] [InlineData("")]
    public void Stages_that_cannot_be_read_are_refused(string text)
        => Assert.Throws<FormatException>(() => StageCycleExecutor.Parse(text, new Dictionary<string, ITestExecutor> { ["cpu.vector"] = new FakeTestExecutor(Vector, (_, _) => null!) }, "cpu.cycle"));

    [Fact] public void Every_preset_names_real_tests_and_options()
    {
        var tests = new ITestExecutor[] { new CpuStressExecutor(), new CpuCoreCycleExecutor([]), new LinpackExecutor(new Memory.Win32MemoryProbe()), new CpuVectorStressExecutor(), new CpuFftExecutor() }.ToDictionary(t => t.Definition.Id.Value);
        foreach (var p in CpuCycle.Presets.Values) Assert.NotEmpty(StageCycleExecutor.Parse(p, tests, "cpu.cycle"));
    }

    [Fact] public void Every_stage_gets_at_least_one_turn_in_a_short_run() { Assert.Equal(10, StageCycleExecutor.StageSeconds(120, 60, 6)); Assert.Equal(120, StageCycleExecutor.StageSeconds(120, 3600, 6)); }

    [Fact] public async Task A_cycle_option_such_as_the_gpu_is_handed_to_every_stage_that_has_it()
    {
        var gpuOpt = new TestOption("gpu", "x", TestOptionKind.Text, "");
        var steady = new TestDefinition(new TestId("gpu.steady"), "s", 60, [gpuOpt]); var seen = new List<string>();
        var tests = new Dictionary<string, ITestExecutor> { ["gpu.steady"] = new FakeTestExecutor(steady, (r, _) => { seen.Add(r.Options!.Get("gpu")); return Task.FromResult(new TestRunResult(steady.Id, TestOutcome.Passed, T0, T0, 0, null)); }) };
        var def = new TestDefinition(new TestId("gpu.cycle"), "c", 60, StageCycleExecutor.Options(new Dictionary<string, string> { ["one"] = "gpu.steady" }, "k_", "k", gpuOpt));
        await new StageCycleExecutor(def, new Dictionary<string, string> { ["one"] = "gpu.steady" }, tests).RunAsync(new(10, new FakeClock(T0), null, null, new TestOptions(def, new Dictionary<string, string> { ["gpu"] = "card-2" })), CancellationToken.None);
        Assert.All(seen, g => Assert.Equal("card-2", g)); Assert.NotEmpty(seen);
    }

    [Fact] public async Task A_stage_the_time_ran_out_before_is_inconclusive_not_a_pass()
    {
        var (cycle, seen) = Make(_ => TestOutcome.Passed);
        var r = await cycle.RunAsync(new(1, new FakeClock(T0), null, null, Custom("cpu.vector cpu.fft", 10)), CancellationToken.None);
        Assert.Equal(["vector:auto:1"], seen); Assert.Equal(TestOutcome.Inconclusive, r.Outcome); Assert.Contains("cpu.fft: not reached", r.Detail);
    }
}
