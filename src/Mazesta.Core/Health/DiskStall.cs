namespace Mazesta.Core.Health;

/// <summary>One reading of a physical disk's activity: the share of time it was busy (Task Manager's "Active time") and what it moved per second.</summary>
public readonly record struct DiskActivitySample(double BusyPercent, double BytesPerSec);

/// <summary>A disk that sits at ~100 % active time while moving almost nothing is stuck (a drive or driver that stalls, a failing SSD): every one of
/// the samples, taken a second apart, must be that busy and that idle, so a real copy (busy and fast) or a short burst never counts.</summary>
public static class DiskStall
{
    public const double BusyAtLeast = 95, BelowBytesPerSec = 2 * 1024 * 1024;
    public const int MinSamples = 5;

    public static bool IsStalled(IReadOnlyList<DiskActivitySample> samples)
        => samples.Count >= MinSamples && samples.All(s => s.BusyPercent >= BusyAtLeast && s.BytesPerSec < BelowBytesPerSec);
}
