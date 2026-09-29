namespace Mazesta.Core.Inventory;

/// <summary>One speed a module can run at, with its main timings in clocks: a JEDEC speed bin, or an XMP profile (which also sets a voltage).</summary>
public sealed record MemoryProfile(string Name, int SpeedMts, int Cl, int Trcd, int Trp, int Tras, int Trc, double? VoltageV);

/// <summary>What a module's own SPD chip says about it. Everything here was read from the module; nothing is looked up or assumed.</summary>
public sealed record SpdModule(
    int Slot, string MemoryType, string? PartNumber, string? ModuleManufacturer, string? DramManufacturer, int? CapacityGb,
    int? Ranks, int? DeviceWidth, int? DieDensityGbit, int? BankGroups, bool? Ecc, string? ManufactureWeek,
    IReadOnlyList<MemoryProfile> Jedec, IReadOnlyList<MemoryProfile> Xmp, string? XmpVersion);

/// <summary>
/// DDR4 SPD (JEDEC 21-C annex L) and Intel XMP 2.0, from the module's 512 bytes. Times are in the medium time base (125 ps) plus a signed
/// fine correction (1 ps); a time becomes clocks as ceil(t / tCK − 0.01), the 1 % guard band the annex prescribes. Checked against a real
/// G.Skill F4-4000C18-32GTZR: XMP 4000 18-22-22-42 tRC 64 at 1.40 V, and JEDEC 2666 19-19-19-43 tRC 61, as HWiNFO reads the same module.
/// </summary>
public static class Ddr4Spd
{
    private const double Mtb = 0.125, Ftb = 0.001;
    /// <summary>DDR4's JEDEC speed bins, fastest first; a module lists every one from its fastest (tCKmin) down to its slowest (tCKmax).</summary>
    private static readonly int[] Bins = [3200, 2933, 2666, 2400, 2133, 1866, 1600];

    public static bool IsDdr4(ReadOnlySpan<byte> spd) => spd.Length >= 256 && spd[2] == 0x0C;

    /// <param name="spd">At least 384 bytes (512 with XMP).</param>
    public static SpdModule Decode(int slot, ReadOnlySpan<byte> spd, string? partNumber = null, string? moduleManufacturer = null, string? dramManufacturer = null)
    {
        if (!IsDdr4(spd)) throw new ArgumentException("Not a DDR4 SPD.", nameof(spd));
        int density = spd[4] & 0x0F; int? dieGbit = density <= 9 ? density switch { 0 => null, 1 => null, 2 => 1, 3 => 2, 4 => 4, 5 => 8, 6 => 16, 7 => 32, _ => null } : null;
        int bankGroups = (spd[4] >> 6) switch { 0 => 0, 1 => 2, 2 => 4, _ => 0 };
        int ranks = ((spd[12] >> 3) & 0x07) + 1, width = 4 << (spd[12] & 0x07);
        int busWidth = 8 << (spd[13] & 0x07); bool ecc = ((spd[13] >> 3) & 0x03) == 1;
        int? capacity = dieGbit is { } g && busWidth > 0 ? g * busWidth / width * ranks / 8 : null;   // GB = die Gbit / 8 × (bus / device width) × ranks
        string? week = spd.Length > 324 && spd[323] != 0 && spd[324] != 0 ? $"20{spd[323]:X2}, week {spd[324]:X2}" : null;

        double tckMin = Time(spd[18], spd[125]), tckMax = Time(spd[19], spd[124]);
        double taa = Time(spd[24], spd[123]), trcd = Time(spd[25], spd[122]), trp = Time(spd[26], spd[121]);
        double tras = ((spd[27] & 0x0F) << 8 | spd[28]) * Mtb, trc = Time((spd[27] >> 4) << 8 | spd[29], spd[120]);
        uint clMask = (uint)(spd[20] | spd[21] << 8 | spd[22] << 16 | (spd[23] & 0x3F) << 24); int clBase = (spd[23] & 0x80) != 0 ? 23 : 7;
        var jedec = new List<MemoryProfile>();
        foreach (int bin in Bins)
        {
            double tck = 2000.0 / bin;
            if (tck < tckMin - 0.0005 || (tckMax > 0 && tck > tckMax + 0.0005)) continue;
            int cl = Clocks(taa, tck); while (cl < clBase + 32 && (clMask & 1u << (cl - clBase)) == 0) cl++;   // the next CAS latency the module lists
            jedec.Add(new($"JEDEC {bin}", bin, cl, Clocks(trcd, tck), Clocks(trp, tck), Clocks(tras, tck), Clocks(trc, tck), 1.2));
        }
        // A module whose fastest speed is not a standard bin (rare) still shows it.
        int fastest = (int)Math.Round(2000 / tckMin);
        if (fastest > 0 && jedec.All(j => j.SpeedMts < fastest - 2))
            jedec.Insert(0, new($"JEDEC {fastest}", fastest, Clocks(taa, tckMin), Clocks(trcd, tckMin), Clocks(trp, tckMin), Clocks(tras, tckMin), Clocks(trc, tckMin), 1.2));

        var (xmp, version) = Xmp(spd);
        return new(slot, "DDR4", partNumber, moduleManufacturer, dramManufacturer, capacity, ranks, width, dieGbit, bankGroups, ecc, week, jedec, xmp, version);
    }

