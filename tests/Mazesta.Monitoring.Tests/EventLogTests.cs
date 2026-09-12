using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Monitoring; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Monitoring.Tests;
public class EventLogTests
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero); }
    [Fact] public void Keeps_newest_entries_up_to_capacity()
    {
        var log = new BoundedEventLog(new Clock(), NullLogger.Instance, capacity: 3);
        for (int i = 0; i < 5; i++) log.Log(EventLevel.Info, "k", i.ToString());
        Assert.Equal(["2", "3", "4"], log.Snapshot().Select(e => e.Detail));
    }
    [Fact] public void Raises_logged_event() { var log = new BoundedEventLog(new Clock(), NullLogger.Instance); EventEntry? got = null; log.Logged += e => got = e; log.Log(EventLevel.Warning, "Engine.PollOverrun", "x"); Assert.Equal("Engine.PollOverrun", got!.Value.Key); }
}
