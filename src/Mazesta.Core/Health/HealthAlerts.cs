namespace Mazesta.Core.Health;

public enum HealthAlertKind { CpuOverheat, GpuOverheat, CpuThrottle }

/// <summary>An alert as data; the UI layer words it (in the user's language). <see cref="Value"/> is °C for the overheat kinds and MHz for a throttle.</summary>
public sealed record HealthAlert(HealthAlertKind Kind, double Value, DateTimeOffset Time);

/// <summary>What the rules look at, read from one poll. A member is null when the machine has no such sensor - a rule never runs on a guess.</summary>
public sealed record HealthSample(double? CpuTempC, double? GpuTempC, double? CpuLoadPercent, double? CpuClockMhz);

/// <summary>
/// The tray monitor's rules, kept out of the tray process so they are unit-testable. Each rule must hold for
/// <c>requiredSamples</c> consecutive samples before it fires, fires again at most every <see cref="RepeatAfter"/>
/// while it stays true, and re-arms only after a clear recovery margin (hysteresis), so a reading bouncing on the
/// threshold does not spam. <see cref="IsWatching"/> tells the host to sample faster while a rule is building up.
/// </summary>
public sealed class HealthAlerts(int requiredSamples = 3)
{
    public const double OverheatC = 95, OverheatRecoveredC = 90, ThrottleLoadPercent = 98, ThrottleLoadRecoveredPercent = 95, ThrottleClockMhz = 2000, ThrottleClockRecoveredMhz = 2200;
    public static readonly TimeSpan RepeatAfter = TimeSpan.FromMinutes(15);

    private sealed class Rule { public int Count; public DateTimeOffset? LastFired; }
    private readonly Dictionary<HealthAlertKind, Rule> _rules = Enum.GetValues<HealthAlertKind>().ToDictionary(k => k, _ => new Rule());
    private readonly int _required = Math.Max(1, requiredSamples);

    /// <summary>True while any rule is counting towards (or past) its threshold: the host should sample faster.</summary>
    public bool IsWatching => _rules.Values.Any(r => r.Count > 0);

    public IReadOnlyList<HealthAlert> Evaluate(HealthSample s, DateTimeOffset now)
    {
        var alerts = new List<HealthAlert>();
        Step(HealthAlertKind.CpuOverheat, s.CpuTempC is { } c ? c > OverheatC : null, s.CpuTempC is < OverheatRecoveredC, s.CpuTempC ?? 0);
        Step(HealthAlertKind.GpuOverheat, s.GpuTempC is { } g ? g > OverheatC : null, s.GpuTempC is < OverheatRecoveredC, s.GpuTempC ?? 0);
        bool? throttling = s.CpuLoadPercent is { } load && s.CpuClockMhz is { } clock ? load >= ThrottleLoadPercent && clock < ThrottleClockMhz : null;
        bool recovered = s.CpuLoadPercent is < ThrottleLoadRecoveredPercent || s.CpuClockMhz is >= ThrottleClockRecoveredMhz;
        Step(HealthAlertKind.CpuThrottle, throttling, recovered, s.CpuClockMhz ?? 0);
        return alerts;

        // active == null: no sensor this sample, so the rule neither advances nor resets.
        void Step(HealthAlertKind kind, bool? active, bool clear, double value)
        {
            var rule = _rules[kind];
            if (active is null) return;
            if (active == false) { rule.Count = 0; if (clear) rule.LastFired = null; return; }
            rule.Count++;
            if (rule.Count >= _required && (rule.LastFired is null || now - rule.LastFired >= RepeatAfter)) { alerts.Add(new(kind, value, now)); rule.LastFired = now; }
        }
    }
}
