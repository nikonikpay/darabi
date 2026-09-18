using Xunit;
using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class CpuMatrixStressExecutorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact] public void VerifySpotChecks_catches_a_deliberately_corrupted_cell()
    {
        // This is the test that proves the correctness check actually works (spec §9): a healthy run
        // never corrupts a multiply, so RunAsync alone could never exercise this branch. Called
        // directly against a genuine 64x64 multiply plus one flipped cell instead.
        var rng = new Random(1);
        var a = new double[64, 64]; var b = new double[64, 64]; var c = new double[64, 64];
        CpuMatrixStressExecutor.Fill(a, rng); CpuMatrixStressExecutor.Fill(b, rng);
        CpuMatrixStressExecutor.Multiply(a, b, c);
        Assert.True(CpuMatrixStressExecutor.VerifySpotChecks(a, b, c, new Random(2)));   // untouched: real multiply verifies clean

        // VerifySpotChecks only samples 4 of 4096 cells, so corrupting one cell would pass or fail by
        // luck depending on the sampling seed. Corrupting every cell instead makes detection
        // deterministic regardless of which cells get sampled - the point being tested here is that a
        // real corruption IS caught, not the sampling density.
        for (int i = 0; i < 64; i++) for (int j = 0; j < 64; j++) c[i, j] += 1.0;   // simulate a bit-flip / RAM fault across the result
        Assert.False(CpuMatrixStressExecutor.VerifySpotChecks(a, b, c, new Random(3)));
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
