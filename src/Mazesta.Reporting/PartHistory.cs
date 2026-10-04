namespace Mazesta.Reporting;

/// <summary>One test or benchmark of a part as a saved report holds it: a test's outcome, or a benchmark's main figures (<see cref="ReportSummary.MaxFigures"/> at most).</summary>
public sealed record PartTestLine(string Name, ReportOutcome? Outcome, IReadOnlyList<SummaryFigure> Figures);

/// <summary>What one saved report recorded about a part: its tests and benchmarks and the highest temperature the part reached while they ran
/// (null when the report has no reading for it).</summary>
public sealed record PartTestSummary(DateTimeOffset At, ReportKind Kind, IReadOnlyList<PartTestLine> Lines, double? MaxTempC);

/// <summary>The recorded tests of one part of the computer, for answering "what is my RAM / graphics card / drive" with what was last measured on it.
/// Made from the saved reports alone; a part no report has run anything on has no summary - never a guessed one.</summary>
public static class PartHistory
{
    /// <summary>The test-id and benchmark-id prefixes of each part, and the temperature that is the part's own.</summary>
    private static readonly Dictionary<string, (string[] Tests, string[] Benchmarks, HeatPart? Heat)> Parts = new()
    {
        ["cpu"] = (["cpu."], ["bench.cpu."], HeatPart.Cpu),
        ["ram"] = (["memory."], ["bench.memory"], null),
        ["gpu"] = (["gpu."], ["bench.gpu."], HeatPart.Gpu),
        ["vram"] = (["gpu.vram"], ["bench.gpu."], HeatPart.GpuMemory),
        ["storage"] = (["storage."], ["bench.storage"], HeatPart.Drive),
        ["network"] = (["network."], ["bench.network"], null),
    };

    public static bool Knows(string part) => Parts.ContainsKey(part);

    /// <summary>This report's tests and benchmarks of the part, or null when it has none (or the part is not one that is tested).</summary>
    public static PartTestSummary? Of(string part, SessionReport r)
    {
        if (!Parts.TryGetValue(part, out var p)) return null;
        var lines = r.Tests.Where(t => t.Outcome != ReportOutcome.NotRun && p.Tests.Any(x => t.Id.StartsWith(x, StringComparison.Ordinal)))
            .Select(t => new PartTestLine(t.Name, t.Outcome, [])).ToList();
        var benches = (r.Benchmarks ?? []).Where(b => p.Benchmarks.Any(x => b.Id.StartsWith(x, StringComparison.Ordinal))).ToList();
        lines.AddRange(benches.Select(b => new PartTestLine(b.Name, null, ReportSummary.FiguresOf(b))));
        if (lines.Count == 0) return null;
        double? max = null;
        if (p.Heat is { } heat)
        {
            var names = lines.Select(l => l.Name).ToHashSet();
            max = ReportSummary.Of(r).Rows.Where(x => names.Contains(x.Name) && x.Peaks.ContainsKey(heat)).Select(x => (double?)x.Peaks[heat]).Max();
        }
        return new(r.CreatedAt, r.Kind, lines, max);
    }

    /// <summary>The newest of the reports (newest first) that has tests or benchmarks of the part.</summary>
    public static PartTestSummary? Latest(string part, IEnumerable<SessionReport> newestFirst) => newestFirst.Select(r => Of(part, r)).FirstOrDefault(s => s is not null);
}
