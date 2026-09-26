using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;

/// <summary>
/// The newest good reading of one sensor, with the time it was taken, kept up to date from the polling engine's snapshots. Null until the
/// sensor has reported, and again whenever its last reading was not usable - an old value is never passed off as the current one.
/// </summary>
public sealed class LatestReading : IDisposable
{
    private readonly PollingEngine _engine; private readonly SensorId _id; private readonly object _lock = new();
    private (DateTimeOffset At, double Value)? _value;

    public LatestReading(PollingEngine engine, SensorId id) { _engine = engine; _id = id; engine.SnapshotPublished += OnSnapshot; }

    public (DateTimeOffset At, double Value)? Value { get { lock (_lock) return _value; } }

    /// <summary>The first sensor with <paramref name="role"/> on the hardware named <paramref name="hardwareName"/> (the first of that kind when no
    /// name matches), or null when the machine has no such sensor.</summary>
    public static LatestReading? Find(PollingEngine engine, HardwareKind kind, string? hardwareName, SensorRole role)
    {
        var nodes = engine.Hardware.Where(n => n.Kind == kind).ToList();
        var node = nodes.FirstOrDefault(n => string.Equals(n.Name.Trim(), hardwareName?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? nodes.FirstOrDefault(n => n.Sensors.Any(s => s.Role == role));
        return node?.Sensors.FirstOrDefault(s => s.Role == role) is { } sensor ? new LatestReading(engine, sensor.Id) : null;
    }

    private void OnSnapshot(SensorSnapshot s)
    {
        foreach (var r in s.Readings)
        {
            if (r.Id != _id) continue;
            lock (_lock) _value = r.Quality == DataQuality.Ok && r.Value is { } v ? (r.Timestamp, v) : null;
            return;
        }
    }

    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
