using System.Globalization; using System.Text; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>A font to embed so the file looks the same on any machine (regular and bold TTF bytes).</summary>
public sealed record ReportFont(string Family, byte[] Regular, byte[] Bold);

/// <summary>The complete human report: one self-contained, offline HTML file (no scripts, no external requests) that is also what the PDF is printed from.</summary>
public static class ReportHtml
{
    private static readonly CultureInfo Inv = ReportFormat.Inv;
    // Not WebUtility.HtmlEncode: it turns every non-ASCII character (all of the Persian text) into a numeric entity.
    private static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static string Lt(string? s) => $"<bdi class=\"lt\">{E(s)}</bdi>";

    public static string Write(SessionReport r, ReportFont? font = null, ReportText? wording = null)
    {
        var w = wording ?? ReportText.Persian;
        var b = new StringBuilder(32 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"").Append(w.Language).Append("\" dir=\"").Append(w.IsRtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(w.TitleOf(r.Kind))).Append(" — ").Append(E(r.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main>");
        Header(b, r, w); Summary(b, r, w);
        Tests(b, r, w);
        if (r.Kind == ReportKind.Benchmark) { Benchmarks(b, r, w); Sensors(b, r, w); } else { Sensors(b, r, w); Benchmarks(b, r, w); }   // the report's subject first
        Machine(b, r.Machine, w);
        b.Append("<footer>").Append(E(w.Footer)).Append("<br>").Append(Lt($"Mazesta Test {r.AppVersion} · {r.Id}")).Append("</footer></main></body></html>");
        return b.ToString();
    }

    /// <summary>The before/after page (spec 4.4) for two reports of the same machine: each test's outcome on both sides, each sensor's average and
    /// maximum with the change, and each benchmark number with the change. A value one side did not measure is written as such, never as 0,
    /// and changes are shown with their sign only - whether higher is better depends on the metric, so nothing is coloured as good or bad.</summary>
    public static string WriteComparison(SessionReport before, SessionReport after, ReportComparison c, ReportFont? font = null, ReportText? wording = null)
    {
        var w = wording ?? ReportText.Persian; var b = new StringBuilder(16 * 1024);
        string N(double? v, string unit) => v is { } x ? Lt(Units.FormatMeasured(x, unit, 1)) : E(w.NotMeasured);
        string D(double? v, string unit) => v is { } x ? Lt((x > 0 ? "+" : "") + Units.FormatMeasured(x, unit, 1)) : "";
        string O(ReportOutcome? o) => o is { } x ? $"<span class=\"badge {x}\">{E(w.OutcomeName(x))}</span>" : E(w.NotMeasured);
        void Head(params string[] columns) { b.Append("<table><thead><tr>"); foreach (var col in columns) b.Append("<th>").Append(E(col)).Append("</th>"); b.Append("</tr></thead><tbody>"); }

        b.Append("<!DOCTYPE html><html lang=\"").Append(w.Language).Append("\" dir=\"").Append(w.IsRtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(w.CompareTitle)).Append(" — ").Append(E(after.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main>")
         .Append("<header>").Append(MazestaLogo.Svg("#201E1E", "logo")).Append("<h1>").Append(E(after.ShopName)).Append(" — ").Append(E(w.CompareTitle)).Append("</h1><div class=\"meta\">")
         .Append(after.ServiceNumber is { } service ? $"<b>{w.ServiceNumber}: {Lt(service)}</b><br>" : "")
         .Append(w.Before).Append(": ").Append(Lt(ReportFormat.Stamp(before.StartedAt) + " · " + before.Id)).Append("<br>")
         .Append(w.After).Append(": ").Append(Lt(ReportFormat.Stamp(after.StartedAt) + " · " + after.Id)).Append("</div></header>");
        if (c.Tests.Count > 0)
        {
            b.Append("<h2>").Append(w.Results).Append("</h2>"); Head(w.Name, w.Before, w.After);
            foreach (var t in c.Tests) b.Append("<tr><td>").Append(E(t.Name)).Append("</td><td>").Append(O(t.Before)).Append("</td><td>").Append(O(t.After)).Append("</td></tr>");
            b.Append("</tbody></table>");
        }
        if (c.Sensors.Count > 0)
        {
            b.Append("<h2>").Append(w.Sensors).Append("</h2>"); Head(w.Sensor, $"{w.Avg} · {w.Before}", $"{w.Avg} · {w.After}", w.Change, $"{w.Max} · {w.Before}", $"{w.Max} · {w.After}", w.Change);
            foreach (var s in c.Sensors)
                b.Append("<tr><td>").Append(Lt(s.Name)).Append("</td><td>").Append(N(s.AverageBefore, s.Unit)).Append("</td><td>").Append(N(s.AverageAfter, s.Unit)).Append("</td><td>").Append(D(s.AverageDelta, s.Unit))
                 .Append("</td><td>").Append(N(s.MaxBefore, s.Unit)).Append("</td><td>").Append(N(s.MaxAfter, s.Unit)).Append("</td><td>").Append(D(s.MaxDelta, s.Unit)).Append("</td></tr>");
            b.Append("</tbody></table>");
        }
        if (c.Benchmarks.Count > 0)
        {
            b.Append("<h2>").Append(w.Benchmarks).Append("</h2>"); Head(w.Benchmarks, w.Metric, w.Before, w.After, w.Change);
            foreach (var m in c.Benchmarks)
            {
                string percent = m.Delta is { } d && m.Before is { } before0 && before0 != 0 ? $" ({(d > 0 ? "+" : "")}{(d / before0 * 100).ToString("F1", Inv)}%)" : "";
                b.Append("<tr><td>").Append(E(m.Benchmark)).Append("</td><td>").Append(E(m.Metric)).Append("</td><td>").Append(N(m.Before, m.Unit)).Append("</td><td>").Append(N(m.After, m.Unit))
                 .Append("</td><td>").Append(D(m.Delta, m.Unit)).Append(percent.Length > 0 ? Lt(percent) : "").Append("</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        Machine(b, after.Machine, w);
        b.Append("<footer>").Append(E(w.Footer)).Append("</footer></main></body></html>");
        return b.ToString();
    }

    private static string Css(ReportFont? f)
    {
        string face = f is null ? "" :
            $"@font-face{{font-family:'{f.Family}';font-weight:400;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Regular)}) format('truetype')}}" +
            $"@font-face{{font-family:'{f.Family}';font-weight:700;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Bold)}) format('truetype')}}";
        string family = f is null ? "Tahoma,'Segoe UI',sans-serif" : $"'{f.Family}',Tahoma,'Segoe UI',sans-serif";
        return face + $@"
@page{{size:A4;margin:14mm}}
*{{box-sizing:border-box}}
body{{margin:0;background:#eef1f5;color:#1b2430;font:14px/1.75 {family}}}
main{{max-width:900px;margin:0 auto;background:#fff;padding:32px 36px}}
.lt{{font-family:'Segoe UI',Consolas,monospace;direction:ltr;unicode-bidi:isolate;font-size:.92em}}
header{{border-bottom:3px solid #FDD400;padding-bottom:14px;margin-bottom:18px}}
.logo{{display:block;height:34px;width:auto;margin-bottom:10px}}
h1{{margin:0;font-size:24px}} h2{{margin:26px 0 10px;font-size:18px;border-inline-start:4px solid #FDD400;padding-inline-start:10px}}
.meta{{color:#5b6675;font-size:12.5px;margin-top:4px}}
.verdict{{border-radius:10px;padding:14px 18px;font-size:17px;font-weight:700;margin:6px 0 14px}}
.verdict.Passed{{background:#e7f6ec;color:#146c2e;border:1px solid #9bd5ad}}
.verdict.Failed{{background:#fdecec;color:#a11a1a;border:1px solid #f0a8a8}}
.verdict.Incomplete{{background:#fff4dc;color:#8a5a00;border:1px solid #f0cf8a}}
.verdict.Benchmark{{background:#e8f0ff;color:#1d4fb8;border:1px solid #a9c1f5}}
.cards{{display:flex;flex-wrap:wrap;gap:10px}} .card{{flex:1 1 120px;background:#f5f7fa;border:1px solid #e0e5ec;border-radius:10px;padding:10px 12px;text-align:center}}
.card b{{display:block;font-size:22px}} .card span{{color:#5b6675;font-size:12px}}
table{{width:100%;border-collapse:collapse}} th,td{{padding:7px 9px;border-bottom:1px solid #e3e7ee;text-align:start;vertical-align:top}}
th{{background:#f5f7fa;font-size:12.5px;color:#4a5566}}
.badge{{display:inline-block;padding:1px 10px;border-radius:99px;font-size:12.5px;font-weight:700}}
.badge.Passed{{background:#e7f6ec;color:#146c2e}} .badge.Failed{{background:#fdecec;color:#a11a1a}} .badge.Cancelled,.badge.NotRun,.badge.Unsupported{{background:#fff4dc;color:#8a5a00}}
pre{{margin:6px 0 0;white-space:pre-wrap;word-break:break-word;background:#f5f7fa;border:1px solid #e3e7ee;border-radius:6px;padding:7px 9px;direction:ltr;text-align:left;font:12px/1.5 Consolas,'Segoe UI',monospace}}
.test{{page-break-inside:avoid}} .charts{{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:12px}} .chart{{border:1px solid #e3e7ee;border-radius:8px;padding:8px 10px;page-break-inside:avoid}}
.chart h4{{margin:0 0 2px;font-size:13px}} .chart small{{color:#5b6675}} svg{{width:100%;height:auto;display:block;direction:ltr}}
footer{{margin-top:28px;padding-top:12px;border-top:1px solid #e3e7ee;color:#5b6675;font-size:12px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}
@media(max-width:640px){{main{{padding:18px}} .charts{{grid-template-columns:1fr}}}}";
    }

    private static void Header(StringBuilder b, SessionReport r, ReportText w)
    {
        b.Append("<header>").Append(MazestaLogo.Svg("#201E1E", "logo")).Append("<h1>").Append(E(r.ShopName)).Append(" — ").Append(E(w.TitleOf(r.Kind))).Append("</h1><div class=\"meta\">");
        if (r.ServiceNumber is { } service) b.Append("<b>").Append(w.ServiceNumber).Append(": ").Append(Lt(service)).Append("</b><br>");
        b.Append(w.Started).Append(": ").Append(Lt(ReportFormat.Stamp(r.StartedAt))).Append(" · ").Append(w.Finished).Append(": ").Append(Lt(ReportFormat.Stamp(r.FinishedAt)))
         .Append(" · ").Append(w.Duration).Append(": ").Append(Lt(ReportFormat.Duration(r.DurationSeconds))).Append("<br>").Append(w.ReportId).Append(": ").Append(Lt(r.Id)).Append("</div></header>");
    }

    private static void Summary(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Verdict is not { } verdict) { b.Append("<div class=\"verdict Benchmark\">").Append(E(w.MeasurementsOnly)).Append("</div>"); return; }
        b.Append("<h2>").Append(w.Verdict).Append("</h2><div class=\"verdict ").Append(verdict).Append("\">").Append(E(w.VerdictName(verdict))).Append("</div><div class=\"cards\">");
        void Card(string label, object value) => b.Append("<div class=\"card\"><b class=\"lt\">").Append(E(Convert.ToString(value, Inv))).Append("</b><span>").Append(E(label)).Append("</span></div>");
        var c = r.Counts;
        Card(w.Total, c.Total); Card(w.Passed, c.Passed); Card(w.Failed, c.Failed); Card(w.NotDone, c.Cancelled + c.Unsupported + c.NotRun); Card(w.Errors, c.Errors);
        b.Append("</div>");
    }

    private static void Tests(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Tests.Count == 0) return;
        b.Append("<h2>").Append(w.Results).Append("</h2><table><thead><tr><th>").Append(w.Name).Append("</th><th>").Append(w.Outcome).Append("</th><th>").Append(w.Duration)
         .Append("</th><th>").Append(w.Errors).Append("</th></tr></thead><tbody>");
        foreach (var t in r.Tests)
        {
            b.Append("<tr class=\"test\"><td><b>").Append(E(t.Name)).Append("</b>");
            if (t.Options.Count > 0) b.Append("<br><small>").Append(w.Options).Append(": ").Append(string.Join(" · ", t.Options.Select(o => Lt($"{o.Key}={o.Value}")))).Append("</small>");
            if (!string.IsNullOrWhiteSpace(t.Detail)) b.Append("<br><small>").Append(w.Detail).Append(":</small><pre>").Append(E(t.Detail)).Append("</pre>");
            b.Append("</td><td><span class=\"badge ").Append(t.Outcome).Append("\">").Append(E(w.OutcomeName(t.Outcome))).Append("</span></td><td>").Append(Lt(ReportFormat.Duration(t.DurationSeconds)))
             .Append("</td><td>").Append(Lt(t.ErrorCount.ToString(Inv))).Append("</td></tr>");
        }
        b.Append("</tbody></table>");
    }

    private static void Sensors(StringBuilder b, SessionReport r, ReportText w)
    {
        b.Append("<h2>").Append(r.Kind == ReportKind.Benchmark ? w.BenchmarkSensors : w.Sensors).Append("</h2>");
        if (r.Sensors.Count == 0) { b.Append("<p>").Append(w.NoSensors).Append("</p>"); return; }
        b.Append("<table><thead><tr><th>").Append(w.Sensor).Append("</th><th>").Append(w.Min).Append("</th><th>").Append(w.Avg).Append("</th><th>").Append(w.Max).Append("</th><th>")
         .Append(w.Samples).Append("</th></tr></thead><tbody>");
        foreach (var s in r.Sensors)
            b.Append("<tr><td>").Append(Lt($"{s.Hardware} · {s.Name}")).Append("</td><td>").Append(Lt(ReportFormat.Value(s.Min, s))).Append("</td><td>").Append(Lt(ReportFormat.Value(s.Average, s))).Append("</td><td>").Append(Lt(ReportFormat.Value(s.Max, s)))
             .Append("</td><td>").Append(Lt(s.Samples.ToString(Inv))).Append("</td></tr>");
        b.Append("</tbody></table><div class=\"charts\">");
        foreach (var s in r.Sensors.Where(s => s.Trace.Count > 1)) Chart(b, r, s);
        b.Append("</div>");
    }

    private static void Benchmarks(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Benchmarks is not { Count: > 0 }) return;
        b.Append("<h2>").Append(w.Benchmarks).Append("</h2><table><thead><tr><th>").Append(w.Name).Append("</th><th>").Append(w.Metric).Append("</th><th>").Append(w.Value).Append("</th></tr></thead><tbody>");
        foreach (var bm in r.Benchmarks)
        {
            b.Append("<tr class=\"test\"><td rowspan=\"").Append(Math.Max(1, bm.Metrics.Count)).Append("\"><b>").Append(E(bm.Name)).Append("</b><br><small>").Append(w.MeasuredAt).Append(": ").Append(Lt(ReportFormat.Stamp(bm.FinishedAt))).Append("</small>");
            if (!string.IsNullOrWhiteSpace(bm.Detail)) b.Append("<pre>").Append(E(bm.Detail)).Append("</pre>");
            b.Append("</td>");
            for (int i = 0; i < bm.Metrics.Count; i++)
            {
                var m = bm.Metrics[i]; if (i > 0) b.Append("<tr>");
                b.Append("<td>").Append(E(m.Name)).Append("</td><td>").Append(Lt(Units.FormatMeasured(m.Value, m.Unit))).Append("</td></tr>");
            }
        }
        b.Append("</tbody></table>");
    }

    /// <summary>A small line chart of one sensor over the session, with the span of each test shaded behind it.</summary>
    private static void Chart(StringBuilder b, SessionReport r, SensorSummary s)
    {
        const double W = 360, H = 90, Pad = 4;
        double total = Math.Max(1, r.DurationSeconds), lo = s.Min, hi = s.Max, span = hi - lo < 1e-9 ? 1 : hi - lo;
        double X(double sec) => Pad + Math.Clamp(sec / total, 0, 1) * (W - 2 * Pad);
        double Y(double v) => H - Pad - (v - lo) / span * (H - 2 * Pad);
        string F(double d) => d.ToString("F1", Inv);
        b.Append("<div class=\"chart\"><h4>").Append(Lt($"{s.Hardware} · {s.Name}")).Append("</h4><small>").Append(Lt($"{ReportFormat.Value(s.Min, s)} – {ReportFormat.Value(s.Max, s)}")).Append("</small>")
         .Append($"<svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-label=\"{E(s.Name)}\">");
        foreach (var t in r.Tests.Where(t => t.Outcome != ReportOutcome.NotRun))
        {
            double a = X((t.StartedAt - r.StartedAt).TotalSeconds), z = X((t.FinishedAt - r.StartedAt).TotalSeconds);
            b.Append($"<rect x=\"{F(a)}\" y=\"0\" width=\"{F(Math.Max(1, z - a))}\" height=\"{H}\" fill=\"{(t.Outcome == ReportOutcome.Failed ? "#f6c9c9" : "#dbe6ff")}\" opacity=\".55\"/>");
        }
        b.Append("<polyline fill=\"none\" stroke=\"#2f6bff\" stroke-width=\"1.6\" stroke-linejoin=\"round\" points=\"").Append(string.Join(' ', s.Trace.Select(p => $"{F(X(p.Seconds))},{F(Y(p.Value))}"))).Append("\"/></svg></div>");
    }

    private static void Machine(StringBuilder b, HardwareInventory m, ReportText w)
    {
        b.Append("<h2>").Append(w.Machine).Append("</h2><table><tbody>");
        foreach (var (label, value) in ReportFormat.MachineRows(m, w)) b.Append("<tr><th style=\"width:26%\">").Append(E(label)).Append("</th><td>").Append(Lt(value)).Append("</td></tr>");
        b.Append("</tbody></table>");
    }

}
