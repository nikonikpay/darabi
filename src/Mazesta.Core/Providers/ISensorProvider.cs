using Mazesta.Core.Hardware;
namespace Mazesta.Core.Providers;
public readonly record struct PollRequest(DateTimeOffset Now, IReadOnlySet<HardwareId> NodesToUpdate);
public sealed record PollResult(IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus)
{ public static readonly PollResult Empty = new([], new Dictionary<HardwareId, NodeStatus>()); }
public interface ISensorProvider : IDisposable
{
    string Name { get; }
    ProviderStatus Status { get; }
    event Action<ProviderStatus>? StatusChanged;
    void Start();
    IReadOnlyList<HardwareNode> Hardware { get; }
    PollResult Poll(PollRequest request);
}
