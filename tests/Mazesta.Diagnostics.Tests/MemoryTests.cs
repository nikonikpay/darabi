using Xunit; using Mazesta.Diagnostics.Memory; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class MemoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    private sealed class FixedProbe(long total, long available) : IMemoryProbe { public MemoryStatus Read() => new(total, available); }
    private const long GiB = 1L << 30;

    [Theory] [MemberData(nameof(AllPatterns))]
    public void Every_pattern_reads_back_clean_and_counts_exactly_the_bytes_that_differ(int pass)
    {
        var block = new byte[1 << 20];
        MemoryPatterns.Fill(block, pass, 3);
        Assert.Equal(0, MemoryPatterns.CountMismatches(block, pass, 3));
        block[3] ^= 0xFF; block[100_000] ^= 0x01; block[^1] ^= 0x80;   // three bytes, one bit or eight each
        Assert.Equal(3, MemoryPatterns.CountMismatches(block, pass, 3));
    }
    public static IEnumerable<object[]> AllPatterns() => Enumerable.Range(0, MemoryPatterns.Count).Select(i => new object[] { i });

    [Fact] public void Address_and_random_patterns_differ_between_blocks_so_a_block_that_returns_another_blocks_data_is_caught()
    {
        var a = new byte[4096]; MemoryPatterns.Fill(a, 18, 1);
        Assert.True(MemoryPatterns.CountMismatches(a, 18, 2) > 0);       // only the low address bytes differ between neighbouring blocks - still caught
        MemoryPatterns.Fill(a, 19, 1);
        Assert.True(MemoryPatterns.CountMismatches(a, 19, 2) > 4000);
    }

    [Fact] public void Budget_leaves_the_larger_of_2_gib_and_a_tenth_of_ram_for_the_os()
    {
        Assert.Equal(60 * GiB - (64 * GiB) / 10, MemoryPatternExecutor.Budget(new(64 * GiB, 60 * GiB), 0));   // a tenth of 64 GiB beats 2 GiB
        Assert.Equal(1 * GiB, MemoryPatternExecutor.Budget(new(16 * GiB, 3 * GiB), 0));                         // 2 GiB beats a tenth of 16 GiB
        Assert.Equal(0, MemoryPatternExecutor.Budget(new(16 * GiB, 1 * GiB), 0));                               // nothing above the reserve
    }
    [Fact] public void A_requested_size_can_only_lower_the_budget()
    {
        Assert.Equal(512L << 20, MemoryPatternExecutor.Budget(new(64 * GiB, 60 * GiB), 512));
        Assert.Equal(1 * GiB, MemoryPatternExecutor.Budget(new(16 * GiB, 3 * GiB), 900_000));
    }

    private static TestExecutionRequest Request(int seconds, int sizeMb) =>
        new(seconds, new FakeClock(T0), null, null, new TestOptions(MemoryPatternExecutor.Definition, new Dictionary<string, string> { [MemoryPatternExecutor.SizeOption] = sizeMb.ToString() }));

    [Fact] public async Task A_real_short_run_over_128_mib_verifies_every_pass_and_passes()
    {
        var exec = new MemoryPatternExecutor(new FixedProbe(64 * GiB, 60 * GiB));
        var result = await exec.RunAsync(Request(1, 128), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, result.Outcome); Assert.Equal(0, result.ErrorCount);
        Assert.Contains("tested=128 MiB", result.Detail);
    }
    [Fact] public async Task Too_little_free_ram_is_Unsupported_not_a_pass()
    {
        var result = await new MemoryPatternExecutor(new FixedProbe(8 * GiB, 2 * GiB)).RunAsync(Request(1, 0), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, result.Outcome);
    }
    [Fact] public async Task Cancelling_reports_Cancelled()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var result = await new MemoryPatternExecutor(new FixedProbe(64 * GiB, 60 * GiB)).RunAsync(Request(30, 128), cts.Token);
        Assert.Equal(TestOutcome.Cancelled, result.Outcome);
    }
}
