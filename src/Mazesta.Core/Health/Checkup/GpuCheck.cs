using Mazesta.Core.Hardware; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>How often, over the samples taken while the card was busy, NVIDIA's driver said why the clock was held (its "clock event reasons"),
/// with the limits it reports for this card. Other makers' cards have no such report: null.</summary>
public sealed record GpuThrottleCounts(int Samples, int PowerCap, int SwThermal, int HwSlowdown, int HwThermal, int PowerBrake, int? SlowdownTempC, int? PowerLimitW);

/// <summary>The card's PCI Express link: as the slot's port negotiated it (<see cref="Gen"/> and <see cref="Width"/> the most seen under load), what
/// the card can do, and what the slot can do. <see cref="LiveGen"/> is true when the generation was read under load (NVML); a link read otherwise
/// may sit in a lower generation to save power, so its generation is not judged.</summary>
public sealed record GpuLink(int? Gen, int? Width, int? CardGen, int? CardWidth, int? SlotGen, int? SlotWidth, bool LiveGen);

/// <summary>What was measured on one card while a GPU benchmark ran.</summary>
public sealed record GpuRunTrace(string? Name, HardwareVendor Vendor, Series? Load, Series? CoreTemp, Series? HotSpot, Series? Power, GpuThrottleCounts? Throttle, GpuLink? Link);

/// <summary>
/// Reads a GPU run: what the driver itself said held the clock (NVIDIA), the gap between the hottest spot and the core, and whether the card's
/// link is as wide and as fast as both the card and the slot allow. A power cap at full load is how cards are built to run and is only noted; a
/// hardware slowdown or an external power brake is a fault.
/// </summary>
public static class GpuCheck
{
    /// <summary>A hot spot this far above the core under load usually means dried paste or a cooler not pressing evenly. NVIDIA cards run about
    /// 10-20 °C apart; AMD's larger dies run wider, so they are held to a looser line.</summary>
    public const double HotspotGapNvidia = 25, HotspotGapOther = 30;

    public static IReadOnlyList<Finding> Evaluate(GpuRunTrace run)
    {
        var found = new List<Finding>();
        void Add(FindingCode code, FindingLevel level, IReadOnlyList<Measure> m) => found.Add(new(code, level, HardwareKind.Gpu, m, run.Name));
        var loaded = Loaded(run);
        double? tempMax = loaded(run.CoreTemp) is { Count: > 0 } t ? t.Max() : null;

        if (run.Throttle is { Samples: >= 5 } r)
        {
            double Share(int n) => (double)n / r.Samples;
            Measure Pct(int n) => M("Check_M_TimeShare", Share(n) * 100, Percent);
            Measure[] temps = [.. tempMax is { } tm ? [M("Check_M_TempMax", tm, Celsius)] : Array.Empty<Measure>(), .. r.SlowdownTempC is { } sd ? [M("Check_M_SlowdownTemp", sd, Celsius)] : Array.Empty<Measure>()];
            bool hw = false;
            if (Share(r.PowerBrake) >= 0.02) { Add(FindingCode.GpuPowerBrake, FindingLevel.Problem, [Pct(r.PowerBrake)]); hw = true; }
            if (Share(r.HwThermal) >= 0.02) { Add(FindingCode.GpuHwThermal, FindingLevel.Problem, [Pct(r.HwThermal), .. temps]); hw = true; }
            // The driver may raise the general slowdown flag for a moment while it changes power states: only a lasting one counts.
            if (!hw && Share(r.HwSlowdown) >= 0.05) Add(FindingCode.GpuHwSlowdown, FindingLevel.Problem, [Pct(r.HwSlowdown)]);
            bool hot = Share(r.SwThermal) >= 0.1;
            if (hot) Add(FindingCode.GpuThermalSlowdown, FindingLevel.Attention, [Pct(r.SwThermal), .. temps]);
            if (Share(r.PowerCap) >= 0.5)
                Add(FindingCode.GpuPowerLimited, FindingLevel.Good, [Pct(r.PowerCap), .. r.PowerLimitW is { } pl ? [M("Check_M_PowerLimit", pl, Watt)] : Array.Empty<Measure>(),
                    .. loaded(run.Power) is { Count: > 0 } p ? [M("Check_M_Power", p.Median(), Watt)] : Array.Empty<Measure>()]);
            if (!hot && Share(r.HwThermal) < 0.02 && tempMax is { } tmax && r.SlowdownTempC is { } slow) Add(FindingCode.GpuHeatOk, FindingLevel.Good, [M("Check_M_TempMax", tmax, Celsius), M("Check_M_SlowdownTemp", slow, Celsius)]);
        }

        if (loaded(run.CoreTemp) is { Count: >= 5 } core && loaded(run.HotSpot) is { Count: >= 5 } spot)
        {
            // Paired by the second they were read in, so a gap is never a hot spot of one moment against a core of another.
            var gaps = spot.T.Select((s, i) => (s, v: spot.V[i])).Join(core.T.Select((s, i) => (s, v: core.V[i])), a => a.s, b => b.s, (a, b) => a.v - b.v).ToList();
            if (gaps.Count >= 5)
            {
                double gap = new Series([.. Enumerable.Range(0, gaps.Count).Select(i => (double)i)], gaps).Median();
                if (gap >= (run.Vendor == HardwareVendor.Nvidia ? HotspotGapNvidia : HotspotGapOther))
                    Add(FindingCode.GpuHotspotGap, FindingLevel.Attention, [M("Check_M_HotspotGap", gap, Celsius), M("Check_M_TempMax", core.Max(), Celsius), M("Check_M_HotspotMax", spot.Max(), Celsius)]);
            }
        }

        if (run.Link is { } link) Link(link, Add);
        return found;
    }

