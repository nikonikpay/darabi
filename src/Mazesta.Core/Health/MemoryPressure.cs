namespace Mazesta.Core.Health;

/// <summary>
/// The tray's «memory is nearly full» rule: RAM in use at or above the threshold (90 % by default) for the whole hold (5 minutes), so a brief spike is
/// not announced, and then at most once a calendar day. It re-arms after the reading falls 3 points below the threshold; a reading in between neither
/// starts nor resets the hold, so a figure bouncing on the line is one episode. A missing reading changes nothing.
/// </summary>
public sealed class MemoryPressure(double alertPercent = MemoryPressure.DefaultPercent, TimeSpan? hold = null)
{
    public const double DefaultPercent = 90, ClearMargin = 3;
    public static readonly TimeSpan DefaultHold = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _hold = hold is { } h && h > TimeSpan.Zero ? h : DefaultHold;
    private DateTimeOffset? _since;

    /// <summary>When it last fired (the host keeps this across restarts, so one day means one day).</summary>
    public DateTimeOffset? LastFired { get; set; }
    public bool IsWatching => _since is not null;

    public bool Evaluate(double? usedPercent, DateTimeOffset now)
    {
        if (usedPercent is not { } p) return false;
        if (p < alertPercent - ClearMargin) { _since = null; return false; }
        if (p < alertPercent) return false;
        _since ??= now;
        if (now - _since < _hold || LastFired is { } last && last.ToLocalTime().Date == now.ToLocalTime().Date) return false;
        LastFired = now; return true;
    }
}
