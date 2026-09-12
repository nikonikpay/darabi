namespace Mazesta.Core.Hardware;

public enum ProviderState { NotStarted, Starting, Ready, Degraded, Failed }

public sealed record ProviderStatus(ProviderState State, int SensorCount, string? ReasonKey, string? Detail)
{
    public static readonly ProviderStatus NotStarted = new(ProviderState.NotStarted, 0, null, null);
    public static readonly ProviderStatus Starting = new(ProviderState.Starting, 0, null, null);
    public static ProviderStatus Ready(int sensorCount) => new(ProviderState.Ready, sensorCount, null, null);
    public static ProviderStatus Degraded(string reasonKey, string detail, int sensorCount) => new(ProviderState.Degraded, sensorCount, reasonKey, detail);
    public static ProviderStatus Failed(string reasonKey, string detail) => new(ProviderState.Failed, 0, reasonKey, detail);
}
