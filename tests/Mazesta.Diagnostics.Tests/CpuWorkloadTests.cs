using Xunit;
using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class CpuWorkloadTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    // ——— integer ———
    [Fact] public void The_integer_block_gives_the_pinned_checksum()
        => Assert.Equal(CpuIntegerExecutor.Expected, CpuIntegerExecutor.Block(new ulong[CpuIntegerExecutor.Elements], CpuIntegerExecutor.Seed()));

    [Fact] public void One_flipped_bit_in_the_integer_data_changes_the_checksum()
    {
        var seed = CpuIntegerExecutor.Seed(); seed[1234] ^= 1UL << 40;   // as a fault in a register or the cache would leave it
        Assert.NotEqual(CpuIntegerExecutor.Expected, CpuIntegerExecutor.Block(new ulong[CpuIntegerExecutor.Elements], seed));
    }

    [Fact] public async Task A_short_integer_run_passes_with_a_rate()
    {
        var r = await new CpuIntegerExecutor().RunAsync(new(1, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains("Gop/s (integer)", r.Detail);
    }

    // ——— which logical processors ran the load ———
    [Fact] public void Coverage_counts_the_processors_seen_by_core_type_and_by_group()
    {
        // A hybrid part: two P-cores with two threads each (class 1) and four E-cores (class 0), the last E-core in group 1.
        CpuCore[] cores = [new(0, 0, 0b11, 1), new(1, 0, 0b1100, 1), new(2, 0, 1 << 4, 0), new(3, 0, 1 << 5, 0), new(4, 0, 1 << 6, 0), new(5, 1, 1, 0)];
        var c = new CpuCoverage();
        foreach (int n in new[] { 0, 1, 2, 4 }) c.Mark(0, n);
        c.Mark(1, 0);
        Assert.Equal("ran on 5 of 8 logical processors (P-cores 3/4 threads; E-cores 2/4 threads; groups 0: 4/7, 1: 1/1)", c.Describe(cores));
        Assert.Equal("ran on 0 of 2 logical processors", new CpuCoverage().Describe([new(0, 0, 0b11, 0)]));
    }

    [Fact] public async Task A_real_run_names_the_processors_it_ran_on()
        => Assert.Matches(@"ran on \d+ of \d+ logical processors", (await new CpuIntegerExecutor().RunAsync(new(1, new FakeClock(T0), null, null), CancellationToken.None)).Detail);

    // ——— hash and compression ———
    [Fact] public void The_text_hashes_to_the_value_python_worked_out()   // hashlib over the same generator: an independent reference
        => Assert.Equal(CpuHashExecutor.ExpectedSha256, CpuHashExecutor.Hex(CpuHashExecutor.Text()));

    [Fact] public void A_block_round_trips_through_deflate_and_a_changed_byte_is_caught()
    {
        var text = CpuHashExecutor.Text(); var (before, after, size) = CpuHashExecutor.Block(text, new MemoryStream(), new byte[text.Length]);
        Assert.Equal(CpuHashExecutor.ExpectedSha256, before); Assert.Equal(before, after); Assert.InRange(size, 1, text.Length / 2);   // the word soup compresses well
        text[4321] ^= 0x10;
        Assert.NotEqual(CpuHashExecutor.ExpectedSha256, CpuHashExecutor.Block(text, new MemoryStream(), new byte[text.Length]).Before);
    }

    [Fact] public async Task A_short_hash_run_passes_with_a_rate()
    {
        var r = await new CpuHashExecutor().RunAsync(new(1, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains("MB/s of input", r.Detail);
    }

    // ——— FFT ———
    [Fact] public void The_fft_agrees_with_a_direct_dft() => Assert.InRange(CpuFftExecutor.DftError(256), 0, 1e-12);

    [Fact] public void The_reference_passes_its_round_trip_and_parseval_checks()
        => Assert.Null(CpuFftExecutor.Reference(new CpuFftExecutor.Plan(CpuFftExecutor.Small)).Problem);

    [Fact] public void A_corrupted_twiddle_is_caught_by_the_round_trip_or_parseval_check()
    {
        var plan = new CpuFftExecutor.Plan(CpuFftExecutor.Small);
        plan.Cos[17] += 1e-6;   // one wrong factor, as a faulty FPU would compute it
        Assert.NotNull(CpuFftExecutor.Reference(plan).Problem);
    }

    [Fact] public void The_same_input_gives_the_same_bits_so_a_difference_is_a_fault()
    {
        var plan = new CpuFftExecutor.Plan(1024);
        double[] Run() { var re = new double[1024]; var im = new double[1024]; CpuFftExecutor.Input(re, im); CpuFftExecutor.Transform(plan, re, im); return re; }
        var a = Run(); var b = Run();
        Assert.True(CpuFftExecutor.Same(a, b));
        b[500] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(b[500]) ^ 1);
        Assert.False(CpuFftExecutor.Same(a, b));
    }

    [Fact] public async Task A_short_fft_run_passes_with_a_rate()
    {
        var r = await new CpuFftExecutor().RunAsync(new(2, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Contains("GFLOPS (5·N·log2 N)", r.Detail);
    }

    private sealed class Probe(long total, long free) : Mazesta.Diagnostics.Memory.IMemoryProbe { public Mazesta.Diagnostics.Memory.MemoryStatus Read() => new(total, free); }
    [Fact] public async Task The_fft_takes_only_the_workers_free_ram_holds_and_none_is_not_a_cpu_failure()
    {
        const long G = 1L << 30;
        Assert.Equal(64, CpuFftExecutor.Workers(new(64 * G, 60 * G), 64));
        Assert.Equal(1, CpuFftExecutor.Workers(new(16 * G, 2 * G + 50 * (1L << 20)), 64));   // above the 2 GiB reserve: room for one
        Assert.Equal(0, CpuFftExecutor.Workers(new(16 * G, G), 64));
        var r = await new CpuFftExecutor(new Probe(16 * G, G)).RunAsync(new(2, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, r.Outcome);
    }
}
