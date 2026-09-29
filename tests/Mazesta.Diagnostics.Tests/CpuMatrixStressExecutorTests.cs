using Xunit;
using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class CpuMatrixStressExecutorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(3)]
    public void The_precomputed_checksums_are_what_the_multiply_gives(int set)
    {
        // The reference must not come from the machine under test, so it is a constant; this pins it. A different value here means the
        // multiply or its inputs changed, not a fault: recompute the constant, and treat it as a new workload version.
        var (a, b) = CpuMatrixStressExecutor.Inputs(set); var c = new double[64, 64];
        CpuMatrixStressExecutor.Multiply(a, b, c);
        Assert.Equal(CpuMatrixStressExecutor.Expected[set], CpuMatrixStressExecutor.Checksum(c));
    }

    [Theory, InlineData(0, 0, 0), InlineData(17, 42, 51), InlineData(63, 63, 1)]
    public void One_flipped_bit_in_one_cell_is_caught(int row, int col, int bit)
    {
        // The whole product is checked, so a single-bit fault anywhere is found every time, not by the luck of a sample.
        var (a, b) = CpuMatrixStressExecutor.Inputs(1); var c = new double[64, 64];
        CpuMatrixStressExecutor.Multiply(a, b, c);
        c[row, col] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(c[row, col]) ^ 1L << bit);
        Assert.NotEqual(CpuMatrixStressExecutor.Expected[1], CpuMatrixStressExecutor.Checksum(c));
    }

    [Fact] public async Task Zero_or_negative_duration_is_Unsupported_not_run()
    {
        var exec = new CpuMatrixStressExecutor(threadCount: 1);
        var result = await exec.RunAsync(new(0, new FakeClock(T0), null, null), CancellationToken.None);
        Assert.Equal(TestOutcome.Unsupported, result.Outcome);
        Assert.Equal(result.StartedAt, result.FinishedAt);
    }

    [Fact] public async Task A_short_real_run_passes_with_measured_iterations_and_reports_progress()
    {
        var exec = new CpuMatrixStressExecutor(threadCount: 2);
        var progress = new List<TestProgress>();
        var result = await exec.RunAsync(new(1, new FakeClock(T0), progress.Add, null), CancellationToken.None);

        Assert.Equal(TestOutcome.Passed, result.Outcome);
        Assert.Equal(0, result.ErrorCount);
        Assert.Contains("matrix load", result.Detail);
        Assert.Contains("threads=2", result.Detail);
        Assert.NotEmpty(progress);
        Assert.Equal(1.0, progress[^1].PercentComplete);
    }

    [Fact] public async Task Cancelling_before_the_run_starts_reports_Cancelled()
    {
        var exec = new CpuMatrixStressExecutor(threadCount: 2);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var result = await exec.RunAsync(new(5, new FakeClock(T0), null, null), cts.Token);
        Assert.Equal(TestOutcome.Cancelled, result.Outcome);
    }
}
