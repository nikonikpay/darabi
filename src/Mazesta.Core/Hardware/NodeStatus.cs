namespace Mazesta.Core.Hardware;

public sealed record NodeStatus(bool IsOk, string? FailureReason, DateTimeOffset? FailingSince, DateTimeOffset? LastSuccessfulUpdate)
{
    public static NodeStatus Healthy(DateTimeOffset lastUpdate) => new(true, null, null, lastUpdate);
    public static NodeStatus Failed(string reason, DateTimeOffset since, DateTimeOffset? lastUpdate) => new(false, reason, since, lastUpdate);
    public static readonly NodeStatus NeverUpdated = new(true, null, null, null);
}
