using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct SensorStats(double? Min, double? Max, double? Average, long Count, DateTimeOffset Since);
public sealed class SensorStatistics(DateTimeOffset since)
{
    private sealed class Acc(DateTimeOffset since) { public double Min = double.MaxValue, Max = double.MinValue, Sum; public long Count; public DateTimeOffset Since = since; }
    private readonly Dictionary<SensorId, Acc> _acc = []; private readonly object _lock = new(); private DateTimeOffset _since = since;
    public void Apply(SensorSnapshot s)
    {
        lock (_lock)
            foreach (var r in s.Readings)
            {
                if (r.Quality != DataQuality.Ok || r.Value is null) continue;
                if (!_acc.TryGetValue(r.Id, out var a)) _acc[r.Id] = a = new Acc(_since);
                double v = r.Value.Value; a.Min = Math.Min(a.Min, v); a.Max = Math.Max(a.Max, v); a.Sum += v; a.Count++;
            }
    }
    public SensorStats Get(SensorId id)
    {
        lock (_lock) return _acc.TryGetValue(id, out var a) && a.Count > 0 ? new SensorStats(a.Min, a.Max, a.Sum / a.Count, a.Count, a.Since) : new SensorStats(null, null, null, 0, _acc.TryGetValue(id, out var e) ? e.Since : _since);
    }
    public void ResetAll(DateTimeOffset now) { lock (_lock) { _acc.Clear(); _since = now; } }
    public void Reset(HardwareId hardware, DateTimeOffset now)
    { lock (_lock) foreach (var id in _acc.Keys.Where(k => k.Hardware == hardware).ToList()) _acc[id] = new Acc(now); }
}