    /// <summary>XMP 2.0: the header 0x0C 0x4A at 384, the enabled profiles at 386, the revision at 387, profile 1 at 393 and profile 2 at 440 (47 bytes
    /// each: voltage, tCK, CAS mask, tAA, tRCD, tRP, the tRAS/tRC upper nibbles, tRAS, tRC … and the fine corrections at +32…+38).</summary>
    private static (IReadOnlyList<MemoryProfile>, string?) Xmp(ReadOnlySpan<byte> spd)
    {
        if (spd.Length < 487 || spd[384] != 0x0C || spd[385] != 0x4A) return ([], null);
        var profiles = new List<MemoryProfile>();
        for (int p = 0; p < 2; p++)
        {
            if ((spd[386] & (1 << p)) == 0) continue;
            int o = p == 0 ? 393 : 440;
            double tck = Time(spd[o + 3], spd[o + 38]);
            if (tck <= 0) continue;
            double taa = Time(spd[o + 8], spd[o + 37]), trcd = Time(spd[o + 9], spd[o + 36]), trp = Time(spd[o + 10], spd[o + 35]);
            double tras = ((spd[o + 11] & 0x0F) << 8 | spd[o + 12]) * Mtb, trc = Time((spd[o + 11] >> 4) << 8 | spd[o + 13], spd[o + 34]);
            double volts = (spd[o] >> 7) + (spd[o] & 0x7F) / 100.0;
            int speed = (int)Math.Round(2000 / tck);
            profiles.Add(new($"XMP {p + 1}", speed, Clocks(taa, tck), Clocks(trcd, tck), Clocks(trp, tck), Clocks(tras, tck), Clocks(trc, tck), volts));
        }
        return (profiles, $"{spd[387] >> 4}.{spd[387] & 0x0F}");
    }

    private static double Time(int mtb, byte fine) => mtb * Mtb + (sbyte)fine * Ftb;
    private static int Clocks(double ns, double tck) => (int)Math.Ceiling(ns / tck - 0.01);

    /// <summary>The profile a module runs at now, judged by speed alone (the timings in use are not read): the XMP profile or JEDEC bin with this
    /// speed, preferring XMP; null when none has it (a speed set by hand).</summary>
    public static MemoryProfile? Matching(SpdModule module, int? configuredMts)
        => configuredMts is not { } mts ? null : module.Xmp.FirstOrDefault(p => Math.Abs(p.SpeedMts - mts) <= 2) ?? module.Jedec.FirstOrDefault(p => Math.Abs(p.SpeedMts - mts) <= 2);
}
