using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public sealed class MonitoringOptions
{
    public static readonly int[] AllowedFastSeconds = [1, 2, 5, 30];
    public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan StorageInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CadenceFor(HardwareKind kind) => kind == HardwareKind.Storage ? StorageInterval : FastInterval;
}
