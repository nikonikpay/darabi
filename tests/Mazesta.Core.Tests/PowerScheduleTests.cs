using Mazesta.Core.Tray; using Xunit;
namespace Mazesta.Core.Tests;

public sealed class PowerScheduleTests
{
    [Theory]
    [InlineData("90", 90)] [InlineData("1:30", 90)] [InlineData("۴۵", 45)] [InlineData("0:01", 1)] [InlineData("48:00", 2880)]
    public void A_wait_is_minutes_or_hours_and_minutes(string text, int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), PowerSchedule.ParseWait(text));

    [Theory]
    [InlineData("")] [InlineData("abc")] [InlineData("0")] [InlineData("-5")] [InlineData("1:75")] [InlineData("49:00")] [InlineData("1:2:3")] [InlineData(null)]
    public void Anything_else_is_refused(string? text) => Assert.Null(PowerSchedule.ParseWait(text));

    [Fact] public void It_warns_in_the_last_minute_and_is_due_at_the_time()
    {
        var now = new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero); var plan = new PowerSchedule(PowerAction.Shutdown, now.AddMinutes(10));
        Assert.False(plan.IsWarning(now)); Assert.True(plan.IsWarning(now.AddSeconds(541))); Assert.False(plan.IsDue(now.AddSeconds(599))); Assert.True(plan.IsDue(now.AddMinutes(10)));
        Assert.Equal(TimeSpan.Zero, plan.Left(now.AddHours(1)));
    }

    [Fact] public void The_time_left_reads_hours_minutes_seconds() => Assert.Equal("1:05:09", PowerSchedule.Format(new TimeSpan(1, 5, 9)));
}
