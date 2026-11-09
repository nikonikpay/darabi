namespace Mazesta.Reporting;

/// <summary>Where a report stands in a service job: the test before the work, or the one after it.</summary>
public static class ServiceStages
{
    public const string Before = "Before", After = "After";
    public static string? Clean(string? s) => s == Before || s == After ? s : null;
}

/// <summary>One figure the site lines up against the same figure of the job's other report. Key matches the two sides (t: a test's outcome,
/// p: a part's peak temperature during a test or benchmark, b: a benchmark figure); Higher says which way is better, null where that is not known.</summary>
public sealed record ServiceFigure(string Key, string Name, double? Value, string? Unit, bool? Higher, string? Outcome);

public static class ServiceFigures
{
    /// <summary>What the site compares between the before and the after report: each test's outcome, each part's peak temperature while each
    /// test and benchmark ran, and the benchmarks' main figures. Only measured values; a part with no reading is left out.
    /// <paramref name="headline"/>: a benchmark's headline metric key and whether higher is better, null when it has none.</summary>
    public static IReadOnlyList<ServiceFigure> Of(SessionReport r, Func<string, (string Key, bool Higher)?> headline, SummaryText wording)
    {
        var list = new List<ServiceFigure>();
        foreach (var t in r.Tests.Where(t => t.Outcome != ReportOutcome.NotRun)) list.Add(new("t:" + t.Id, t.Name, null, null, null, t.Outcome.ToString()));
        foreach (var row in ReportSummary.Of(r).Rows)
            foreach (var (part, max) in row.Peaks.OrderBy(p => p.Key))
                list.Add(new($"p:{row.Id}:{part}", $"{SummaryHtml.Short(row.Name)} · {wording.PartName(part)}", Math.Round(max, 1), "°C", false, null));
        foreach (var b in r.Benchmarks ?? [])
        {
            var head = headline(b.Id);
            foreach (var m in b.Metrics.Where(m => m.Key is not null && (ReportSummary.MainFigures.Contains(m.Key) || m.Key == head?.Key)))
                list.Add(new($"b:{b.Id}:{m.Key}", $"{b.Name} · {m.Name}", m.Value, m.Unit, head is { } h && m.Key == h.Key ? h.Higher : null, null));
        }
        return list.GroupBy(f => f.Key).Select(g => g.Last()).ToList();   // a benchmark run twice: its last run
    }
}
