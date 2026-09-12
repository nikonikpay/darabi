using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Monitoring.Tests;
public class SensorStatisticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly SensorId A = new("cpu/intelcpu-0#temperature/0"), B = new("gpu/nvidiagpu-0#temperature/0");
    private static SensorSnapshot Snap(long seq, params (SensorId id, double? v, DataQuality q)[] r)
        => new(seq, T0.AddSeconds(seq * 2), r.Select(x => new SensorReading(x.id, x.v, T0, x.q, "t")).ToList(), new Dictionary<HardwareId, NodeStatus>());
    [Fact] public void Tracks_min_max_average_of_ok_readings_only()
    {
        var s = new SensorStatistics(T0);
        s.Apply(Snap(1, (A, 40, DataQuality.Ok))); s.Apply(Snap(2, (A, 60, DataQuality.Ok))); s.Apply(Snap(3, (A, 999, DataQuality.Invalid))); s.Apply(Snap(4, (A, null, DataQuality.Missing))); s.Apply(Snap(5, (A, 50, DataQuality.Stale)));
        var st = s.Get(A); Assert.Equal((40.0, 60.0, 50.0, 2L), (st.Min, st.Max, st.Average, st.Count));
    }
    [Fact] public void Unknown_sensor_returns_empty_stats() { var st = new SensorStatistics(T0).Get(A); Assert.Null(st.Min); Assert.Equal(0, st.Count); }
    [Fact] public void Reset_by_hardware_only_clears_that_hardware()
    {
        var s = new SensorStatistics(T0); s.Apply(Snap(1, (A, 40, DataQuality.Ok), (B, 70, DataQuality.Ok)));
        s.Reset(new HardwareId("cpu/intelcpu-0"), T0.AddMinutes(1));
        Assert.Equal(0, s.Get(A).Count); Assert.Equal(1, s.Get(B).Count); Assert.Equal(T0.AddMinutes(1), s.Get(A).Since);
    }
}
