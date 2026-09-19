using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Diagnostics.Evidence;

public readonly record struct SensorStat(double Average, double Max, int Samples)
{
    public string Format(string label, string unit, bool includeMax = false)
        => includeMax ? $"{label} avg {Average:F1}{unit} max {Max:F1}{unit} (n={Samples})" : $"{label} avg {Average:F1}{unit} (n={Samples})";
}

/// <summary>
/// Spec §8: "نمره سرعت به‌تنهایی PASS سلامت نیست" - a pass must be backed by measurements of the machine
/// itself, not by the test's own say-so. Reads the sensor history <see cref="PollingEngine"/> has already
/// recorded for exactly a run's own time window. Returns null - never a made-up number - when there is no
/// engine, no such sensor, or no sample fell inside the window (a run shorter than one polling interval).
/// </summary>
public static class SensorEvidence
{
    public static SensorStat? Read(PollingEngine? engine, HardwareKind kind, SensorRole role, DateTimeOffset from, DateTimeOffset to)
    {
        if (engine is null) return null;
        var sensors = engine.Hardware.Where(n => n.Kind == kind).SelectMany(n => n.Sensors).Where(s => s.Role == role).ToList();
        int startSec = engine.History.SecondsSinceEpoch(from), endSec = engine.History.SecondsSinceEpoch(to);
        var samples = new List<float>();
        foreach (var s in sensors)
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = 0; i < raw.Seconds.Length; i++)
                if (raw.Seconds[i] >= startSec && raw.Seconds[i] <= endSec && !float.IsNaN(raw.Values[i])) samples.Add(raw.Values[i]);
        }
        return samples.Count == 0 ? null : new(samples.Average(), samples.Max(), samples.Count);
    }

    /// <summary>The most recent valid reading of a role, or null. A test uses it to size itself from what the
    /// machine reports right now (how much VRAM is free) instead of assuming.</summary>
    public static double? Latest(PollingEngine? engine, HardwareKind kind, SensorRole role)
    {
        if (engine is null) return null;
        foreach (var s in engine.Hardware.Where(n => n.Kind == kind).SelectMany(n => n.Sensors).Where(s => s.Role == role))
        {
            var raw = engine.History.GetRaw(s.Id);
            for (int i = raw.Values.Length - 1; i >= 0; i--) if (!float.IsNaN(raw.Values[i])) return raw.Values[i];
        }
        return null;
    }

    /// <summary>The non-empty evidence strings joined for a result's Detail, or "" when nothing could be measured.</summary>
    public static string Join(params string?[] parts) => string.Join("; ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
