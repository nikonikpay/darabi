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

    /// <summary>The bad-sector health in percent from the raw values of the attributes; null when the drive reports none of the six (nothing to judge).</summary>
    public static int? FromAttributes(IReadOnlyDictionary<int, ulong> raw)
    {
        double health = 100; bool any = false;
        foreach (var (id, (weight, limit)) in Attributes)
            if (raw.TryGetValue(id, out var v)) { any = true; health *= (100 - Math.Min(limit, (v & 0xFFFF) * weight)) / 100; }
        return any ? (int)Math.Round(health, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>Life used, in percent, that makes the drive's health: its own wear counter and the bad-sector health together; null when neither is known.</summary>
    public static int? Wear(int? usedPercent, int? sectorHealth)
    {
        if (usedPercent is null && sectorHealth is null) return null;
        double health = (100 - Math.Clamp(usedPercent ?? 0, 0, 100)) * (sectorHealth ?? 100) / 100.0;
        return Math.Clamp(100 - (int)Math.Round(health, MidpointRounding.AwayFromZero), 0, 100);
    }
}
