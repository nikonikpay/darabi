using Xunit;
using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class CpuStressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private sealed class FixedProbe(long total, long available) : IMemoryProbe { public MemoryStatus Read() => new(total, available); }

    // ——— Linpack ———
    private static (double[] Lu, int[] Piv, double[] X, double[] B) Solve(int n)
    {
        var a = new double[n * n]; var piv = new int[n]; var b = new double[n]; var x = new double[n];
        LinpackExecutor.Generate(a, n); LinpackExecutor.GenerateRhs(b);
        Assert.True(LinpackExecutor.Factor(a, n, piv, CancellationToken.None));
        b.CopyTo(x, 0); LinpackExecutor.Solve(a, n, piv, x);
        return (a, piv, x, b);
    }

    [Theory, InlineData(7), InlineData(64), InlineData(203)]   // under one block, exactly one, and ragged blocks with a partial last row quad
    public void Linpack_solves_the_system_within_hpl_s_residual_bound(int n)
    {
        var (_, _, x, b) = Solve(n);
        var a = new double[n * n]; LinpackExecutor.Generate(a, n);
        Assert.InRange(LinpackExecutor.ScaledResidual(a, n, x, b), 0, 16);
    }

    [Fact] public void Linpack_gives_the_same_bits_every_time_so_a_difference_is_a_fault()
        => Assert.Equal(LinpackExecutor.Checksum(Solve(300).X), LinpackExecutor.Checksum(Solve(300).X));

    [Fact] public void A_corrupted_factor_breaks_the_residual_check()
    {
        const int n = 150; var (lu, piv, _, b) = Solve(n);
        lu[40 * n + 90] += 0.25;   // one wrong number in U, as a flipped bit would leave it
        var x = (double[])b.Clone(); LinpackExecutor.Solve(lu, n, piv, x);
        var a = new double[n * n]; LinpackExecutor.Generate(a, n);
        Assert.True(LinpackExecutor.ScaledResidual(a, n, x, b) > 16);
    }

    [Fact] public void Linpack_size_is_the_technician_s_or_fits_a_quarter_of_free_ram()
    {
        Assert.Equal(1000, LinpackExecutor.ChooseSize(1000, 0));
        Assert.Equal(LinpackExecutor.AutoMax, LinpackExecutor.ChooseSize(0, 64L << 30));
        Assert.Equal(2048, LinpackExecutor.ChooseSize(0, 4L * 8 * 2048 * 2048));
    }

    [Fact] public void Linpack_fills_the_free_ram_with_systems_and_goes_past_it_only_when_asked()
    {
        const long GiB = 1L << 30;
        Assert.Equal((LinpackExecutor.FillSize, 58), LinpackExecutor.Plan(0, "physical", new(32 * GiB, 31 * GiB, 60 * GiB)));   // 31 GiB free less a twentieth of 32: 29.4 GiB of 512 MiB systems
        Assert.Equal((LinpackExecutor.FillSize, 92), LinpackExecutor.Plan(0, "virtual", new(32 * GiB, 31 * GiB, 60 * GiB)));    // one and a half times the RAM, less 2 GiB
        Assert.Equal(LinpackExecutor.Plan(0, "physical", new(32 * GiB, 31 * GiB)), LinpackExecutor.Plan(0, "virtual", new(32 * GiB, 31 * GiB)));   // the commit limit is not known: as physical
        Assert.Equal(1, LinpackExecutor.Plan(0, "single", new(32 * GiB, 31 * GiB, 60 * GiB)).Count);
        var small = LinpackExecutor.Plan(0, "physical", new(2 * GiB, GiB + (GiB >> 2)));   // a quarter of a gibibyte to fill: one smaller system
        Assert.True(small.Count >= 1 && 8L * small.N * small.N <= GiB >> 2);
    }

    [Fact] public async Task Linpack_fills_memory_and_says_how_much()
    {
        var r = await new LinpackExecutor(new FixedProbe(2L << 30, (1L << 30) + (64L << 20))).RunAsync(new(1, new FakeClock(T0), null, null,
            new TestOptions(LinpackExecutor.Definition, new Dictionary<string, string> { [LinpackExecutor.SizeOption] = "512" })), CancellationToken.None);   // 64 MiB to fill with 2 MiB systems
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains("memory filled: 32 systems, 64 MiB (Physical only)", r.Detail);
    }

    // ——— the full-load test ———
    [Fact] public void The_full_load_test_goes_through_its_stages_in_turn_and_through_all_of_them_in_a_short_run()
    {
        Assert.Equal(60, CpuStressExecutor.StageSeconds(60, 300)); Assert.Equal(10, CpuStressExecutor.StageSeconds(60, 30));
        Assert.Equal([0, 1, 2, 0], new[] { 0.0, 61, 125, 180 }.Select(t => CpuStressExecutor.StageAt(t, 60)));
    }

    [Fact] public void A_variable_load_is_high_then_low_in_turn_and_a_steady_one_stays_high()
    {
        Assert.Equal([1.0, 1.0, 0.0, 1.0], new[] { 0.0, 19.9, 20, 30 }.Select(t => CpuStressExecutor.LoadAt(t, true, 100, 0, 20, 10)));
        Assert.Equal(0.8, CpuStressExecutor.LoadAt(25, false, 80, 0, 20, 10)); Assert.Equal(0.3, CpuStressExecutor.LoadAt(25, true, 80, 30, 20, 10));
    }

    [Fact] public async Task The_full_load_test_runs_every_stage_names_it_in_the_log_and_hands_it_on_with_the_progress()
    {
        var log = new List<TestLogEntry>(); var stages = new List<string?>();
        var r = await new CpuStressExecutor().RunAsync(new(3, new FakeClock(T0), p => { lock (stages) stages.Add(p.Stage); }, null,
            new TestOptions(CpuStressExecutor.Definition, new Dictionary<string, string> { [CpuStressExecutor.StageOption] = "1" }), e => { lock (log) log.Add(e); }), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Equal(0, r.ErrorCount);
        foreach (var s in CpuStressExecutor.Stages)
        {
            Assert.Contains(log, e => e.Key == "Log_CpuStress_Stage" && e.Args.Contains("@" + s.NameKey));
            Assert.Contains(s.NameKey, stages); Assert.DoesNotContain($"{s.Name}: 0 blocks", r.Detail);
        }
    }

    [Fact] public async Task A_variable_load_is_said_in_the_account_so_the_checkup_does_not_judge_it_as_a_full_one()
    {
        var r = await new CpuStressExecutor().RunAsync(new(3, new FakeClock(T0), null, null,
            new TestOptions(CpuStressExecutor.Definition, new Dictionary<string, string> { [CpuStressExecutor.PatternOption] = "variable", [CpuStressExecutor.HighSecondsOption] = "1", [CpuStressExecutor.LowSecondsOption] = "1" })), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains(CpuStressExecutor.PartLoadMark, r.Detail);
    }

    [Fact] public async Task Linpack_passes_a_short_run_and_refuses_what_ram_cannot_hold()
    {
        var ok = await new LinpackExecutor(new FixedProbe(16L << 30, 8L << 30)).RunAsync(new(1, new FakeClock(T0), null, null,
            new TestOptions(LinpackExecutor.Definition, new Dictionary<string, string> { [LinpackExecutor.SizeOption] = "256", [LinpackExecutor.MemoryOption] = "single" })), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, ok.Outcome); Assert.Contains("GFLOPS", ok.Detail);
        var no = await new LinpackExecutor(new FixedProbe(2L << 30, 1L << 30)).RunAsync(new(1, new FakeClock(T0), null, null,
            new TestOptions(LinpackExecutor.Definition, new Dictionary<string, string> { [LinpackExecutor.SizeOption] = "20000", [LinpackExecutor.MemoryOption] = "single" })), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, no.Outcome);
    }

    // ——— single core, core by core ———
    [Fact] public async Task Single_core_visits_every_core_and_passes_when_every_core_agrees()
    {
        var cores = CpuTopology.Cores.Take(2).ToList();
        var r = await new CpuCoreCycleExecutor(cores).RunAsync(new(2, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains($"{cores.Count} physical cores", r.Detail);
    }

    [Fact] public void Single_core_passes_only_when_every_core_was_tested()
    {
        Assert.Equal(TestOutcome.Passed, CpuCoreCycleExecutor.Verdict(0, [true, true]));
        Assert.Equal(TestOutcome.Inconclusive, CpuCoreCycleExecutor.Verdict(0, [true, false]));   // a core never reached, or not pinned: not a pass
        Assert.Equal(TestOutcome.Failed, CpuCoreCycleExecutor.Verdict(3, [true, false]));         // a wrong result fails whatever the coverage
    }

    [Fact] public async Task Single_core_with_too_little_time_for_every_core_is_Inconclusive()
    {
        var cores = CpuTopology.Cores;
        if (cores.Count < 3) return;   // needs more cores than the one-second run can reach
        var r = await new CpuCoreCycleExecutor(cores).RunAsync(new(1, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Inconclusive, r.Outcome); Assert.Contains("not tested", r.Detail);
    }

    [Fact] public void The_topology_lists_every_logical_processor_once()
        => Assert.Equal(Environment.ProcessorCount, CpuTopology.Cores.Sum(c => c.Threads));

    [Fact] public void A_core_s_first_thread_is_one_of_its_own()
        => Assert.All(CpuTopology.Cores, c => { Assert.Equal(1, System.Numerics.BitOperations.PopCount(c.FirstThreadMask)); Assert.NotEqual(0UL, c.FirstThreadMask & c.Mask); });

    // ——— vector stress ———
    [Fact] public async Task Vector_stress_runs_on_the_widest_width_and_passes()
    {
        Assert.NotNull(CpuVectorStressExecutor.Resolve("auto"));
        var r = await new CpuVectorStressExecutor().RunAsync(new(1, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains("GFLOPS", r.Detail);
    }

    [Theory, InlineData(CpuVectorStressExecutor.Width.Sse), InlineData(CpuVectorStressExecutor.Width.Avx2), InlineData(CpuVectorStressExecutor.Width.Avx512)]
    public void Every_vector_lane_equals_the_scalar_reference(CpuVectorStressExecutor.Width width)
    {
        if (CpuVectorStressExecutor.Resolve(width switch { CpuVectorStressExecutor.Width.Avx512 => "avx512", CpuVectorStressExecutor.Width.Avx2 => "avx2", _ => "sse" }) is null) return;
        var got = new double[CpuVectorStressExecutor.LanesOf(width)];
        CpuVectorStressExecutor.Block(width, got);
        Assert.Equal(0, CpuVectorStressExecutor.WrongLanes(got, CpuVectorStressExecutor.Reference(width)));
    }

    [Fact] public void A_wrong_or_non_finite_lane_is_named()
    {
        var expected = CpuVectorStressExecutor.Reference(CpuVectorStressExecutor.Width.Avx2);
        var got = (double[])expected.Clone();
        got[2] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(got[2]) ^ 1);   // the lowest bit of one lane
        Assert.Equal(0b0100, CpuVectorStressExecutor.WrongLanes(got, expected));
        got = (double[])expected.Clone(); got[0] = double.NaN; got[3] = double.PositiveInfinity;
        Assert.Equal(0b1001, CpuVectorStressExecutor.WrongLanes(got, expected));
    }

    [Fact] public void A_width_the_cpu_lacks_is_unavailable_not_failed()
    {
        var exec = new CpuVectorStressExecutor();
        string? missing = new[] { "avx512", "avx2" }.FirstOrDefault(w => CpuVectorStressExecutor.Resolve(w) is null);
        if (missing is null) return;   // this CPU has every width
        Assert.NotNull(exec.CheckAvailability(new TestOptions(CpuVectorStressExecutor.Definition, new Dictionary<string, string> { [CpuVectorStressExecutor.WidthOption] = missing })));
    }
}
