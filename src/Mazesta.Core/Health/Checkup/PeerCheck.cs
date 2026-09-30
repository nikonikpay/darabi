using Mazesta.Core.Hardware; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>What a run measured on the side, by kind, to explain a gap to other systems: <see cref="PowerW"/> the part's average power,
/// <see cref="TempMaxC"/> its hottest reading, <see cref="ClockMhz"/> its clock under load. Null where it was not measured.</summary>
public sealed record RunConditions(double? PowerW, double? TempMaxC, double? ClockMhz);

/// <summary>This run against the other systems with the same part model: <see cref="Median"/> is their usual result (each system counted once,
/// by its best run) over <see cref="Systems"/> systems, and <see cref="Theirs"/> the conditions of the run nearest that median.</summary>
public sealed record PeerStanding(string Benchmark, HardwareKind Part, double Mine, double Median, int Systems, bool HigherIsBetter, string Unit, RunConditions Ours, RunConditions? Theirs);

/// <summary>
/// The one comparison most users cannot make themselves: is this result normal for this model? Within 10 % of the model's usual result is normal
/// (runs of one machine differ by a few percent); below that the gap is said, with what the conditions point to. Fewer than three systems is
/// too few to call anything normal, and is said so.
/// </summary>
public static class PeerCheck
{
    public const int MinSystems = 3;
    public const double Normal = -10, Poor = -20;

    public static Finding Evaluate(PeerStanding s)
    {
        double diff = s.HigherIsBetter ? (s.Mine / s.Median - 1) * 100 : (s.Median / s.Mine - 1) * 100;
        var m = new List<Measure> { M("Check_M_Mine", s.Mine, s.Unit), M("Check_M_Median", s.Median, s.Unit), M("Check_M_Systems", s.Systems, None) };
        if (s.Systems < MinSystems) return new(FindingCode.BenchFewPeers, FindingLevel.Note, s.Part, m, s.Benchmark);
        m.Add(M("Check_M_Diff", diff, Percent));
        if (diff >= Normal) return new(FindingCode.BenchWithPeers, FindingLevel.Good, s.Part, m, s.Benchmark);

        var hint = FindingHint.None; var level = diff < Poor ? FindingLevel.Problem : FindingLevel.Attention;
        if (s.Theirs is { } t)
        {
            bool hotter = s.Ours.TempMaxC is { } a && t.TempMaxC is { } b && a >= b + 8;
            if (s.Ours.PowerW is { } p && t.PowerW is { } q && q > 0 && p <= q * 0.85 && !hotter)
            {
                // Less power without more heat is a limit someone set (a BIOS profile, a laptop's), not a failing part.
                hint = FindingHint.LessPowerThanPeers; level = diff < Poor ? FindingLevel.Attention : FindingLevel.Note;
                m.AddRange([M("Check_M_PowerOurs", p, Watt), M("Check_M_PowerTheirs", q, Watt)]);
            }
            else if (hotter) { hint = FindingHint.HotterThanPeers; m.AddRange([M("Check_M_TempOurs", s.Ours.TempMaxC!.Value, Celsius), M("Check_M_TempTheirs", t.TempMaxC!.Value, Celsius)]); }
            else if (s.Ours.ClockMhz is { } c && t.ClockMhz is { } d && d > 0 && c <= d * 0.93) { hint = FindingHint.LowerClockThanPeers; m.AddRange([M("Check_M_ClockOurs", c, MHz), M("Check_M_ClockTheirs", d, MHz)]); }
        }
        return new(FindingCode.BenchBelowPeers, level, s.Part, m, s.Benchmark, hint);
    }
}
