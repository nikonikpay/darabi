using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public sealed class MonitoringOptions
{
    public static readonly int[] AllowedFastSeconds = [1, 2, 5, 30];
    /// <summary>The overlay's own refresh choices, in seconds: the monitor's intervals up to 5 s and half a second.</summary>
    public static readonly double[] OverlayRefreshSeconds = [0.5, 1, 2, 5];
    public static bool IsAllowed(TimeSpan interval) => interval == TimeSpan.FromMilliseconds(500) || (interval.TotalSeconds == Math.Floor(interval.TotalSeconds) && AllowedFastSeconds.Contains((int)interval.TotalSeconds));
    public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan StorageInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CadenceFor(HardwareKind kind) => kind == HardwareKind.Storage ? StorageInterval : FastInterval;
}
