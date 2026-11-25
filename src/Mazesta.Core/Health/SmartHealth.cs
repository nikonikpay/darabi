namespace Mazesta.Core.Health;

/// <summary>
/// A drive's health the way Hard Disk Sentinel's default method counts it, so a technician can set the two side by side: 100 % less what the
/// drive's own wear counter has used (an SSD), times, for each health-critical SMART attribute, (100 − min(limit, weight × raw count)) %.
/// The count is the low 16 bits of the raw value; the weights and limits are Hard Disk Sentinel's published ones.
/// </summary>
public static class SmartHealth
{
    private static readonly Dictionary<int, (double Weight, double Limit)> Attributes = new()
    {
        [5] = (1, 70),     // reallocated sectors
        [7] = (0.5, 20),   // seek error rate
        [10] = (3, 60),    // spin retry
        [196] = (0.6, 30), // reallocation events
        [197] = (0.6, 48), // pending sectors
        [198] = (1, 70),   // offline uncorrectable sectors
    };

    /// <summary>The ATA SMART attributes of a WMI <c>MSStorageDriver_ATAPISmartData.VendorSpecific</c> blob (2-byte header, 12-byte entries): id → raw value.</summary>
    public static IReadOnlyDictionary<int, ulong> Parse(ReadOnlySpan<byte> blob)
    {
        var raw = new Dictionary<int, ulong>();
        for (int i = 2; i + 12 <= blob.Length; i += 12)
        {
            if (blob[i] == 0) continue;
            ulong v = 0; for (int b = 5; b >= 0; b--) v = v << 8 | blob[i + 5 + b];
            raw[blob[i]] = v;
        }
        return raw;
    }

    /// <summary>Each attribute in full: its normalized current and worst values (byte 3 and 4 of the entry), the raw value, and the threshold the drive
    /// set for it (from <c>MSStorageDriver_FailurePredictThresholds.VendorSpecific</c>: 2-byte header, 12-byte entries of id then threshold) when Windows gave it.</summary>
    public static IReadOnlyList<SmartAttribute> ReadAttributes(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> thresholds = default)
    {
        var limits = new Dictionary<int, byte>();
        for (int i = 2; i + 12 <= thresholds.Length; i += 12) if (thresholds[i] != 0) limits[thresholds[i]] = thresholds[i + 1];
        var list = new List<SmartAttribute>();
        for (int i = 2; i + 12 <= blob.Length; i += 12)
        {
            if (blob[i] == 0) continue;
            ulong v = 0; for (int b = 5; b >= 0; b--) v = v << 8 | blob[i + 5 + b];
            list.Add(new(blob[i], blob[i + 3], blob[i + 4], v, limits.TryGetValue(blob[i], out var t) ? t : null));
        }
        return list;
    }

    /// <summary>The bad-sector health in percent from the raw values of the attributes; null when the drive reports none of the six (nothing to judge).</summary>
    public static int? FromAttributes(IReadOnlyDictionary<int, ulong> raw)
    {
        double health = 100; bool any = false;
        foreach (var (id, (weight, limit)) in Attributes)
            if (raw.TryGetValue(id, out var v)) { any = true; health *= (100 - Math.Min(limit, (v & 0xFFFF) * weight)) / 100; }
        return any ? (int)Math.Round(health, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>Sectors the drive has replaced (5), cannot read and is waiting to replace (197) or has given up on (198), added up; null when it
    /// reports none of the three. On a hard disk any of them is a surface that has started to fail, long before the health figure falls far.</summary>
    public static long? BadSectors(IReadOnlyDictionary<int, ulong> raw)
    {
        long? sum = null;
        foreach (int id in (int[])[5, 197, 198]) if (raw.TryGetValue(id, out var v)) sum = (sum ?? 0) + (long)(v & 0xFFFF);
        return sum;
    }

    /// <summary>Life used, in percent, that makes the drive's health: its own wear counter and the bad-sector health together; null when neither is known.</summary>
    public static int? Wear(int? usedPercent, int? sectorHealth)
    {
        if (usedPercent is null && sectorHealth is null) return null;
        double health = (100 - Math.Clamp(usedPercent ?? 0, 0, 100)) * (sectorHealth ?? 100) / 100.0;
        return Math.Clamp(100 - (int)Math.Round(health, MidpointRounding.AwayFromZero), 0, 100);
    }
}

/// <summary>One ATA SMART attribute as the drive reports it. A threshold of 0 means the drive never fails on it; one Windows did not give is null.</summary>
public sealed record SmartAttribute(int Id, byte Current, byte Worst, ulong Raw, byte? Threshold)
{
    /// <summary>The drive's own failure test: its normalized value has fallen to the threshold it set for it.</summary>
    public bool Failing => Threshold is > 0 and var t && Current is > 0 && Current <= t;
}
