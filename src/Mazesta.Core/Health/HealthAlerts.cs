namespace Mazesta.Core.Health;

public enum HealthAlertKind { CpuOverheat, GpuOverheat, CpuThrottle, GpuHotSpotOverheat, SsdOverheat }

/// <summary>An alert as data; the UI layer words it (in the user's language). <see cref="Value"/> is °C for the overheat kinds and MHz for a throttle.</summary>
public sealed record HealthAlert(HealthAlertKind Kind, double Value, DateTimeOffset Time);

/// <summary>What the rules look at, read from one poll. A member is null when the machine has no such sensor - a rule never runs on a guess.</summary>
public sealed record HealthSample(double? CpuTempC, double? GpuTempC, double? CpuLoadPercent, double? CpuClockMhz, double? GpuHotSpotC = null, double? GpuLoadPercent = null, double? SsdTempC = null, double? GpuVramC = null, double? CpuHottestCoreC = null);

/// <summary>
/// The tray monitor's rules, kept out of the tray process so they are unit-testable. An overheat rule counts only while the part is at full load
/// (<see cref="FullLoadPercent"/>: an idle chip that reads hot is a cooler or sensor question, not a load one) and fires once the temperature has stayed above
/// its threshold for <c>hold</c> (10 minutes by default); it fires again at most every <see cref="RepeatAfter"/> while it stays true, and re-arms only after a
/// clear recovery margin (hysteresis), so a reading bouncing on the threshold does not spam. <see cref="IsWatching"/> tells the host to sample faster while
/// a rule is building up. The thresholds are the user's (the tray's settings, 60 to 105 °C): the processor, the graphics card's average (core) and its hot spot.
/// The throttle rule is a full load under 2 GHz, or - when the CPU's base clock is known - under three quarters of it: a low-power laptop chip (an N100's base
/// is 800 MHz) runs all-core under 2 GHz by design, which is not throttling.
/// </summary>
public sealed class HealthAlerts(TimeSpan? hold = null, double cpuAlertC = HealthAlerts.CpuDefaultC, double gpuAlertC = HealthAlerts.GpuDefaultC, double? cpuBaseMhz = null, double gpuHotSpotAlertC = HealthAlerts.GpuHotSpotDefaultC, double ssdAlertC = HealthAlerts.SsdDefaultC)
{
    private readonly double _throttleMhz = cpuBaseMhz is > 0 and { } b ? Math.Min(ThrottleClockMhz, b * 0.75) : ThrottleClockMhz;
    public const double MinAlertC = 60, MaxAlertC = 105, ClearMarginC = 5, CpuDefaultC = 98, GpuDefaultC = 90, GpuHotSpotDefaultC = 98, SsdDefaultC = 70, FullLoadPercent = 90;
    public const double ThrottleLoadPercent = 98, ThrottleLoadRecoveredPercent = 95, ThrottleClockMhz = 2000, ThrottleClockRecoveredMhz = 2200;
    public static readonly TimeSpan RepeatAfter = TimeSpan.FromMinutes(15), DefaultHold = TimeSpan.FromMinutes(10), ThrottleHold = TimeSpan.FromMinutes(2);

    private sealed class Rule { public DateTimeOffset? Since, Last, LastFired; }
    private readonly Dictionary<HealthAlertKind, Rule> _rules = Enum.GetValues<HealthAlertKind>().ToDictionary(k => k, _ => new Rule());
    private readonly TimeSpan _hold = hold is { } h && h > TimeSpan.Zero ? h : DefaultHold;

    /// <summary>True while any rule is counting towards (or past) its threshold: the host should sample faster.</summary>
    public bool IsWatching => _rules.Values.Any(r => r.Since is not null);

    public IReadOnlyList<HealthAlert> Evaluate(HealthSample s, DateTimeOffset now)
    {
        var alerts = new List<HealthAlert>();
        bool? cpuFull = s.CpuLoadPercent is { } cl ? cl >= FullLoadPercent : null, gpuFull = s.GpuLoadPercent is { } gl ? gl >= FullLoadPercent : null;
        Step(HealthAlertKind.CpuOverheat, s.CpuTempC is { } c && cpuFull is { } cf ? c > cpuAlertC && cf : null, s.CpuTempC < cpuAlertC - ClearMarginC, s.CpuTempC ?? 0, _hold);
        Step(HealthAlertKind.GpuOverheat, s.GpuTempC is { } g && gpuFull is { } gf ? g > gpuAlertC && gf : null, s.GpuTempC < gpuAlertC - ClearMarginC, s.GpuTempC ?? 0, _hold);
        Step(HealthAlertKind.GpuHotSpotOverheat, s.GpuHotSpotC is { } hs && gpuFull is { } hf ? hs > gpuHotSpotAlertC && hf : null, s.GpuHotSpotC < gpuHotSpotAlertC - ClearMarginC, s.GpuHotSpotC ?? 0, _hold);
        // A drive heats whatever the processors do (a copy, a sleeping fan): no load condition, only the time over the limit.
        Step(HealthAlertKind.SsdOverheat, s.SsdTempC is { } ssd ? ssd > ssdAlertC : null, s.SsdTempC < ssdAlertC - ClearMarginC, s.SsdTempC ?? 0, _hold);
        bool? throttling = s.CpuLoadPercent is { } load && s.CpuClockMhz is { } clock ? load >= ThrottleLoadPercent && clock < _throttleMhz : null;
        bool recovered = s.CpuLoadPercent is < ThrottleLoadRecoveredPercent || s.CpuClockMhz >= _throttleMhz * (ThrottleClockRecoveredMhz / ThrottleClockMhz);
        Step(HealthAlertKind.CpuThrottle, throttling, recovered, s.CpuClockMhz ?? 0, _hold < ThrottleHold ? _hold : ThrottleHold);
        return alerts;

        // active == null: no sensor this sample, so the rule neither advances nor resets. A gap longer than the hold (the PC slept) starts the count over.
        void Step(HealthAlertKind kind, bool? active, bool clear, double value, TimeSpan needed)
        {
            var rule = _rules[kind];
            if (active is null) return;
            if (active == false) { rule.Since = null; if (clear) rule.LastFired = null; return; }
            if (rule.Since is null || now - rule.Last > (_hold > TimeSpan.FromMinutes(5) ? _hold : TimeSpan.FromMinutes(5))) rule.Since = now;
            rule.Last = now;
            if (now - rule.Since >= needed && (rule.LastFired is null || now - rule.LastFired >= RepeatAfter)) { alerts.Add(new(kind, value, now)); rule.LastFired = now; }
        }
    }
}
