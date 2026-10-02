using Mazesta.Core.Hardware; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// A benchmark run against this same machine's earlier runs of it (same settings, newest first): the one comparison that always holds, since other
/// systems differ in more than health and a similar one may never be tested. Their median is the usual result; within 10 % of it is as before
/// (runs differ by a few percent), below that the machine got slower. With no earlier run there is nothing to say, and nothing is said.
/// </summary>
public static class SelfCheck
{
    public const int MaxEarlier = 5;

    public static Finding? Evaluate(string benchmark, HardwareKind part, double mine, IEnumerable<double> earlier, bool higherIsBetter, string unit)
    {
        var e = earlier.Where(v => double.IsFinite(v) && v > 0).Take(MaxEarlier).OrderBy(v => v).ToList();
        if (e.Count == 0 || !double.IsFinite(mine) || mine <= 0) return null;
        double median = e.Count % 2 == 1 ? e[e.Count / 2] : (e[e.Count / 2 - 1] + e[e.Count / 2]) / 2;
        double diff = higherIsBetter ? (mine / median - 1) * 100 : (median / mine - 1) * 100;
        var m = new List<Measure> { M("Check_M_Mine", mine, unit), M("Check_M_Earlier", median, unit), M("Check_M_EarlierRuns", e.Count, None), M("Check_M_Diff", diff, Percent) };
        if (diff >= PeerCheck.Normal) return new(FindingCode.BenchAsBefore, FindingLevel.Good, part, m, benchmark);
        return new(FindingCode.BenchSlowerThanBefore, diff < PeerCheck.Poor ? FindingLevel.Problem : FindingLevel.Attention, part, m, benchmark);
    }
}
