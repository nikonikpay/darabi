using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Monitoring.Tests;
public class HistoryStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly SensorId A = new("cpu/x#temperature/0");
    private static SensorSnapshot Snap(long seq, int seconds, double? v, DataQuality q = DataQuality.Ok)
        => new(seq, T0.AddSeconds(seconds), [new SensorReading(A, v, T0.AddSeconds(seconds), q, "t")], new Dictionary<HardwareId, NodeStatus>());
    [Fact] public void Raw_ring_keeps_only_capacity_newest()
    {
        var h = new HistoryStore(T0, rawCapacity: 3, minuteCapacity: 10);
        for (int i = 0; i < 5; i++) h.Append(Snap(i, i * 2, 40 + i));
        var raw = h.GetRaw(A); Assert.Equal([4, 6, 8], raw.Seconds); Assert.Equal([42f, 43f, 44f], raw.Values);
    }
    [Fact] public void Non_ok_readings_store_nan_gap()
    {
        var h = new HistoryStore(T0); h.Append(Snap(0, 0, 40)); h.Append(Snap(1, 2, null, DataQuality.Missing)); h.Append(Snap(2, 4, 999, DataQuality.Invalid)); h.Append(Snap(3, 6, 41, DataQuality.Stale));
        var raw = h.GetRaw(A); Assert.Equal(4, raw.Values.Length); Assert.True(float.IsNaN(raw.Values[1]) && float.IsNaN(raw.Values[2]) && float.IsNaN(raw.Values[3]));
    }
    [Fact] public void Minute_tier_aggregates_ok_samples_per_minute()
    {
        var h = new HistoryStore(T0);
        h.Append(Snap(0, 10, 40)); h.Append(Snap(1, 30, 60)); h.Append(Snap(2, 50, null, DataQuality.Missing)); h.Append(Snap(3, 70, 100));
        var m = h.GetMinutes(A); Assert.Equal([0, 1], m.Minute); Assert.Equal((40f, 60f, 50f), (m.Min[0], m.Max[0], m.Avg[0])); Assert.Equal(100f, m.Avg[1]);
    }
    [Fact] public void Minute_with_no_ok_samples_is_nan_gap()
    {
        var h = new HistoryStore(T0); h.Append(Snap(0, 5, null, DataQuality.Missing));
        var m = h.GetMinutes(A); Assert.Single(m.Minute); Assert.True(float.IsNaN(m.Avg[0]));
    }
    [Fact] public void Minute_ring_is_bounded()
    {
        var h = new HistoryStore(T0, rawCapacity: 10, minuteCapacity: 2);
        for (int i = 0; i < 5; i++) h.Append(Snap(i, i * 60, i));
        Assert.Equal([3, 4], h.GetMinutes(A).Minute);
    }
    [Fact] public void Byte_budget_for_200_sensors_is_under_13_MB()
    {
        var h = new HistoryStore(T0);
        var readings = Enumerable.Range(0, 200).Select(i => new SensorReading(new SensorId($"n#{i}"), 1, T0, DataQuality.Ok, "t")).ToList();
        h.Append(new SensorSnapshot(0, T0, readings, new Dictionary<HardwareId, NodeStatus>()));
        Assert.InRange(h.EstimatedBytes, 1, 13L * 1024 * 1024);
    }
    [Fact] public void Unknown_sensor_returns_empty_series() => Assert.Empty(new HistoryStore(T0).GetRaw(A).Seconds);
}
