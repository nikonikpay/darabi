using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Monitoring.Tests;
public class StaleDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static SensorReading Ok(DateTimeOffset ts) => new(new SensorId("x#y"), 1, ts, DataQuality.Ok, "t");
    [Fact] public void Fresh_reading_stays_ok() => Assert.Equal(DataQuality.Ok, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddSeconds(5)));
    [Fact] public void Older_than_three_cadences_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddSeconds(7)));
    [Fact] public void Slow_cadence_tolerates_long_gaps() => Assert.Equal(DataQuality.Ok, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromMinutes(15), T0.AddMinutes(40)));
    [Fact] public void Failed_node_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), NodeStatus.Failed("x", T0, T0), TimeSpan.FromSeconds(2), T0));
    [Fact] public void Non_ok_quality_is_preserved() { var r = Ok(T0) with { Quality = DataQuality.Invalid }; Assert.Equal(DataQuality.Invalid, StaleDetector.Apply(r, NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddMinutes(1))); }
    [Fact] public void Missing_status_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), null, TimeSpan.FromSeconds(2), T0));
}
