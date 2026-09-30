using System.Text; using Mazesta.Core.Hardware;
namespace Mazesta.Reporting;

/// <summary>The report as plain text (spec §7.4): the same content as the HTML - verdict, tests with their evidence, benchmarks, measured
/// sensors and the machine - for pasting into a message or a service ticket, or opening where no browser is at hand.</summary>
public static class ReportPlainText
{
    public static string Write(SessionReport r, ReportText? wording = null)
    {
        var w = wording ?? ReportText.Persian; var b = new StringBuilder(4096);
        void Heading(string title) => b.AppendLine().AppendLine(title).AppendLine(new string('-', Math.Max(8, title.Length)));

        b.AppendLine($"{r.ShopName} — {w.TitleOf(r.Kind)}");
        b.AppendLine($"{w.Started}: {ReportFormat.Stamp(r.StartedAt)} · {w.Finished}: {ReportFormat.Stamp(r.FinishedAt)} · {w.Duration}: {ReportFormat.Duration(r.DurationSeconds)}");
        b.AppendLine($"{w.ReportId}: {r.Id}");
        if (r.ServiceNumber is { } service) b.AppendLine($"{w.ServiceNumber}: {service}");

        if (r.Verdict is { } verdict)
        {
            Heading(w.Verdict);
            b.AppendLine(w.VerdictName(verdict));
            var c = r.Counts;
            b.AppendLine($"{w.Total}: {c.Total} · {w.Passed}: {c.Passed} · {w.Failed}: {c.Failed} · {w.NotDone}: {c.Cancelled + c.Unsupported + c.NotRun} · {w.Errors}: {c.Errors}");
        }
        else b.AppendLine().AppendLine(w.MeasurementsOnly);

        if (r.Findings is { Count: > 0 })
        {
            Heading(w.Checkup);
            foreach (var f in r.Findings.OrderBy(f => f.Level switch { "Problem" => 0, "Attention" => 1, "Note" => 2, _ => 3 }))
            {
                b.AppendLine($"* [{w.LevelName(f.Level)}] {f.Title}{(string.IsNullOrWhiteSpace(f.Subject) ? "" : " · " + f.Subject)}");
                b.AppendLine("  " + f.Text);
                if (!string.IsNullOrWhiteSpace(f.Hint)) b.AppendLine("  " + f.Hint);
                if (f.Measures.Count > 0) b.AppendLine("  " + string.Join(" · ", f.Measures.Select(m => $"{m.Name}: {m.Value}")));
                if (!string.IsNullOrWhiteSpace(f.Source)) b.AppendLine($"  {w.MakerFigures}: {f.Source}");
            }
        }

        if (r.Tests.Count > 0)
        {
            Heading(w.Results);
            foreach (var t in r.Tests)
            {
                b.AppendLine($"* {t.Name}: {w.OutcomeName(t.Outcome)} · {ReportFormat.Duration(t.DurationSeconds)} · {w.Errors}: {t.ErrorCount}");
                if (t.Options.Count > 0) b.AppendLine($"  {w.Options}: {string.Join(", ", t.Options.Select(o => $"{o.Key}={o.Value}"))}");
                if (!string.IsNullOrWhiteSpace(t.Detail)) b.AppendLine($"  {w.Detail}: {t.Detail}");
                if (!string.IsNullOrWhiteSpace(t.Advice)) b.AppendLine("  " + t.Advice.ReplaceLineEndings(Environment.NewLine + "  "));
            }
        }
        if (r.Benchmarks is { Count: > 0 })
        {
            Heading(w.Benchmarks);
            foreach (var bm in r.Benchmarks)
            {
                b.AppendLine($"* {bm.Name} ({w.MeasuredAt}: {ReportFormat.Stamp(bm.FinishedAt)})");
                foreach (var m in bm.Metrics) b.AppendLine($"  {m.Name}: {Units.FormatMeasured(m.Value, m.Unit)}");
                if (!string.IsNullOrWhiteSpace(bm.Detail)) b.AppendLine($"  {w.Detail}: {bm.Detail}");
            }
        }
        Heading(r.Kind == ReportKind.Benchmark ? w.BenchmarkSensors : w.Sensors);
        if (r.Sensors.Count == 0) b.AppendLine(w.NoSensors);
        foreach (var s in r.Sensors) b.AppendLine($"* {s.Hardware} · {s.Name}: {w.Min} {ReportFormat.Value(s.Min, s)} · {w.Avg} {ReportFormat.Value(s.Average, s)} · {w.Max} {ReportFormat.Value(s.Max, s)} ({w.Samples}: {s.Samples})");

        Heading(w.Machine);
        foreach (var (label, value) in ReportFormat.MachineRows(r.Machine, w)) b.AppendLine(label.Length == 0 ? $"  {value}" : $"* {label}: {value}");
        b.AppendLine().AppendLine(w.Footer).AppendLine($"Mazesta Test {r.AppVersion} · {r.Id}");
        return b.ToString();
    }
}
