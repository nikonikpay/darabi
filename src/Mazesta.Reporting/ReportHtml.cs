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
        Header(b, r, w); Summary(b, r, w); WorkDone(b, r, w); Findings(b, r, w);
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
.finding{{border:1px solid #e3e7ee;border-inline-start:4px solid #c9d1dc;border-radius:8px;padding:8px 12px;margin:8px 0;page-break-inside:avoid}} .finding.Problem{{border-inline-start-color:#d23a3a}} .finding.Attention{{border-inline-start-color:#e0a020}} .finding.Good{{border-inline-start-color:#2e9e5b}}
.measures{{margin-top:6px;color:#4a5566;font-size:12.5px}}
.advice{{margin-top:6px;padding:6px 10px;border-inline-start:3px solid #f0cf8a;background:#fff8e6;font-size:12.5px;line-height:1.7}}
.badge{{display:inline-block;padding:1px 10px;border-radius:99px;font-size:12.5px;font-weight:700}}
.badge.Passed{{background:#e7f6ec;color:#146c2e}} .badge.Good{{background:#e7f6ec;color:#146c2e}} .badge.Problem{{background:#fdecec;color:#a11a1a}} .badge.Attention{{background:#fff4dc;color:#8a5a00}} .badge.Note{{background:#eef1f5;color:#4a5566}} .badge.Failed{{background:#fdecec;color:#a11a1a}} .badge.Cancelled,.badge.NotRun{{background:#eceff3;color:#5b6573}} .badge.Unsupported,.badge.Unsupported,.badge.Error,.badge.Inconclusive{{background:#fff4dc;color:#8a5a00}}
table.bm{{table-layout:fixed}} table.bm td{{overflow-wrap:anywhere}}
pre{{margin:6px 0 0;overflow-wrap:anywhere;white-space:pre-wrap;word-break:break-word;background:#f5f7fa;border:1px solid #e3e7ee;border-radius:6px;padding:7px 9px;direction:ltr;text-align:left;font:12px/1.5 Consolas,'Segoe UI',monospace}}
.test{{page-break-inside:avoid}} .charts{{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:12px}} .chart{{border:1px solid #e3e7ee;border-radius:8px;padding:8px 10px;page-break-inside:avoid}}
.chart h4{{margin:0 0 2px;font-size:13px}} .chart small{{color:#5b6675}} svg{{width:100%;height:auto;display:block;direction:ltr}}
footer{{margin-top:28px;padding-top:12px;border-top:1px solid #e3e7ee;color:#5b6675;font-size:12px}}
*{{-webkit-print-color-adjust:exact;print-color-adjust:exact}}
.ico{{width:1.15em;height:1.15em;flex:none;vertical-align:-.2em}}
.tcard{{border:1px solid #dfe4ec;border-radius:12px;margin:14px 0;overflow:hidden;background:#fff}}
.th{{display:flex;gap:12px;align-items:center;flex-wrap:wrap;padding:12px 14px;background:#f5f7fa;border-bottom:1px solid #e3e7ee;border-inline-start:5px solid #c9d1dc;break-after:avoid}}
.tcard.Passed .th{{border-inline-start-color:#2e9e5b}} .tcard.Failed .th{{border-inline-start-color:#d23a3a}} .tcard.Inconclusive .th,.tcard.Error .th,.tcard.Unsupported .th{{border-inline-start-color:#e0a020}}
.tcard.Bench .th{{border-inline-start-color:#1d4fb8}}
.tico{{display:grid;place-items:center;width:38px;height:38px;border-radius:10px;background:#201E1E;color:#FDD400}} .tico .ico{{width:22px;height:22px}}
.tn{{flex:1 1 220px;min-width:0}} .tn b{{display:block;font-size:15.5px}} .tn small{{color:#5b6675;font-size:12px}}
.tm{{display:flex;gap:6px;align-items:center;flex-wrap:wrap}}
.chip{{display:inline-flex;align-items:center;gap:5px;background:#fff;border:1px solid #dfe4ec;border-radius:99px;padding:1px 10px;font-size:12.5px}} .chip.bad{{background:#fdecec;border-color:#f0a8a8;color:#a11a1a}}
.groups{{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:6px 12px;padding:12px 14px 4px;align-items:start}}
.grp h4,h3.cat,.chart h4{{display:flex;align-items:center;gap:7px;color:var(--c);margin:0 0 8px;font-size:13.5px}} h3.cat{{margin:20px 0 6px;font-size:15px}}
.tiles{{display:flex;flex-direction:column;gap:8px;margin-bottom:10px}}
.tile{{border:1px solid #e3e7ee;border-top:3px solid var(--c);border-radius:8px;padding:7px 10px 9px;background:#fff;break-inside:avoid}}
.tl{{display:flex;align-items:center;gap:6px;font-weight:700;font-size:12.5px;line-height:1.5}} .tl .ico{{color:var(--c)}}
.ts,.sens .ts{{color:#7a8594;font-size:11px;margin:0 0 4px;min-height:1.4em}}
.trio{{display:flex;justify-content:space-between;gap:6px;margin-top:4px}} .trio div{{display:flex;flex-direction:column;align-items:flex-start}}
.trio span{{font-size:11px;color:#6b7686}} .trio b{{font-size:13px;font-weight:600}} .trio .mid b{{font-size:17px;font-weight:700;color:var(--c)}}
.rng{{position:relative;height:5px;border-radius:3px;margin-top:9px;background:linear-gradient(90deg,#e9ecef,var(--c))}} [dir=rtl] .rng{{background:linear-gradient(270deg,#e9ecef,var(--c))}}
.rng i{{position:absolute;top:-3px;width:11px;height:11px;margin-inline-start:-5px;border-radius:50%;background:#fff;border:2.5px solid var(--c)}}
.tcard .charts{{padding:0 14px 12px}} .chart h4 small{{font-weight:400;color:#6b7686}}
.lg{{display:flex;flex-wrap:wrap;gap:4px 12px;margin-top:4px;font-size:11.5px;color:#4a5566}} .lg i{{display:inline-block;width:10px;height:10px;border-radius:3px;margin-inline-end:5px;vertical-align:-1px}}
.tcard .advice{{margin:0 14px 10px}} .raw{{margin:0 14px 12px}} .raw summary{{cursor:pointer;color:#5b6675;font-size:12px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}
@media(max-width:640px){{main{{padding:18px}} .charts{{grid-template-columns:1fr}}}}";
    }

    private static void Header(StringBuilder b, SessionReport r, ReportText w)
    {
        b.Append("<header>").Append(MazestaLogo.Svg("#201E1E", "logo")).Append("<h1>").Append(E(r.ShopName)).Append(" — ").Append(E(w.TitleOf(r.Kind))).Append("</h1><div class=\"meta\">");
        if (r.ServiceNumber is { } service) b.Append("<b>").Append(w.ServiceNumber).Append(": ").Append(Lt(service)).Append("</b>");
        if (w.StageOf(r.ServiceStage) is { } stage) b.Append(r.ServiceNumber is null ? "" : " · ").Append("<b>").Append(E(stage)).Append("</b>");
        if (r.ServiceNumber is not null || r.ServiceStage is not null) b.Append("<br>");
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

    // ---- test cards: one card per test, its figures grouped by kind with icons, its own charts ----

    private enum Cat { Score, Temp, Fps, Load, Mem, Clock, Power, Fan, Volt, Other }
    private static Cat CatOf(SensorSummary s) => s.Kind switch { "Temperature" => Cat.Temp, "Load" => Cat.Load, "Clock" => Cat.Clock, "Power" => Cat.Power, "Fan" => Cat.Fan, "Voltage" => Cat.Volt, "Data" or "SmallData" => Cat.Mem, _ => Cat.Other };
    private static string CatColor(Cat c) => c switch { Cat.Temp => "#d9480f", Cat.Fps => "#2f9e44", Cat.Score => "#8a6d00", Cat.Mem => "#099268", Cat.Load => "#1c7ed6", Cat.Clock => "#7048e8", Cat.Power => "#b58105", Cat.Fan => "#0c8599", Cat.Volt => "#c2255c", _ => "#5b6675" };
    private static string CatName(Cat c, ReportText w) => c switch { Cat.Temp => w.KindTemp, Cat.Fps => w.KindFps, Cat.Score => w.KindScore, Cat.Mem => w.KindMem, Cat.Load => w.KindLoad, Cat.Clock => w.KindClock, Cat.Power => w.KindPower, Cat.Fan => w.KindFan, Cat.Volt => w.KindVolt, _ => w.KindOther };
    private static readonly string[] Palette = ["#d9480f", "#1c7ed6", "#2f9e44", "#7048e8", "#b58105", "#0c8599"];
    private static readonly System.Text.RegularExpressions.Regex FpsRx = new(@"(?<v>\d+(?:\.\d+)?) FPS (?<k>average|lowest half-second|highest half-second)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["temp"] = "<path d=\"M14 14.76V5a2 2 0 0 0-4 0v9.76a4 4 0 1 0 4 0z\"/>",
        ["load"] = "<path d=\"M12 14l4-4\"/><path d=\"M3.3 17a9 9 0 1 1 17.4 0\"/>",
        ["clock"] = "<path d=\"M2 12h3l3-8 4 16 3-8h7\"/>",
        ["power"] = "<path d=\"M13 2L4 14h7l-1 8 9-12h-7z\"/>",
        ["fan"] = "<circle cx=\"12\" cy=\"12\" r=\"2\"/><path d=\"M12 10c0-4 1-7 4-7s3 4-1 6M14 12c4 0 7 1 7 4s-4 3-6-1M12 14c0 4-1 7-4 7s-3-4 1-6M10 12c-4 0-7-1-7-4s4-3 6 1\"/>",
        ["fps"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"M10 9l5 3-5 3z\"/>",
        ["volt"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v10M8 12h8\"/>",
        ["other"] = "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\"/><path d=\"M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4\"/>",
        ["cpu"] = "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\"/><rect x=\"10\" y=\"10\" width=\"4\" height=\"4\"/><path d=\"M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4\"/>",
        ["memory"] = "<rect x=\"2\" y=\"7\" width=\"20\" height=\"10\" rx=\"1\"/><path d=\"M6 11v2M10 11v2M14 11v2M18 11v2\"/>",
        ["gpu"] = "<rect x=\"2\" y=\"6\" width=\"20\" height=\"11\" rx=\"1\"/><circle cx=\"9\" cy=\"11.5\" r=\"2.5\"/><path d=\"M15 9h4M15 12h4M5 17v3M19 17v3\"/>",
        ["storage"] = "<rect x=\"3\" y=\"6\" width=\"18\" height=\"12\" rx=\"2\"/><path d=\"M7 14h.01M11 14h6\"/>",
        ["network"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18\"/>",
        ["time"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
        ["bug"] = "<path d=\"M12 8v5M12 16h.01\"/><path d=\"M10.3 3.9L2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z\"/>",
        ["mem"] = "<rect x=\"2\" y=\"7\" width=\"20\" height=\"10\" rx=\"1\"/><path d=\"M6 11v2M10 11v2M14 11v2M18 11v2\"/>",
        ["score"] = "<path d=\"M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM7 6H4v2a3 3 0 0 0 3 3M17 6h3v2a3 3 0 0 1-3 3\"/>",
        ["check"] = "<path d=\"M5 12l4 4 10-10\"/>",
    };
    private static string Ico(string name) => $"<svg class=\"ico\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.9\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>";
    private static string CatIco(Cat c) => Ico(c.ToString().ToLowerInvariant());
    private static string TestIcon(string id) => id.Split('.').Select(Icon1).FirstOrDefault(x => x != "check") ?? "check";
    private static string Icon1(string part) => part switch { "cpu" => "cpu", "memory" or "ram" => "memory", "gpu" => "gpu", "storage" or "disk" or "drive" => "storage", "network" or "net" => "network", "power" => "power", _ => "check" };

    /// <summary>The part a sensor belongs to, so "Core" is never anonymous in a chart legend.</summary>
    private static string Short(SensorSummary s)
    {
        string r = s.Role ?? "", tag = r.StartsWith("Cpu") ? "CPU" : r.StartsWith("Gpu") ? "GPU" : r.StartsWith("Ram") ? "RAM" : r.StartsWith("Storage") ? "Drive" : r.StartsWith("Net") ? "Net" : r.StartsWith("Board") || r.StartsWith("Chipset") ? "Board" : "";
        return tag.Length == 0 || s.Name.StartsWith(tag, StringComparison.OrdinalIgnoreCase) ? s.Name : $"{tag} · {s.Name}";
    }

    /// <summary>One figure: the kind's icon, its name, then min / average / max and where the average sits between the extremes. Every tile has the same height so a row lines up.</summary>
    private static void Tile(StringBuilder b, Cat c, string label, string? sub, double? min, double? avg, double? max, string unit, ReportText w, bool exact = false)
    {
        string V(double v) => Lt(exact ? Units.FormatMeasured(v, unit) : Units.FormatMeasured(v, unit, 1));
        b.Append("<div class=\"tile\" style=\"--c:").Append(CatColor(c)).Append("\"><div class=\"tl\">").Append(CatIco(c)).Append("<span>").Append(E(label)).Append("</span></div>")
         .Append("<div class=\"ts\">").Append(string.IsNullOrWhiteSpace(sub) ? "&nbsp;" : Lt(sub)).Append("</div><div class=\"trio\">");
        if (min is { } lo) b.Append("<div><span>").Append(E(w.Min)).Append("</span><b>").Append(V(lo)).Append("</b></div>");
        if (avg is { } a) b.Append("<div class=\"mid\"><span>").Append(min is null && max is null ? "&nbsp;" : E(w.Avg)).Append("</span><b>").Append(V(a)).Append("</b></div>");
        if (max is { } hi) b.Append("<div><span>").Append(E(w.Max)).Append("</span><b>").Append(V(hi)).Append("</b></div>");
        b.Append("</div>");
        if (min is { } m0 && max is { } m1 && avg is { } m2 && m1 - m0 > 1e-9)
            b.Append("<div class=\"rng\"><i style=\"inset-inline-start:").Append(Math.Clamp((m2 - m0) / (m1 - m0) * 100, 0, 100).ToString("F1", Inv)).Append("%\"></i></div>");
        b.Append("</div>");
    }

    private sealed record Score(string Label, double Value, string Unit);
    private static readonly System.Text.RegularExpressions.Regex RateRx = new(@"(?<v>\d[\d,]*(?:\.\d+)?) (?<u>Gop/s|GFLOPS|MB/s|GB/s|IOPS|Mbps)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The test's own result figures (speed, operations per second…) read out of its evidence text, each with the words said next to it.</summary>
    private static List<Score> ScoresOf(string? detail)
    {
        var list = new List<Score>(); if (detail is null) return list;
        foreach (var clause in detail.Split([';', '\n']))
        {
            if (clause.Contains("FPS") || clause.Contains(" avg ")) continue;
            var m = RateRx.Match(clause); if (!m.Success) continue;
            string label = clause.Remove(m.Index, m.Length).Trim(' ', ':', '=', ',', '-', '·');
            if (label.Length > 60) label = label[..60].TrimEnd();
            list.Add(new(label.Length == 0 ? m.Groups["u"].Value : label, double.Parse(m.Groups["v"].Value.Replace(",", ""), Inv), m.Groups["u"].Value));
        }
        return list;
    }

    private static void Tests(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Tests.Count == 0) return;
        b.Append("<h2>").Append(w.Results).Append("</h2>");
        foreach (var t in r.Tests)
        {
            double? Fps(string kind) => t.Detail is null ? null : FpsRx.Matches(t.Detail).Where(m => m.Groups["k"].Value.StartsWith(kind)).Select(m => (double?)double.Parse(m.Groups["v"].Value, Inv)).FirstOrDefault();
            string badges = $"<span class=\"badge {t.Outcome}\">{E(w.OutcomeName(t.Outcome))}</span><span class=\"chip\">{Ico("time")}{Lt(ReportFormat.Duration(t.DurationSeconds))}</span><span class=\"chip{(t.ErrorCount > 0 ? " bad" : "")}\">{Ico("bug")}{E(w.Errors)} {Lt(t.ErrorCount.ToString(Inv))}</span>";
            string? options = t.Options.Count > 0 ? E(w.Options) + ": " + string.Join(" · ", t.Options.Select(o => Lt($"{o.Key}={o.Value}"))) : null;
            Card(b, t.Outcome.ToString(), TestIcon(t.Id), t.Name, options, badges, ScoresOf(t.Detail), (Fps("lowest"), Fps("average"), Fps("highest")),
                r.TestSensors?.FirstOrDefault(x => x.TestId == t.Id)?.Sensors ?? [], t.DurationSeconds, t.Advice, t.Detail, w);
        }
    }

    /// <summary>One card for a test or a benchmark run: its result figures first, then every reading taken during that run by kind (min, average, max), then that run's own charts.</summary>
    private static void Card(StringBuilder b, string cls, string icon, string name, string? note, string badges, IReadOnlyList<Score> scores, (double? Low, double? Avg, double? High) fps,
        IReadOnlyList<SensorSummary> sensors, double seconds, string? advice, string? detail, ReportText w)
    {
        bool figures = sensors.Count > 0 || fps.Avg is not null || scores.Count > 0;
        b.Append("<section class=\"tcard ").Append(cls).Append("\"><header class=\"th\"><span class=\"tico\">").Append(Ico(icon)).Append("</span><div class=\"tn\"><b>").Append(E(name)).Append("</b>");
        if (note is not null) b.Append("<small>").Append(note).Append("</small>");
        b.Append("</div><div class=\"tm\">").Append(badges).Append("</div></header>");

        if (figures) b.Append("<div class=\"groups\">");
        foreach (var cat in Enum.GetValues<Cat>())
        {
            var rows = sensors.Where(s => CatOf(s) == cat).ToList();
            if (cat == Cat.Score ? scores.Count == 0 : cat == Cat.Fps ? fps.Avg is null : rows.Count == 0) continue;
            b.Append("<div class=\"grp\" style=\"--c:").Append(CatColor(cat)).Append("\"><h4>").Append(CatIco(cat)).Append(E(CatName(cat, w))).Append("</h4><div class=\"tiles\">");
            if (cat == Cat.Score) foreach (var s in scores) Tile(b, cat, s.Label, null, null, s.Value, null, s.Unit, w, exact: true);
            if (cat == Cat.Fps) Tile(b, cat, "FPS", null, fps.Low, fps.Avg, fps.High, "FPS", w);
            foreach (var s in rows) Tile(b, cat, Short(s), s.Hardware, s.Min, s.Average, s.Max, s.Unit, w);
            b.Append("</div></div>");
        }
        if (figures) b.Append("</div>");

        var charts = Enum.GetValues<Cat>().Where(c => c is not (Cat.Score or Cat.Fps or Cat.Volt or Cat.Other)).Select(c => (c, list: sensors.Where(s => CatOf(s) == c && s.Trace.Count > 1).ToList())).Where(x => x.list.Count > 0).ToList();
        if (charts.Count > 0)
        {
            b.Append("<div class=\"charts\">");
            foreach (var (c, list) in charts) SeriesChart(b, c, list, seconds, w);
            b.Append("</div>");
        }
        if (!string.IsNullOrWhiteSpace(advice)) b.Append("<div class=\"advice\">").Append(E(advice).Replace("\n", "<br>")).Append("</div>");
        // The figures above are the same evidence in order; the raw sentence stays for the technician, folded when the cards already show it.
        if (!string.IsNullOrWhiteSpace(detail))
            b.Append(figures ? "<details class=\"raw\"><summary>" : "<div class=\"raw\"><small>").Append(E(w.Detail)).Append(figures ? "</summary><pre>" : ":</small><pre>").Append(E(detail)).Append(figures ? "</pre></details>" : "</pre></div>");
        b.Append("</section>");
    }

    /// <summary>One kind of reading (every temperature, every load…) of one test over that test's own time, one line per sensor with its legend.</summary>
    private static void SeriesChart(StringBuilder b, Cat cat, List<SensorSummary> list, double seconds, ReportText w)
    {
        const double W = 520, H = 150, L = 40, R = 8, T = 8, B = 20;
        bool pct = cat == Cat.Load;
        double lo = pct ? 0 : list.Min(s => s.Trace.Min(p => p.Value)), hi = pct ? 100 : list.Max(s => s.Trace.Max(p => p.Value));
        if (hi - lo < 1e-9) hi = lo + 1;
        if (!pct) { double pad = (hi - lo) * .08; lo -= pad; hi += pad; }
        double dur = Math.Max(1, Math.Max(seconds, list.Max(s => s.Trace.Max(p => p.Seconds))));
        double X(double s) => L + s / dur * (W - L - R);
        double Y(double v) => T + (hi - v) / (hi - lo) * (H - T - B);
        string F(double d) => d.ToString("F1", Inv);
        b.Append("<div class=\"chart\" style=\"--c:").Append(CatColor(cat)).Append("\"><h4>").Append(CatIco(cat)).Append(E(CatName(cat, w))).Append(" <small>").Append(Lt(list[0].Unit)).Append("</small></h4>")
         .Append($"<svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-label=\"{E(CatName(cat, w))}\">");
        foreach (double f in new[] { 0, .5, 1 })
        {
            double y = T + f * (H - T - B);
            b.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{F(y)}\" y2=\"{F(y)}\" stroke=\"#e3e7ee\" stroke-width=\"1\"/><text x=\"{L - 5}\" y=\"{F(y + 3.5)}\" text-anchor=\"end\" font-size=\"11\" fill=\"#5b6675\">{(hi - f * (hi - lo)).ToString("F0", Inv)}</text>");
        }
        b.Append($"<text x=\"{L}\" y=\"{H - 5}\" font-size=\"11\" fill=\"#5b6675\">0</text><text x=\"{W - R}\" y=\"{H - 5}\" text-anchor=\"end\" font-size=\"11\" fill=\"#5b6675\">{E(ReportFormat.Duration(dur))}</text>");
        for (int i = 0; i < list.Count; i++)
            b.Append($"<polyline fill=\"none\" stroke=\"{Palette[i % Palette.Length]}\" stroke-width=\"1.7\" stroke-linejoin=\"round\" stroke-linecap=\"round\" points=\"").Append(string.Join(' ', list[i].Trace.Select(p => $"{F(X(p.Seconds))},{F(Y(p.Value))}"))).Append("\"/>");
        b.Append("</svg><div class=\"lg\">");
        for (int i = 0; i < list.Count; i++) b.Append("<span><i style=\"background:").Append(Palette[i % Palette.Length]).Append("\"></i>").Append(Lt(Short(list[i]))).Append("</span>");
        b.Append("</div></div>");
    }

    /// <summary>The whole session's readings by kind, each with the test it peaked in (the per-test cards hold the rest).</summary>
    private static void Sensors(StringBuilder b, SessionReport r, ReportText w)
    {
        b.Append("<h2>").Append(r.Kind == ReportKind.Benchmark ? w.BenchmarkSensors : w.Sensors).Append("</h2>");
        if (r.Sensors.Count == 0) { b.Append("<p>").Append(w.NoSensors).Append("</p>"); return; }
        bool perTest = r.TestSensors is { Count: > 0 };
        foreach (var cat in Enum.GetValues<Cat>())
        {
            var rows = r.Sensors.Where(s => CatOf(s) == cat).ToList();
            if (rows.Count == 0) continue;
            b.Append("<h3 class=\"cat\" style=\"--c:").Append(CatColor(cat)).Append("\">").Append(CatIco(cat)).Append(E(CatName(cat, w))).Append("</h3><table class=\"sens\"><thead><tr><th>").Append(w.Sensor).Append("</th><th>").Append(w.Min).Append("</th><th>").Append(w.Avg).Append("</th><th>").Append(w.Max).Append("</th>");
            b.Append(perTest ? "<th>" + E(w.PeakIn) + "</th>" : "").Append("<th>").Append(w.Samples).Append("</th></tr></thead><tbody>");
            foreach (var s in rows)
            {
                b.Append("<tr><td>").Append(Lt(Short(s))).Append("<br><small class=\"ts\">").Append(Lt(s.Hardware)).Append("</small></td><td>").Append(Lt(ReportFormat.Value(s.Min, s))).Append("</td><td><b>").Append(Lt(ReportFormat.Value(s.Average, s))).Append("</b></td><td>").Append(Lt(ReportFormat.Value(s.Max, s))).Append("</td>");
                if (perTest)
                {
                    var peak = r.TestSensors!.SelectMany(x => x.Sensors.Where(q => q.Id == s.Id).Select(q => (x.TestId, q.Max))).DefaultIfEmpty().MaxBy(p => p.Max);
                    b.Append("<td>").Append(peak.TestId is null ? "" : E(r.Tests.FirstOrDefault(t => t.Id == peak.TestId)?.Name ?? peak.TestId)).Append("</td>");
                }
                b.Append("<td>").Append(Lt(s.Samples.ToString(Inv))).Append("</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        if (perTest) return;   // older reports have no per-test figures: the whole-session charts stay
        b.Append("<div class=\"charts\">");
        foreach (var s in r.Sensors.Where(s => s.Trace.Count > 1)) Chart(b, r, s);
        b.Append("</div>");
    }

    /// <summary>The checkup's findings, the ones that need action first; each with what it means and the numbers it stands on.</summary>
    /// <summary>What the technician wrote about the work done on the customer's system (kept apart from every measured value).</summary>
    private static void WorkDone(StringBuilder b, SessionReport r, ReportText w)
    {
        if (string.IsNullOrWhiteSpace(r.ServiceNotes)) return;
        b.Append("<h2>").Append(E(w.WorkDone)).Append("</h2><div style=\"white-space:pre-wrap;line-height:1.9;border:1px solid #e3e7ee;border-radius:8px;padding:10px 14px\">").Append(E(r.ServiceNotes.Trim())).Append("</div>");
    }

    private static void Findings(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Findings is not { Count: > 0 }) return;
        b.Append("<h2>").Append(E(w.Checkup)).Append("</h2><p class=\"meta\">").Append(E(w.CheckupNote)).Append("</p>");
        foreach (var f in r.Findings.OrderBy(f => f.Level switch { "Problem" => 0, "Attention" => 1, "Note" => 2, _ => 3 }))
        {
            b.Append("<div class=\"finding ").Append(E(f.Level)).Append("\"><div><span class=\"badge ").Append(E(f.Level)).Append("\">").Append(E(w.LevelName(f.Level))).Append("</span> <b>").Append(E(f.Title)).Append("</b>");
            if (!string.IsNullOrWhiteSpace(f.Subject)) b.Append(" · ").Append(Lt(f.Subject));
            b.Append("</div><div>").Append(E(f.Text)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(f.Hint)) b.Append("<div class=\"advice\">").Append(E(f.Hint)).Append("</div>");
            if (f.Measures.Count > 0) b.Append("<div class=\"measures\">").Append(string.Join(" · ", f.Measures.Select(m => E(m.Name) + ": " + Lt(m.Value)))).Append("</div>");
            if (!string.IsNullOrWhiteSpace(f.Source)) b.Append("<div class=\"measures\">").Append(E(w.MakerFigures)).Append(": ").Append(Lt(f.Source)).Append("</div>");
            b.Append("</div>");
        }
    }

    private static void Benchmarks(StringBuilder b, SessionReport r, ReportText w)
    {
        if (r.Benchmarks is not { Count: > 0 }) return;
        b.Append("<h2>").Append(w.Benchmarks).Append("</h2>");
        foreach (var bm in r.Benchmarks)
        {
            double seconds = bm.StartedAt is { } s0 ? Math.Max(0, (bm.FinishedAt - s0).TotalSeconds) : 0;
            string badges = $"<span class=\"chip\">{Ico("time")}{Lt(ReportFormat.Duration(seconds))}</span><span class=\"chip\">{E(w.MeasuredAt)} {Lt(ReportFormat.Stamp(bm.FinishedAt))}</span>";
            var scores = bm.Metrics.Select(m => new Score(m.Name, m.Value, m.Unit)).ToList();
            Card(b, "Bench", TestIcon(bm.Id), bm.Name, null, badges, scores, (null, null, null),
                r.TestSensors?.FirstOrDefault(x => x.TestId == BenchKey(bm))?.Sensors ?? [], seconds, null, bm.Detail, w);
        }
    }

    /// <summary>The key a benchmark run's own sensor figures are stored under (the same benchmark can run twice in one report).</summary>
    public static string BenchKey(BenchmarkEntry bm) => $"{bm.Id}|{bm.FinishedAt.UtcTicks}";

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
