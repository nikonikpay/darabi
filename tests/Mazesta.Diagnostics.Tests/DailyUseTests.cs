using Xunit; using Mazesta.Diagnostics.Cpu; using Mazesta.Diagnostics.Tests.Fakes;
namespace Mazesta.Diagnostics.Tests;

public class DailyUseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact] public void A_cycle_is_an_hour_unless_the_run_is_shorter() { Assert.Equal(3600, DailyUseExecutor.CycleSeconds(60, 8 * 3600)); Assert.Equal(600, DailyUseExecutor.CycleSeconds(60, 600)); Assert.Equal(1800, DailyUseExecutor.CycleSeconds(30, 3600)); }
    [Fact] public void The_phases_fill_the_cycle_and_idle_comes_twice() { Assert.Equal(1.0, DailyUseExecutor.Cycle.Sum(c => c.Share), 9); Assert.Equal(2, DailyUseExecutor.Cycle.Count(c => c.Phase == DailyUseExecutor.Phase.Idle)); }
    [Fact] public void Sorting_and_json_round_trips_check_clean() { Assert.Equal(0, DailyUseExecutor.SortCheck(5)); Assert.Equal(0, DailyUseExecutor.JsonCheck(5)); }

    [Fact] public async Task A_short_run_goes_through_every_phase_and_passes()
    {
        var options = new TestOptions(DailyUseExecutor.Definition, new Dictionary<string, string> { [DailyUseExecutor.CycleOption] = "1" });
        var stages = new List<string>();
        var r = await new DailyUseExecutor([]).RunAsync(new(12, new FakeClock(T0), p => { if (p.Stage is { } s) lock (stages) stages.Add(s); }, null, options), CancellationToken.None);
        Assert.Equal(TestOutcome.Passed, r.Outcome); Assert.Equal(0, r.ErrorCount);
        foreach (var p in Enum.GetValues<DailyUseExecutor.Phase>()) Assert.Contains($"Test_Daily_Phase_{p}", stages);
        Assert.Contains("1 whole cycle(s)", r.Detail);
    }
}
