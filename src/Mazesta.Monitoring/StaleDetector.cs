using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public static class StaleDetector
{
    public const int StaleMultiplier = 3;
    public static DataQuality Apply(SensorReading r, NodeStatus? status, TimeSpan cadence, DateTimeOffset now)
    {
        if (r.Quality != DataQuality.Ok) return r.Quality;
        if (status is null || !status.IsOk || status.LastSuccessfulUpdate is null) return DataQuality.Stale;
        return now - status.LastSuccessfulUpdate.Value > cadence * StaleMultiplier ? DataQuality.Stale : DataQuality.Ok;
    }
}
