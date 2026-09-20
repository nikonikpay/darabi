using System.Globalization; using System.Text; using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>A font to embed so the file looks the same on any machine (regular and bold TTF bytes).</summary>
public sealed record ReportFont(string Family, byte[] Regular, byte[] Bold);

/// <summary>The complete human report: one self-contained, offline HTML file (no scripts, no external requests) that is also what the PDF is printed from.</summary>
public static class ReportHtml
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // Not WebUtility.HtmlEncode: it turns every non-ASCII character (all of the Persian text) into a numeric entity.
    private static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static string Lt(string? s) => $"<bdi class=\"lt\">{E(s)}</bdi>";

    public static string Write(SessionReport r, ReportFont? font = null)
    {
        var b = new StringBuilder(32 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(ReportText.Title)).Append(" — ").Append(E(r.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main>");
        Header(b, r); Summary(b, r); Tests(b, r); Sensors(b, r); Machine(b, r.Machine);
        b.Append("<footer>").Append(E(ReportText.Footer)).Append("<br>").Append(Lt($"Mazesta Test {r.AppVersion} · {r.Id}")).Append("</footer></main></body></html>");
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
header{{border-bottom:3px solid #2f6bff;padding-bottom:14px;margin-bottom:18px}}
h1{{margin:0;font-size:24px}} h2{{margin:26px 0 10px;font-size:18px;border-inline-start:4px solid #2f6bff;padding-inline-start:10px}}
.meta{{color:#5b6675;font-size:12.5px;margin-top:4px}}
.verdict{{border-radius:10px;padding:14px 18px;font-size:17px;font-weight:700;margin:6px 0 14px}}
.verdict.Passed{{background:#e7f6ec;color:#146c2e;border:1px solid #9bd5ad}}
.verdict.Failed{{background:#fdecec;color:#a11a1a;border:1px solid #f0a8a8}}
.verdict.Incomplete{{background:#fff4dc;color:#8a5a00;border:1px solid #f0cf8a}}
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

    private static void Header(StringBuilder b, SessionReport r)
    {
        b.Append("<header><h1>").Append(E(r.ShopName)).Append(" — ").Append(E(ReportText.Title)).Append("</h1><div class=\"meta\">")
         .Append(ReportText.Started).Append(": ").Append(Lt(Stamp(r.StartedAt))).Append(" · ").Append(ReportText.Finished).Append(": ").Append(Lt(Stamp(r.FinishedAt)))
         .Append(" · ").Append(ReportText.Duration).Append(": ").Append(Lt(Duration(r.DurationSeconds))).Append("<br>").Append(ReportText.ReportId).Append(": ").Append(Lt(r.Id)).Append("</div></header>");
    }

    private static void Summary(StringBuilder b, SessionReport r)
    {
        b.Append("<h2>").Append(ReportText.Verdict).Append("</h2><div class=\"verdict ").Append(r.Verdict).Append("\">").Append(E(ReportText.VerdictName(r.Verdict))).Append("</div><div class=\"cards\">");
        void Card(string label, object value) => b.Append("<div class=\"card\"><b class=\"lt\">").Append(E(Convert.ToString(value, Inv))).Append("</b><span>").Append(E(label)).Append("</span></div>");
        var c = r.Counts;
        Card(ReportText.Total, c.Total); Card(ReportText.Passed, c.Passed); Card(ReportText.Failed, c.Failed); Card(ReportText.NotDone, c.Cancelled + c.Unsupported + c.NotRun); Card(ReportText.Errors, c.Errors);
        b.Append("</div>");
    }

    private static void Tests(StringBuilder b, SessionReport r)
    {
        b.Append("<h2>").Append(ReportText.Results).Append("</h2><table><thead><tr><th>").Append(ReportText.Name).Append("</th><th>").Append(ReportText.Outcome).Append("</th><th>").Append(ReportText.Duration)
         .Append("</th><th>").Append(ReportText.Errors).Append("</th></tr></thead><tbody>");
        foreach (var t in r.Tests)
        {
            b.Append("<tr class=\"test\"><td><b>").Append(E(t.Name)).Append("</b>");
            if (t.Options.Count > 0) b.Append("<br><small>").Append(ReportText.Options).Append(": ").Append(string.Join(" · ", t.Options.Select(o => Lt($"{o.Key}={o.Value}")))).Append("</small>");
            if (!string.IsNullOrWhiteSpace(t.Detail)) b.Append("<br><small>").Append(ReportText.Detail).Append(":</small><pre>").Append(E(t.Detail)).Append("</pre>");
            b.Append("</td><td><span class=\"badge ").Append(t.Outcome).Append("\">").Append(E(ReportText.OutcomeName(t.Outcome))).Append("</span></td><td>").Append(Lt(Duration(t.DurationSeconds)))
             .Append("</td><td>").Append(Lt(t.ErrorCount.ToString(Inv))).Append("</td></tr>");
        }
        b.Append("</tbody></table>");
    }

    private static void Sensors(StringBuilder b, SessionReport r)
    {
        b.Append("<h2>").Append(ReportText.Sensors).Append("</h2>");
        if (r.Sensors.Count == 0) { b.Append("<p>").Append(ReportText.NoSensors).Append("</p>"); return; }
        b.Append("<table><thead><tr><th>").Append(ReportText.Sensor).Append("</th><th>").Append(ReportText.Min).Append("</th><th>").Append(ReportText.Avg).Append("</th><th>").Append(ReportText.Max).Append("</th><th>")
         .Append(ReportText.Samples).Append("</th></tr></thead><tbody>");
        foreach (var s in r.Sensors)
            b.Append("<tr><td>").Append(Lt($"{s.Hardware} · {s.Name}")).Append("</td><td>").Append(Lt(Value(s.Min, s))).Append("</td><td>").Append(Lt(Value(s.Average, s))).Append("</td><td>").Append(Lt(Value(s.Max, s)))
             .Append("</td><td>").Append(Lt(s.Samples.ToString(Inv))).Append("</td></tr>");
        b.Append("</tbody></table><div class=\"charts\">");
        foreach (var s in r.Sensors.Where(s => s.Trace.Count > 1)) Chart(b, r, s);
        b.Append("</div>");
    }

    /// <summary>A small line chart of one sensor over the session, with the span of each test shaded behind it.</summary>
    private static void Chart(StringBuilder b, SessionReport r, SensorSummary s)
    {
        const double W = 360, H = 90, Pad = 4;
        double total = Math.Max(1, r.DurationSeconds), lo = s.Min, hi = s.Max, span = hi - lo < 1e-9 ? 1 : hi - lo;
        double X(double sec) => Pad + Math.Clamp(sec / total, 0, 1) * (W - 2 * Pad);
        double Y(double v) => H - Pad - (v - lo) / span * (H - 2 * Pad);
        string F(double d) => d.ToString("F1", Inv);
        b.Append("<div class=\"chart\"><h4>").Append(Lt($"{s.Hardware} · {s.Name}")).Append("</h4><small>").Append(Lt($"{Value(s.Min, s)} – {Value(s.Max, s)}")).Append("</small>")
         .Append($"<svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-label=\"{E(s.Name)}\">");
        foreach (var t in r.Tests.Where(t => t.Outcome != ReportOutcome.NotRun))
        {
            double a = X((t.StartedAt - r.StartedAt).TotalSeconds), z = X((t.FinishedAt - r.StartedAt).TotalSeconds);
            b.Append($"<rect x=\"{F(a)}\" y=\"0\" width=\"{F(Math.Max(1, z - a))}\" height=\"{H}\" fill=\"{(t.Outcome == ReportOutcome.Failed ? "#f6c9c9" : "#dbe6ff")}\" opacity=\".55\"/>");
        }
        b.Append("<polyline fill=\"none\" stroke=\"#2f6bff\" stroke-width=\"1.6\" stroke-linejoin=\"round\" points=\"").Append(string.Join(' ', s.Trace.Select(p => $"{F(X(p.Seconds))},{F(Y(p.Value))}"))).Append("\"/></svg></div>");
    }

    private static void Machine(StringBuilder b, HardwareInventory m)
    {
        b.Append("<h2>").Append(ReportText.Machine).Append("</h2><table><tbody>");
        void Row(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) b.Append("<tr><th style=\"width:26%\">").Append(E(label)).Append("</th><td>").Append(Lt(value)).Append("</td></tr>"); }
        if (m.Cpu is { } c) Row(ReportText.Cpu, $"{c.Name} · {c.PhysicalCores}C/{c.LogicalProcessors}T" + (c.MaxClockMhz is { } mhz ? $" · {mhz / 1000.0:F2} GHz" : ""));
        foreach (var g in m.Gpus) Row(ReportText.Gpu, $"{g.Name}" + (g.DriverVersion is { } d ? $" · driver {d}" : ""));
        if (m.TotalPhysicalMemoryBytes is { } ram) Row(ReportText.Ram, $"{ram / 1073741824.0:F0} GB · {m.MemoryModules.Count} module(s)");
        foreach (var d in m.MemoryModules) Row("", $"{d.Slot}: {(d.CapacityBytes ?? 0) / 1073741824} GB {d.Manufacturer} {d.PartNumber} {(d.ConfiguredSpeedMts ?? d.SpeedMts)} MT/s".Trim());
        if (m.Motherboard is { } mb) Row(ReportText.Board, $"{mb.Manufacturer} {mb.Product}");
        if (m.Bios is { } bios) Row(ReportText.Bios, $"{bios.Vendor} {bios.Version}" + (bios.ReleaseDate is { } rd ? $" ({rd:yyyy-MM-dd})" : ""));
        foreach (var s in m.Storage) Row(ReportText.Storage, $"{s.FriendlyName} · {s.MediaType} {s.BusType} · {(s.SizeBytes ?? 0) / 1_000_000_000} GB · {s.HealthStatus}");
        foreach (var n in m.NetworkAdapters.Where(n => n.IsUp)) Row(ReportText.Network, $"{n.Name}" + (n.LinkSpeedBps is { } bps ? $" · {bps / 1_000_000} Mbps" : ""));
        if (m.Os is { } os) Row(ReportText.Os, $"{os.Caption} {os.Version} ({os.Architecture})");
        b.Append("</tbody></table>");
    }

    private static string Value(double v, SensorSummary s) => (Math.Abs(v) >= 100 ? v.ToString("F0", Inv) : v.ToString("F1", Inv)) + (s.Unit.Length == 0 ? "" : " " + s.Unit);
    private static string Stamp(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Inv);
    private static string Duration(double seconds) => seconds >= 60 ? $"{(int)(seconds / 60)}m {seconds % 60:F0}s" : $"{seconds:F0}s";
}
