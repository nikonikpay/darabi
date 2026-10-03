namespace Mazesta.Core.Inventory;

/// <summary>
/// DDR5 SPD (JEDEC JESD400-5) and Intel XMP 3.0, from the module's 1024 bytes. DDR5 writes its times in picoseconds, 16 bits each, low byte
/// first; a time becomes clocks as trunc((t × 997 / tCK + 1000) / 1000), the rounding the standard prescribes, and a CAS latency is always even.
/// The XMP offsets (header 0x0C 0x4A at 640, profiles of 64 bytes from 704: tCK at +5, tAA +13, tRCD +15, tRP +17, tRAS +19, tRC +21) are the
/// ones memtest86+ reads. Not checked on a real module yet (the owner's machines are DDR4), so what cannot be cross-checked is left out instead
/// of shown: a profile's voltage only when its VPP byte decodes to a DDR5 VPP, AMD EXPO profiles not at all.
/// </summary>
public static class Ddr5Spd
{
    /// <summary>DDR5's JEDEC speed bins with their nominal cycle times in picoseconds, fastest first.</summary>
    private static readonly (int Mts, int Tck)[] Bins =
    [
        (8800, 227), (8400, 238), (8000, 250), (7600, 263), (7200, 277), (6800, 294), (6400, 312), (6000, 333),
        (5600, 357), (5200, 384), (4800, 416), (4400, 454), (4000, 500), (3600, 555), (3200, 625),
    ];

    public static bool IsDdr5(ReadOnlySpan<byte> spd) => spd.Length >= 1024 && spd[2] == 0x12;

    /// <param name="spd">1024 bytes; only 0-63, 234-235 and 640-895 are read.</param>
    /// <param name="capacityGb">The module's size as its reader worked it out (asymmetric modules make it more than one multiplication).</param>
    public static SpdModule Decode(int slot, ReadOnlySpan<byte> spd, string? partNumber = null, string? moduleManufacturer = null, string? dramManufacturer = null,
        int? capacityGb = null, string? manufactureWeek = null)
    {
        if (!IsDdr5(spd)) throw new ArgumentException("Not a DDR5 SPD.", nameof(spd));
        int? dieGbit = (spd[4] & 0x1F) switch { 1 => 4, 2 => 8, 3 => 12, 4 => 16, 5 => 24, 6 => 32, 7 => 48, 8 => 64, _ => null };
        int? width = (spd[6] >> 5) switch { 0 => 4, 1 => 8, 2 => 16, 3 => 32, _ => null };
        int? bankGroups = (spd[7] >> 5) switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => null };
        int ranks = ((spd[234] >> 3) & 0x07) + 1;
        bool? ecc = ((spd[235] >> 3) & 0x03) switch { 0 => false, 1 or 2 => true, _ => null };

        int tckMin = Word(spd, 20), tckMax = Word(spd, 22);
        int taa = Word(spd, 30), trcd = Word(spd, 32), trp = Word(spd, 34), tras = Word(spd, 36), trc = Word(spd, 38);
        ulong clMask = 0; for (int i = 0; i < 5; i++) clMask |= (ulong)spd[24 + i] << (8 * i);   // bit i is CL 20 + 2i
        var jedec = new List<MemoryProfile>();
        if (Plausible(tckMin) && taa > 0 && trcd > 0 && trp > 0 && tras > 0 && trc > 0)
        {
            foreach (var (mts, tck) in Bins)
            {
                if (tck < tckMin - 1 || (tckMax > 0 && tck > tckMax + 1)) continue;
                jedec.Add(new($"JEDEC {mts}", mts, Cas(taa, tck, clMask), Clocks(trcd, tck), Clocks(trp, tck), Clocks(tras, tck), Clocks(trc, tck), 1.1));
            }
            // A module whose fastest speed is not a standard bin still shows it.
            int fastest = Speed(tckMin);
            if (jedec.All(j => j.SpeedMts < fastest))
                jedec.Insert(0, new($"JEDEC {fastest}", fastest, Cas(taa, tckMin, clMask), Clocks(trcd, tckMin), Clocks(trp, tckMin), Clocks(tras, tckMin), Clocks(trc, tckMin), 1.1));
        }

        var (xmp, version) = Xmp(spd);
        return new(slot, "DDR5", partNumber, moduleManufacturer, dramManufacturer, capacityGb, ranks, width, dieGbit, bankGroups, ecc, manufactureWeek, jedec, xmp, version);
    }

    /// <summary>The three profiles the module's maker wrote (the two the user may write are not a rating and are left out). The third one's place
    /// is where AMD EXPO starts on modules that carry it ("EXPO" at 832); then it is not an XMP profile.</summary>
    private static (IReadOnlyList<MemoryProfile>, string?) Xmp(ReadOnlySpan<byte> spd)
    {
        if (spd[640] != 0x0C || spd[641] != 0x4A) return ([], null);
        bool expo = spd[832] == 'E' && spd[833] == 'X' && spd[834] == 'P' && spd[835] == 'O';
        var profiles = new List<MemoryProfile>();
        for (int p = 0; p < (expo ? 2 : 3); p++)
        {
            int o = 704 + p * 64, tck = Word(spd, o + 5);
            int taa = Word(spd, o + 13), trcd = Word(spd, o + 15), trp = Word(spd, o + 17), tras = Word(spd, o + 19), trc = Word(spd, o + 21);
            if (!Plausible(tck) || taa <= 0 || trcd <= 0 || trp <= 0 || tras <= 0 || trc <= 0) continue;
            int cl = Clocks(taa, tck); cl += cl % 2;
            double vpp = Volts(spd[o]), vdd = Volts(spd[o + 1]);
            double? volts = vpp is >= 1.5 and <= 2.2 && vdd is >= 1.0 and <= 1.7 ? vdd : null;
            profiles.Add(new($"XMP {p + 1}", Speed(tck), cl, Clocks(trcd, tck), Clocks(trp, tck), Clocks(tras, tck), Clocks(trc, tck), volts));
        }
        return (profiles, spd[642] >> 4 == 3 ? $"{spd[642] >> 4}.{spd[642] & 0x0F}" : null);
    }

    private static int Word(ReadOnlySpan<byte> spd, int at) => spd[at] | spd[at + 1] << 8;
    /// <summary>A cycle time DDR5 can have: 200 ps (10000 MT/s) to 1010 ps, the slowest clock the standard allows.</summary>
    private static bool Plausible(int tck) => tck is >= 200 and <= 1010;
    private static int Clocks(int ps, int tck) => (int)((ps * 997L / tck + 1000) / 1000);
    /// <summary>The CAS latency at this clock: even, and the next one the module lists when the computed one is not among them.</summary>
    private static int Cas(int taa, int tck, ulong mask)
    {
        int cl = Clocks(taa, tck); cl += cl % 2;
        if (mask == 0 || cl < 20) return cl;
        for (int c = cl; c <= 98; c += 2) if ((mask & 1UL << ((c - 20) / 2)) != 0) return c;
        return cl;
    }
    private static int Speed(int tck) => (int)Math.Round(2_000_000.0 / tck / 100) * 100;
    /// <summary>XMP 3.0's voltage byte: whole volts in bits 6-5, 50 mV steps in bits 4-0.</summary>
    private static double Volts(byte b) => ((b >> 5) & 0x03) + (b & 0x1F) * 0.05;
}
