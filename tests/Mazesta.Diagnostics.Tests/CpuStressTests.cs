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

    [Fact] public async Task Linpack_passes_a_short_run_and_refuses_what_ram_cannot_hold()
    {
        var ok = await new LinpackExecutor(new FixedProbe(16L << 30, 8L << 30)).RunAsync(new(1, new FakeClock(T0), null, null,
            new TestOptions(LinpackExecutor.Definition, new Dictionary<string, string> { [LinpackExecutor.SizeOption] = "256" })), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, ok.Outcome); Assert.Contains("GFLOPS", ok.Detail);
        var no = await new LinpackExecutor(new FixedProbe(2L << 30, 1L << 30)).RunAsync(new(1, new FakeClock(T0), null, null,
            new TestOptions(LinpackExecutor.Definition, new Dictionary<string, string> { [LinpackExecutor.SizeOption] = "20000" })), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, no.Outcome);
    }

    // ——— single core, core by core ———
    [Fact] public async Task Single_core_visits_every_core_and_passes_when_every_core_agrees()
    {
        var cores = CpuTopology.Cores.Take(2).ToList();
        var r = await new CpuCoreCycleExecutor(cores).RunAsync(new(2, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains($"{cores.Count} physical cores", r.Detail);
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

    [Fact] public void A_width_the_cpu_lacks_is_unavailable_not_failed()
    {
        var exec = new CpuVectorStressExecutor();
        string? missing = new[] { "avx512", "avx2" }.FirstOrDefault(w => CpuVectorStressExecutor.Resolve(w) is null);
        if (missing is null) return;   // this CPU has every width
        Assert.NotNull(exec.CheckAvailability(new TestOptions(CpuVectorStressExecutor.Definition, new Dictionary<string, string> { [CpuVectorStressExecutor.WidthOption] = missing })));
    }
}
