using System.Globalization; using System.Text; using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>One drive as the customer summary shows it: what it is and what the drive and Windows say about its health. Every value the
/// drive does not report is null and printed as such.</summary>
public sealed record DriveSummary(string Name, string? MediaType, string? BusType, long? SizeBytes, string? Health, int? WearPercent, double? TemperatureC, long? PowerOnHours);

/// <summary>One part's temperature: the reading when the summary was made (null when there is no recent one) and the highest since
/// <see cref="MaxSince"/>, when the monitor started counting it (null when it never read one).</summary>
public sealed record TemperatureSummary(string Part, double? CurrentC, double? MaxC, DateTimeOffset MaxSince);

/// <summary>
/// The one-page summary a shop hands the customer: the machine, its drives' health, its temperatures and the last test result, boxed and short
/// enough for an A5 sheet. Only measured values: a missing one says so. It is a snapshot of the moment it was made, not a test.
/// </summary>
public sealed record CustomerSummary(DateTimeOffset CreatedAt, string ShopName, string? ServiceNumber, string AppVersion, HardwareInventory Machine,
    IReadOnlyList<DriveSummary> Drives, IReadOnlyList<TemperatureSummary> Temperatures, ReportVerdict? LastVerdict, DateTimeOffset? LastTestAt);

/// <summary>The summary's wording in one language. Component names and values stay Latin in both (spec §7.1).</summary>
public sealed class SummaryText
{
    public required string Language { get; init; }
    public bool IsRtl => Language == "fa";
    public required string Title, Date, Machine, Drives, Temperatures, LastTest, NoTest, Now, Max, MaxSince, Drive, Health, Wear, Temperature, PowerOn, Hours,
        NotReported, HealthHealthy, HealthWarning, HealthUnhealthy, HealthUnknown, NoDrives, Footer, Technician, Customer, Ram, Modules, Released;

    public static SummaryText For(string language) => language == "en" ? English : Persian;

    public string HealthName(string? health) => health switch { "Healthy" => HealthHealthy, "Warning" => HealthWarning, "Unhealthy" => HealthUnhealthy, null => NotReported, _ => HealthUnknown };

    public static readonly SummaryText Persian = new()
    {
        Language = "fa", Title = "خلاصه‌ی وضعیت سیستم", Date = "تاریخ", Machine = "مشخصات سیستم", Drives = "سلامت هارد و SSD", Temperatures = "دماها",
        LastTest = "آخرین آزمون جامع", NoTest = "روی این سیستم هنوز آزمون جامعی ثبت نشده است.", Now = "عدد بزرگ هر کادر، دما در لحظه‌ی صدور این برگه است.", Max = "بیشینه", MaxSince = "از ساعت {0}",
        Drive = "دیسک", Health = "وضعیت سلامت", Wear = "عمر مصرف‌شده", Temperature = "دما", PowerOn = "کارکرد", Hours = "ساعت", NotReported = "گزارش نشد",
        HealthHealthy = "سالم", HealthWarning = "هشدار", HealthUnhealthy = "معیوب", HealthUnknown = "نامشخص", NoDrives = "هیچ دیسکی گزارش نشد.",
        Footer = "همه‌ی مقادیر از اندازه‌گیری همین دستگاه در زمان صدور آمده‌اند؛ مقداری که در دسترس نبوده «گزارش نشد» نوشته شده است. این برگه خلاصه است؛ جزئیات در گزارش کامل آزمون.",
        Technician = "امضای تکنسین", Customer = "امضای مشتری", Ram = "حافظه‌ی RAM", Modules = "ماژول", Released = "انتشار"
    };

