using Xunit; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class CpuCycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly TestDefinition Vector = new(new TestId("cpu.vector"), "Test_Cpu_Vector", 60, [new TestOption("width", "x", TestOptionKind.Text, "auto")]);
    private static readonly TestDefinition Fft = new(new TestId("cpu.fft"), "Test_Cpu_Fft", 60);

    private static (CpuCycleExecutor Cycle, List<string> Seen) Make(Func<TestExecutionRequest, TestOutcome> vector)
    {
        var seen = new List<string>();
        var tests = new ITestExecutor[]
        {
            new FakeTestExecutor(Vector, (r, _) => { string w = r.Options!.Get("width"); seen.Add($"vector:{w}:{r.DurationSeconds}"); var o = vector(r); return Task.FromResult(new TestRunResult(Vector.Id, o, T0, T0, o == TestOutcome.Failed ? 3 : 0, $"width {w}")); }),
            new FakeTestExecutor(Fft, (r, _) => { seen.Add($"fft:{r.DurationSeconds}"); return Task.FromResult(new TestRunResult(Fft.Id, TestOutcome.Passed, T0, T0, 0, "fft")); }),
        }.ToDictionary(t => t.Definition.Id.Value);
        return (new CpuCycleExecutor(tests), seen);
    }

    private static TestOptions Custom(string stages, int stageSeconds) => new(CpuCycleExecutor.Definition, new Dictionary<string, string>
        { [CpuCycleExecutor.PresetOption] = CpuCycleExecutor.Custom, [CpuCycleExecutor.StagesOption] = stages, [CpuCycleExecutor.StageSecondsOption] = stageSeconds.ToString() });

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
        => Assert.Throws<FormatException>(() => CpuCycleExecutor.Parse(text, new Dictionary<string, ITestExecutor> { ["cpu.vector"] = new FakeTestExecutor(Vector, (_, _) => null!) }));

    [Fact] public void Every_preset_names_real_tests_and_options()
    {
        var tests = new ITestExecutor[] { new CpuStressExecutor(), new CpuCoreCycleExecutor([]), new LinpackExecutor(new Memory.Win32MemoryProbe()), new CpuVectorStressExecutor(), new CpuFftExecutor() }.ToDictionary(t => t.Definition.Id.Value);
        foreach (var p in CpuCycleExecutor.Presets.Values) Assert.NotEmpty(CpuCycleExecutor.Parse(p, tests));
    }

    [Fact] public void Every_stage_gets_at_least_one_turn_in_a_short_run() { Assert.Equal(10, CpuCycleExecutor.StageSeconds(120, 60, 6)); Assert.Equal(120, CpuCycleExecutor.StageSeconds(120, 3600, 6)); }
}