    private static void Link(GpuLink l, Action<FindingCode, FindingLevel, IReadOnlyList<Measure>> add)
    {
        bool fine = true;
        if (l.Width is { } w && l.CardWidth is { } cw)
        {
            int expect = Math.Min(cw, l.SlotWidth ?? cw);
            // With the slot unknown (a port Windows treats as plain PCI) a narrower link may be all the slot has: many APUs give the graphics slot x8.
            if (w < expect && l.SlotWidth is null) { add(FindingCode.GpuLinkBelowCard, FindingLevel.Note, [M("Check_M_LinkWidth", w, Lanes), M("Check_M_CardWidth", cw, Lanes)]); fine = false; }
            else if (w < expect) { add(FindingCode.GpuLinkNarrow, FindingLevel.Attention, [M("Check_M_LinkWidth", w, Lanes), M("Check_M_LinkWidthExpected", expect, Lanes)]); fine = false; }
            else if (l.SlotWidth is { } sw && sw < cw) { add(FindingCode.GpuSlotNarrow, FindingLevel.Note, [M("Check_M_SlotWidth", sw, Lanes), M("Check_M_CardWidth", cw, Lanes)]); fine = false; }
        }
        if (l.LiveGen && l.Gen is { } g && l.CardGen is { } cg)
        {
            int expect = Math.Min(cg, l.SlotGen ?? cg);
            if (g < expect) { add(FindingCode.GpuLinkSlowGen, FindingLevel.Attention, [M("Check_M_LinkGen", g, None), M("Check_M_LinkGenExpected", expect, None)]); fine = false; }
            else if (l.SlotGen is { } sg && sg < cg) { add(FindingCode.GpuSlotOlderGen, FindingLevel.Note, [M("Check_M_SlotGen", sg, None), M("Check_M_CardGen", cg, None)]); fine = false; }
        }
        if (fine && l.Width is { } width && l.CardWidth is not null)
            add(FindingCode.GpuLinkOk, FindingLevel.Good, [.. l.LiveGen && l.Gen is { } gen ? [M("Check_M_LinkGen", gen, None)] : Array.Empty<Measure>(), M("Check_M_LinkWidth", width, Lanes)]);
    }

    /// <summary>A reading narrowed to the seconds the card was busy (load 80 % or more); with no load reading, the whole run.</summary>
    private static Func<Series?, Series?> Loaded(GpuRunTrace run)
    {
        if (run.Load is not { Count: > 0 } load) return s => s;
        var busy = load.T.Where((_, i) => load.V[i] >= 80).ToHashSet();
        return s => s is null ? null : Series.Of(s.T.Select((t, i) => (t, s.V[i])).Where(x => busy.Contains(x.t)));
    }
}
