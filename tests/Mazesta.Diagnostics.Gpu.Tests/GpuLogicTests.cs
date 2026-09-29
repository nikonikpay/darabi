using Xunit; using Mazesta.Diagnostics.Gpu;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GpuLogicTests
{
    [Fact] public void Steady_keeps_the_gpu_busy_for_the_whole_frame()
        => Assert.Equal((TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)), GpuStressExecutor.Plan(GpuStressProfile.Steady, 12.3, 0, 0));

    [Fact] public void Variable_walks_through_load_levels_every_five_seconds_including_idle()
    {
        Func<double, double> busyMs = t => GpuStressExecutor.Plan(GpuStressProfile.Variable, t, 0, 0).Busy.TotalMilliseconds;
        Assert.Equal(100, busyMs(0)); Assert.Equal(30, busyMs(5.1)); Assert.Equal(0, busyMs(10.5)); Assert.Equal(60, busyMs(15)); Assert.Equal(100, busyMs(40));   // 8 levels x 5 s, then it repeats
    }
    [Fact] public void Pulse_is_one_busy_pulse_then_one_idle_gap_exactly_as_configured()
        => Assert.Equal((TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(700)), GpuStressExecutor.Plan(GpuStressProfile.Pulse, 3, 200, 500));

    [Fact] public void Batch_grows_while_a_submission_is_short_so_the_readback_stops_idling_a_fast_gpu()
    {
        var s = new BatchSizer(16); var target = TimeSpan.FromMilliseconds(250);
        s.Record(TimeSpan.FromMilliseconds(8), target); Assert.Equal(32, s.Count);
        s.Record(TimeSpan.FromMilliseconds(200), target); Assert.Equal(32, s.Count);   // near the target: kept
        s.Record(TimeSpan.FromMilliseconds(900), target); Assert.Equal(16, s.Count);   // far past it (a slow card, a throttled one): shrinks
    }
    [Fact] public void Batch_stays_within_its_bounds()
    {
        var s = new BatchSizer(2, min: 1, max: 4);
        for (int i = 0; i < 5; i++) s.Record(TimeSpan.Zero, TimeSpan.FromMilliseconds(100)); Assert.Equal(4, s.Count);
        for (int i = 0; i < 5; i++) s.Record(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100)); Assert.Equal(1, s.Count);
    }
    [Fact] public void Shaped_stress_profiles_keep_a_submission_within_half_their_busy_window()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), GpuStressExecutor.BatchTarget(GpuStressProfile.Steady, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(TimeSpan.FromMilliseconds(15), GpuStressExecutor.BatchTarget(GpuStressProfile.Variable, TimeSpan.FromMilliseconds(30)));
        Assert.Equal(TimeSpan.FromMilliseconds(125), GpuStressExecutor.BatchTarget(GpuStressProfile.Pulse, TimeSpan.FromMilliseconds(250)));
    }

    [Fact] public void Vram_budget_follows_the_live_free_vram_reading_and_keeps_a_margin_for_the_display()
    {
        long gib = 1L << 30;
        Assert.Equal((long)(20480d * 1024 * 1024 * 0.9) - (512L << 20), GpuVramExecutor.Budget(20 * 1024, 24 * gib));   // 20 GB free: 90 % minus 512 MiB
        Assert.Equal(0, GpuVramExecutor.Budget(300, 24 * gib));                                                            // nearly full: nothing safe to test
        Assert.Equal((long)(8 * gib * 0.6), GpuVramExecutor.Budget(null, 8 * gib));                                        // no sensor: 60 % of the dedicated memory
        Assert.Equal(1024L << 20, GpuVramExecutor.Budget(20 * 1024, 24 * gib, requestedMb: 1024));                          // a request can only lower it
        Assert.Equal((long)(4 * gib * 0.9), GpuVramExecutor.Budget(20 * 1024, 24 * gib, residentBytes: 4 * gib));          // Windows' resident budget caps it
    }
    [Fact] public void The_cpu_reference_of_the_vram_pattern_rotates_four_patterns()
    {
        Assert.Equal(0xAAAAAAAAu, VramPattern.Expected(7, 2)); Assert.Equal(0x55555555u, VramPattern.Expected(7, 3));
        Assert.Equal(~VramPattern.Expected(9, 0), VramPattern.Expected(9, 1));
    }
}
