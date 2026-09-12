using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct FocusRequest(IReadOnlySet<HardwareKind> Kinds, string Reason);
public sealed class MonitoringFocus
{
    public event Action<FocusRequest>? FocusRequested;
    public void RequestFocus(IReadOnlySet<HardwareKind> kinds, string reason) => FocusRequested?.Invoke(new FocusRequest(kinds, reason));
}
