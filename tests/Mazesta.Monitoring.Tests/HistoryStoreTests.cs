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
    private static SensorSnapshot WideSnap(int sensors, long seq, int seconds)
        => new(seq, T0.AddSeconds(seconds), Enumerable.Range(0, sensors).Select(i => new SensorReading(new SensorId($"n#{i}"), 1, T0.AddSeconds(seconds), DataQuality.Ok, "t")).ToList(), new Dictionary<HardwareId, NodeStatus>());
    [Fact] public void Byte_budget_for_609_sensors_on_the_first_tick_is_raw_only()
    {
        // 609 sensors is the dev box's real sensor count. On the first tick no sensor has crossed a
        // minute boundary, so only the raw rings exist: 609 x 900 x 8 = 4,384,800 bytes (4.18 MB).
        var h = new HistoryStore(T0);
        h.Append(WideSnap(609, 0, 0));
        Assert.Equal(609L * 900 * 8, h.EstimatedBytes);
        Assert.InRange(h.EstimatedBytes, 1, 5L * 1024 * 1024);
    }
    [Fact] public void Minute_tier_is_allocated_only_after_the_first_rollover()
    {
        var h = new HistoryStore(T0);
        h.Append(WideSnap(609, 0, 0)); long rawOnly = h.EstimatedBytes;
        h.Append(WideSnap(609, 1, 30)); Assert.Equal(rawOnly, h.EstimatedBytes);      // still inside minute 0
        h.Append(WideSnap(609, 2, 61));                                                // rollover into minute 1
        Assert.Equal(rawOnly + 609L * 2879 * 18, h.EstimatedBytes);
    }
    [Fact] public void Minute_values_survive_the_rollover_that_allocates_the_tier()
    {
        var h = new HistoryStore(T0);
        h.Append(Snap(0, 10, 40)); h.Append(Snap(1, 20, 60));
        Assert.Equal([0], h.GetMinutes(A).Minute);                                      // in-progress bucket, no arrays yet
        h.Append(Snap(2, 70, 80));
        var m = h.GetMinutes(A);
        Assert.Equal([0, 1], m.Minute); Assert.Equal((40f, 60f, 50f), (m.Min[0], m.Max[0], m.Avg[0])); Assert.Equal(80f, m.Avg[1]);
    }
    [Fact] public void Unknown_sensor_returns_empty_series() => Assert.Empty(new HistoryStore(T0).GetRaw(A).Seconds);
}
