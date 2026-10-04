using System.Text.RegularExpressions; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.Services;

/// <summary>One line of a test's detail for the page: its text, and whether it is still the executor's own English (shown left to right).</summary>
public sealed record DetailLine(string Text, bool Latin);

/// <summary>
/// A test's detail, as its executor wrote it (one English line, parts joined by "; "), turned into separate lines in the user's language. Only
/// the parts whose form is known are worded again, with the same numbers; a part that is not recognised is kept exactly as written, on a line
/// of its own, so nothing is dropped and nothing is guessed. The report keeps the executor's original line.
/// </summary>
public static partial class TestDetailText
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["GPU load"] = "Detail_L_GpuLoad", ["CPU load"] = "Detail_L_CpuLoad", ["GPU core"] = "Detail_L_GpuCore", ["GPU temperature"] = "Detail_L_GpuCore", ["GPU hot spot"] = "Detail_L_GpuHotSpot",
        ["GPU power"] = "Detail_L_GpuPower", ["CPU package power"] = "Detail_L_CpuPower", ["CPU temperature"] = "Detail_L_CpuTemp",
    };
    private static readonly Dictionary<string, string> Counts = new(StringComparer.Ordinal)
    {
        ["dispatches"] = "Detail_C_Dispatches", ["threads"] = "Detail_C_Threads", ["iterations"] = "Detail_C_Iterations", ["passes"] = "Detail_C_Passes", ["frames"] = "Detail_C_Frames",
    };

    public static IReadOnlyList<DetailLine> Lines(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return [];
        return [.. detail.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Line)];
    }

    private static DetailLine Line(string part)
    {
        if (Stat().Match(part) is { Success: true } s && Labels.TryGetValue(s.Groups[1].Value, out var label))
        {
            string unit = s.Groups[3].Value.Trim();
            return new(s.Groups[4].Success ? Loc.Format("Detail_Stat_Max", Loc.Get(label), s.Groups[2].Value, s.Groups[4].Value, unit, s.Groups[5].Value)
                : Loc.Format("Detail_Stat", Loc.Get(label), s.Groups[2].Value, unit, s.Groups[5].Value), false);
        }
        if (Count().Match(part) is { Success: true } c && Counts.TryGetValue(c.Groups[1].Value, out var count)) return new(Loc.Format(count, c.Groups[2].Value), false);
        if (GpuStress().Match(part) is { Success: true } g) return new(Loc.Format("Detail_GpuStress", Loc.Get("Detail_Profile_" + g.Groups[1].Value), g.Groups[2].Value), false);
        if (Verified().Match(part) is { Success: true } v) return new(Loc.Format("Detail_GpuVerified", v.Groups[1].Value, v.Groups[2].Value), false);
        if (Gops().Match(part) is { Success: true } o) return new(Loc.Format("Detail_Gops", o.Groups[1].Value), false);
        if (FrameRate().Match(part) is { Success: true } f) return new(Loc.Format("Detail_FrameRate", f.Groups[1].Value), false);
        return new(part, true);
    }

    [GeneratedRegex(@"^(?:measured )?([A-Za-z ]+?) avg ([\d.]+)(%|°C| W) (?:max ([\d.]+)(?:%|°C| W) )?\(n=(\d+)\)$")] private static partial Regex Stat();
    [GeneratedRegex(@"^([a-z]+)=([\d,]+)$")] private static partial Regex Count();
    [GeneratedRegex(@"^GPU (Steady|Variable|Pulse) compute stress on (.+)$")] private static partial Regex GpuStress();
    [GeneratedRegex(@"^verified ([\d,]+) of ([\d,]+) thread results through chained dispatches \(a sample, not every thread\)$")] private static partial Regex Verified();
    [GeneratedRegex(@"^([\d,.]+) Gop/s integer$")] private static partial Regex Gops();
    [GeneratedRegex(@"^([\d,.]+) frame/s$")] private static partial Regex FrameRate();
}