    public static readonly SummaryText English = new()
    {
        Language = "en", Title = "System status summary", Date = "Date", Machine = "System", Drives = "Drive health (HDD / SSD)", Temperatures = "Temperatures",
        LastTest = "Last full test", NoTest = "No full test has been recorded on this machine yet.", Now = "The large number in each box is the temperature when this sheet was issued.", Max = "Highest", MaxSince = "since {0}",
        Drive = "Drive", Health = "Health", Wear = "Life used", Temperature = "Temperature", PowerOn = "Powered on", Hours = "h", NotReported = "not reported",
        HealthHealthy = "Healthy", HealthWarning = "Warning", HealthUnhealthy = "Failing", HealthUnknown = "Unknown", NoDrives = "No drive was reported.",
        Footer = "Every value was measured on this machine when this sheet was issued; a value that was not available reads “not reported”. This is a summary; the full test report has the details.",
        Technician = "Technician", Customer = "Customer", Ram = "Memory (RAM)", Modules = "module(s)", Released = "released"
    };
}

/// <summary>The customer summary as one self-contained offline HTML page (no scripts, no external requests), laid out for an A5 sheet.</summary>
public static class SummaryHtml
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static string Lt(string? s) => $"<bdi class=\"lt\">{E(s)}</bdi>";

    public static string Write(CustomerSummary s, ReportFont? font = null, SummaryText? wording = null, ReportText? reportWording = null)
    {
        var w = wording ?? SummaryText.Persian; var rw = reportWording ?? ReportText.For(w.Language);
        string na = $"<span class=\"na\">{E(w.NotReported)}</span>";
        string Temp(double? c) => c is { } v ? Lt($"{v.ToString("F0", Inv)} °C") : na;
        var b = new StringBuilder(24 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"").Append(w.Language).Append("\" dir=\"").Append(w.IsRtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(w.Title)).Append(" — ").Append(E(s.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main>");

        b.Append("<header><div class=\"brand\">").Append(MazestaLogo.Svg("#0B0B0D", "logo")).Append("<div><h1>").Append(E(w.Title)).Append("</h1><div class=\"shop\">").Append(E(s.ShopName)).Append("</div></div></div><div class=\"meta\">")
         .Append(E(w.Date)).Append(": ").Append(Lt(ReportFormat.Stamp(s.CreatedAt)));
        if (s.ServiceNumber is { } service) b.Append("<br>").Append(E(rw.ServiceNumber)).Append(": <b>").Append(Lt(service)).Append("</b>");
        b.Append("</div></header>");

        // The machine: label/value pairs in two columns.
        b.Append("<section><h2>").Append(E(w.Machine)).Append("</h2><dl class=\"spec\">");
        void Row(string label, string? value, bool wide = false) { if (!string.IsNullOrWhiteSpace(value)) b.Append(wide ? "<div class=\"wide\">" : "<div>").Append("<dt>").Append(E(label)).Append("</dt><dd>").Append(Lt(value)).Append("</dd></div>"); }
        var m = s.Machine;
        Row(rw.Cpu, m.Cpu?.Name?.Trim(), wide: true);
        foreach (var g in m.Gpus) Row(rw.Gpu, g.Name + (g.DriverVersion is { } d ? $" · driver {d}" : ""), wide: true);
        if (m.Motherboard is { } mb) Row(rw.Board, $"{mb.Manufacturer} {mb.Product}".Trim(), wide: true);
        if (m.Bios is { } bios) Row(rw.Bios, $"{bios.Version}".Trim() + (bios.ReleaseDate is { } rd ? $" · {rd:yyyy-MM-dd}" : ""));
        if (m.TotalPhysicalMemoryBytes is { } ram) Row(w.Ram, $"{ram / 1073741824.0:F0} GB · {m.MemoryModules.Count} × " + string.Join(", ", m.MemoryModules.Select(x => x.ConfiguredSpeedMts ?? x.SpeedMts).OfType<int>().Distinct().Select(x => $"{x} MT/s")));
        if (m.Os is { } os) Row(rw.Os, $"{os.Caption} {os.Version}".Replace("Microsoft ", "").Trim(), wide: true);
        b.Append("</dl></section>");

        // Drives: one row each.
        b.Append("<section><h2>").Append(E(w.Drives)).Append("</h2>");
        if (s.Drives.Count == 0) b.Append("<p class=\"na\">").Append(E(w.NoDrives)).Append("</p>");
        else
        {
            b.Append("<table class=\"drives\"><thead><tr><th>").Append(E(w.Drive)).Append("</th><th>").Append(E(w.Health)).Append("</th><th>").Append(E(w.Wear))
             .Append("</th><th>").Append(E(w.Temperature)).Append("</th><th>").Append(E(w.PowerOn)).Append("</th></tr></thead><tbody>");
            foreach (var d in s.Drives)
            {
                string cls = d.Health switch { "Healthy" => "ok", "Warning" => "warn", "Unhealthy" => "bad", _ => "unk" };
                b.Append("<tr><td><b>").Append(Lt(d.Name)).Append("</b><small>")
                 .Append(Lt(string.Join(" · ", new[] { d.MediaType, d.BusType, d.SizeBytes is { } sz ? $"{sz / 1_000_000_000} GB" : null }.Where(x => !string.IsNullOrWhiteSpace(x)))))
                 .Append("</small></td><td><span class=\"badge ").Append(cls).Append("\">").Append(E(w.HealthName(d.Health))).Append("</span></td><td>")
                 .Append(d.WearPercent is { } wear ? Lt($"{wear}%") : na).Append("</td><td>").Append(Temp(d.TemperatureC)).Append("</td><td>")
                 .Append(d.PowerOnHours is { } h ? Lt(h.ToString("N0", Inv)) + " " + E(w.Hours) : na).Append("</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        b.Append("</section>");

        // Temperatures: the big number is now, the highest since the monitor started under it.
        b.Append("<section><h2>").Append(E(w.Temperatures)).Append("</h2><div class=\"temps\">");
        foreach (var t in s.Temperatures)
            b.Append("<div class=\"temp\"><div class=\"part\">").Append(Lt(t.Part)).Append("</div><div class=\"now\">").Append(Temp(t.CurrentC)).Append("</div><div class=\"max\">")
             .Append(E(w.Max)).Append(" ").Append(Temp(t.MaxC)).Append("<br><small>").Append(E(string.Format(Inv, w.MaxSince, t.MaxSince.ToLocalTime().ToString("HH:mm", Inv)))).Append("</small></div></div>");
        b.Append("</div>");
        if (s.Temperatures.Count == 0) b.Append("<p class=\"na\">").Append(E(w.NotReported)).Append("</p>");
        b.Append("<p class=\"note\">").Append(E(w.Now)).Append("</p></section>");

        b.Append("<section><h2>").Append(E(w.LastTest)).Append("</h2>");
        if (s.LastVerdict is { } verdict) b.Append("<div class=\"verdict ").Append(verdict).Append("\">").Append(E(rw.VerdictName(verdict))).Append("<small>").Append(Lt(ReportFormat.Stamp(s.LastTestAt ?? s.CreatedAt))).Append("</small></div>");
        else b.Append("<p class=\"na\">").Append(E(w.NoTest)).Append("</p>");
        b.Append("</section>");

        b.Append("<div class=\"sign\"><div>").Append(E(w.Technician)).Append("</div><div>").Append(E(w.Customer)).Append("</div></div>")
         .Append("<footer>").Append(E(w.Footer)).Append(" ").Append(Lt($"Mazesta Test {s.AppVersion}")).Append("</footer></main></body></html>");
        return b.ToString();
    }

    private static string Css(ReportFont? f)
    {
        string face = f is null ? "" :
            $"@font-face{{font-family:'{f.Family}';font-weight:400;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Regular)}) format('truetype')}}" +
            $"@font-face{{font-family:'{f.Family}';font-weight:700;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Bold)}) format('truetype')}}";
        string family = f is null ? "Tahoma,'Segoe UI',sans-serif" : $"'{f.Family}',Tahoma,'Segoe UI',sans-serif";
        return face + $@"
@page{{size:A5;margin:8mm}}
*{{box-sizing:border-box}}
body{{margin:0;background:#eceae3;color:#16161a;font:10px/1.5 {family}}}
main{{max-width:540px;margin:0 auto;background:#fff;padding:14px 16px}}
.lt{{font-family:'Segoe UI',Tahoma,sans-serif;direction:ltr;unicode-bidi:isolate}}
header{{display:flex;justify-content:space-between;align-items:center;gap:10px;border-bottom:3px solid #FDD400;padding-bottom:7px;margin-bottom:11px}}
.brand{{display:flex;align-items:center;gap:9px}} .logo{{height:20px;width:auto}}
h1{{margin:0;font-size:14px;line-height:1.3}} .shop{{color:#55555f;font-size:9.5px}} .meta{{font-size:9.5px;color:#44444c;text-align:end;line-height:1.55}}
section{{border:1.3px solid #16161a;border-radius:7px;padding:9px 9px 6px;margin-bottom:10px;page-break-inside:avoid}}
h2{{margin:-17px 0 3px;font-size:10.5px;display:inline-block;background:#fff;padding:0 5px;border-inline-start:3px solid #FDD400;line-height:1.4}}
.spec{{display:grid;grid-template-columns:1fr 1fr;gap:0 14px;margin:0}} .spec div{{display:flex;gap:6px;padding:2px 0;border-bottom:1px dotted #d0cfc8;min-width:0}}
.spec div.wide{{grid-column:1/3}} dt{{color:#55555f;flex:0 0 auto}} dd{{margin:0;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}
table{{width:100%;border-collapse:collapse}} th,td{{padding:2.5px 3px;border-bottom:1px dotted #d0cfc8;text-align:start;vertical-align:middle}}
thead th{{font-weight:400;color:#55555f;font-size:9px;border-bottom:1px solid #16161a}} tbody tr:last-child td{{border-bottom:0}}
.drives td small{{display:block;color:#66666e;font-size:8.5px;line-height:1.3}} .drives td b{{font-size:9.8px}}
.badge{{display:inline-block;border-radius:99px;padding:0 7px;font-weight:700;font-size:9px;border:1px solid;white-space:nowrap}}
.badge.ok{{background:#e6f6ee;color:#0f6b3c;border-color:#98d4b2}} .badge.warn{{background:#fff4d6;color:#8a5a00;border-color:#efd08a}}
.badge.bad{{background:#fde8e7;color:#a0180f;border-color:#f0a39e}} .badge.unk{{background:#f0f0ee;color:#55555f;border-color:#d0d0cc}}
.temps{{display:grid;grid-template-columns:repeat(3,1fr);gap:5px}}
.temp{{border:1px solid #d8d7d0;border-radius:5px;padding:3px 5px;text-align:center}}
.temp .part{{font-weight:700;font-size:9.5px}} .temp .now{{font-size:15px;font-weight:700;line-height:1.25}}
.temp .max{{font-size:9px;color:#33333a;border-top:1px dotted #d0cfc8;margin-top:2px;padding-top:1px;line-height:1.35}} .temp small{{color:#77777f;font-size:8px}}
.note{{margin:3px 0 0;color:#77777f;font-size:8.5px}}
.verdict{{border-radius:5px;padding:4px 8px;font-weight:700;display:flex;justify-content:space-between;gap:8px;align-items:center}}
.verdict small{{font-weight:400;color:#44444c}}
.verdict.Passed{{background:#e6f6ee;color:#0f6b3c}} .verdict.Failed{{background:#fde8e7;color:#a0180f}} .verdict.Incomplete{{background:#fff4d6;color:#8a5a00}}
.na{{color:#77777f;font-weight:400;font-style:italic}} p.na{{margin:2px 0}}
.sign{{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin:6px 0 4px}} .sign div{{border-top:1px solid #16161a;padding-top:2px;font-size:9px;color:#44444c;text-align:center;margin-top:24px}}
footer{{color:#77777f;font-size:8px;line-height:1.45;border-top:1px solid #d8d7d0;padding-top:4px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}";
    }
}
