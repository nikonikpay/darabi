using System.Globalization; using System.Text; using Mazesta.Core.Health;
namespace Mazesta.Reporting;

/// <summary>A part whose temperature a summary reports: CPU, GPU core, GPU hot spot, GPU memory, a drive, the board.</summary>
public enum HeatPart { Cpu, Gpu, GpuHotSpot, GpuMemory, Drive, Board }

/// <summary>A part's highest temperature over the whole report, the device it was read on, and the test or benchmark it was reached in (null when
/// the report has no run to name).</summary>
public sealed record PartPeak(HeatPart Part, string Device, double MaxC, string? During);

/// <summary>One test or benchmark of the report: its result (null for a benchmark, which neither passes nor fails), how long it ran, and each
/// part's highest temperature while it ran. A part with no reading in its window is absent, never 0.</summary>
public sealed record SummaryRow(string Name, ReportOutcome? Outcome, double DurationSeconds, IReadOnlyDictionary<HeatPart, double> Peaks)
{
    /// <summary>The benchmark's main figures (name, value, unit) - its result, not the conditions it ran in; empty for a test.</summary>
    public IReadOnlyList<SummaryFigure> Figures { get; init; } = [];
}

/// <summary>One of a benchmark's main figures, as the summary prints it.</summary>
public sealed record SummaryFigure(string Name, double Value, string Unit);

/// <summary>
/// The one-page summary of one saved report, for the customer: its verdict, each test's result and the highest temperatures the parts reached
/// while it ran. Made from the report alone - what was measured then, never a reading taken now. For a report saved before exact per-test peaks
/// were recorded, a test's peak comes from the report's down-sampled trace (<see cref="FromTrace"/>): a measured value, but possibly under the
/// true peak.
/// </summary>
public sealed record ReportSummary(SessionReport Report, IReadOnlyList<PartPeak> Peaks, IReadOnlyList<SummaryRow> Rows, IReadOnlyList<HeatPart> Columns, bool FromTrace)
{
    private static readonly Dictionary<string, HeatPart> Roles = new()
    {
        ["CpuPackageTemp"] = HeatPart.Cpu, ["CpuTctlTdie"] = HeatPart.Cpu, ["GpuCoreTemp"] = HeatPart.Gpu, ["GpuHotSpotTemp"] = HeatPart.GpuHotSpot,
        ["GpuVramTemp"] = HeatPart.GpuMemory, ["StorageTemp"] = HeatPart.Drive, ["BoardTemp"] = HeatPart.Board,
    };
    /// <summary>Reports saved before sensors carried their role: the names the monitor gives these sensors.</summary>
    private static readonly Dictionary<string, HeatPart> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CPU Package"] = HeatPart.Cpu, ["Core (Tctl/Tdie)"] = HeatPart.Cpu, ["GPU Core"] = HeatPart.Gpu, ["GPU Hot Spot"] = HeatPart.GpuHotSpot, ["GPU Memory Junction"] = HeatPart.GpuMemory,
    };

    /// <summary>The metrics a summary prints for a benchmark: what it measured about the part (speed, latency, frame rate), none of the readings of
    /// the conditions (clocks, temperatures, power) that the full report keeps. In the order the benchmark lists them.</summary>
    private static readonly HashSet<string> MainFigures = new(StringComparer.Ordinal)
    {
        "Bench_Cpu_Gflops", "Bench_Cpu_PerThread", "Bench_Mem_Write", "Bench_Mem_Read", "Bench_Mem_Latency",
        "Bench_Storage_SeqRead", "Bench_Storage_SeqWrite",
        "Bench_Gpu_Fps", "Bench_Gpu_Triangles", "Bench_Gpu_Rt_Fps", "Bench_Gpu_Rt_Rays", "Bench_Scene_Score", "Bench_Scene_ScoreGpu", "Bench_Scene_ScoreCpu", "Bench_Scene_ScoreRam", "Bench_Gpu_Scene_Fps", "Bench_Gpu_Scene_FpsPart", "Bench_Gpu_Scene_Low",
        "Bench_Gpu_Ai_Fp32", "Bench_Gpu_Ai_Fp16", "Bench_Gpu_Ai_Int8", "Bench_Net_Download", "Bench_Net_Upload", "Bench_Net_Ping",
        "Bench_Ai_Prompt", "Bench_Ai_Gen",
    };
    /// <summary>The most figures a summary line holds: one line a benchmark.</summary>
    public const int MaxFigures = 3;
    public static IReadOnlyList<SummaryFigure> FiguresOf(BenchmarkEntry b)
        => [.. (b.Metrics.Any(m => m.Key is not null) ? b.Metrics.Where(m => m.Key is not null && MainFigures.Contains(m.Key))
            : b.Metrics).Take(MaxFigures).Select(m => new SummaryFigure(m.Name, m.Value, m.Unit))];   // saved before metrics carried their key: the first ones are the results

    public static HeatPart? PartOf(SensorSummary s) => s.Kind != "Temperature" ? null
        : s.Role is { } role ? (Roles.TryGetValue(role, out var p) ? p : null) : Names.TryGetValue(s.Name, out var n) ? n : null;

    public static ReportSummary Of(SessionReport r)
    {
        // The CPU is its package sensor where it has one (Tctl/Tdie on AMD otherwise): one CPU number, not two that disagree by an offset.
        var byPart = r.Sensors.Select(s => (Sensor: s, Part: PartOf(s))).Where(x => x.Part is not null).GroupBy(x => x.Part!.Value)
            .ToDictionary(g => g.Key, g => g.Key == HeatPart.Cpu && g.Any(x => x.Sensor.Role == "CpuPackageTemp" || x.Sensor.Name == "CPU Package")
                ? g.Where(x => x.Sensor.Role == "CpuPackageTemp" || x.Sensor.Name == "CPU Package").Select(x => x.Sensor).ToList() : g.Select(x => x.Sensor).ToList());

        var runs = r.Tests.Where(t => t.Outcome != ReportOutcome.NotRun).Select(t => (t.Name, (ReportOutcome?)t.Outcome, From: t.StartedAt, To: t.FinishedAt, t.DurationSeconds, Figures: (IReadOnlyList<SummaryFigure>)[]))
            .Concat((r.Benchmarks ?? []).Select(b => (b.Name, (ReportOutcome?)null, From: b.StartedAt ?? b.FinishedAt, To: b.FinishedAt, Math.Max(0, (b.FinishedAt - (b.StartedAt ?? b.FinishedAt)).TotalSeconds), Figures: FiguresOf(b))))
            .OrderBy(x => x.From).ToList();
        bool fromTrace = r.Peaks is null;

        double? PeakIn(SensorSummary s, DateTimeOffset from, DateTimeOffset to)
        {
            if (r.Peaks is { } peaks) return peaks.FirstOrDefault(p => p.SensorId == s.Id && p.From == from && p.To == to)?.Max;
            double a = (from - r.StartedAt).TotalSeconds - 1, b = (to - r.StartedAt).TotalSeconds + 1;
            var inside = s.Trace.Where(p => p.Seconds >= a && p.Seconds <= b).ToList();
            return inside.Count > 0 ? inside.Max(p => p.Value) : null;
        }

        var rows = runs.Select(x => new SummaryRow(x.Name, x.Item2, x.Item5, byPart.Select(kv => (kv.Key, Max: kv.Value.Select(s => PeakIn(s, x.From, x.To)).Where(v => v is not null).Max()))
            .Where(p => p.Max is not null).ToDictionary(p => p.Key, p => p.Max!.Value)) { Figures = x.Figures }).ToList();

        var peaks = byPart.OrderBy(kv => kv.Key).Select(kv =>
        {
            var hottest = kv.Value.MaxBy(s => s.Max)!;
            string? during = rows.Where(x => x.Peaks.ContainsKey(kv.Key)).MaxBy(x => x.Peaks[kv.Key])?.Name;
            return new PartPeak(kv.Key, hottest.Hardware, hottest.Max, during);
        }).ToList();
        HeatPart[] shown = [HeatPart.Cpu, HeatPart.Gpu, HeatPart.GpuHotSpot, HeatPart.Drive];
        return new(r, peaks, rows, [.. shown.Where(byPart.ContainsKey)], fromTrace);
    }
}

/// <summary>The summary's wording in one language. Component names and values stay Latin in both (spec §7.1).</summary>
public sealed class SummaryText
{
    public required string Language { get; init; }
    public bool IsRtl => Language == "fa";
    public required string Title, Date, Machine, Drives, Peaks, PeaksNote, TraceNote, During, Tests, Test, Result, Duration, Minutes, Seconds, NoRuns, Benchmark,
        Drive, Health, NotReported, HealthHealthy, HealthWarning, HealthUnhealthy, HealthUnknown, NoDrives, Footer, Technician, Customer, Ram, NoTemps,
        Cpu, Gpu, GpuHotSpot, GpuMemory, DriveTemp, Board, Installed;

    public static SummaryText For(string language) => language == "en" ? English : Persian;

    public string HealthName(string? health) => health switch { "Healthy" => HealthHealthy, "Warning" => HealthWarning, "Unhealthy" => HealthUnhealthy, null => NotReported, _ => HealthUnknown };
    public string PartName(HeatPart p) => p switch { HeatPart.Cpu => Cpu, HeatPart.Gpu => Gpu, HeatPart.GpuHotSpot => GpuHotSpot, HeatPart.GpuMemory => GpuMemory, HeatPart.Drive => DriveTemp, _ => Board };

    public static readonly SummaryText Persian = new()
    {
        Language = "fa", Title = "خلاصه‌ی گزارش آزمون", Date = "تاریخ گزارش", Machine = "مشخصات سیستم", Drives = "سلامت هارد و SSD", Peaks = "بیشترین دما در طول آزمون",
        PeaksNote = "بالاترین دمایی که هر قطعه در مدت این گزارش به آن رسید، و آزمونی که در آن ثبت شد.", TraceNote = "این گزارش پیش از ثبت دقیق اوج هر آزمون ساخته شده؛ اوج هر ردیف از نمونه‌های ذخیره‌شده‌ی گزارش است.",
        During = "در", Tests = "نتیجه‌ی آزمون‌ها", Test = "آزمون", Result = "نتیجه", Duration = "مدت", Minutes = "دقیقه", Seconds = "ثانیه",
        NoRuns = "در این گزارش آزمونی اجرا نشده است.", Benchmark = "بنچمارک",
        Drive = "دیسک", Health = "سلامت", NotReported = "گزارش نشد", HealthHealthy = "سالم", HealthWarning = "هشدار", HealthUnhealthy = "معیوب", HealthUnknown = "نامشخص",
        NoDrives = "هیچ دیسکی گزارش نشد.", NoTemps = "در مدت این گزارش دمایی ثبت نشد.",
        Footer = "همه‌ی مقادیر در زمان همین آزمون روی این دستگاه اندازه‌گیری شده‌اند؛ مقداری که در دسترس نبوده «گزارش نشد» نوشته شده است. جزئیات کامل در گزارش اصلی است.",
        Technician = "امضای تکنسین", Customer = "امضای مشتری", Ram = "حافظه‌ی RAM",
        Cpu = "پردازنده", Gpu = "کارت گرافیک", GpuHotSpot = "نقطه‌ی داغ گرافیک", GpuMemory = "حافظه‌ی گرافیک", DriveTemp = "دیسک", Board = "مادربرد",
        Installed = "نرم‌افزار تست مازستا روی سیستم شما نصب است و می‌توانید نتایج کامل را از داخل نرم‌افزار ببینید."
    };

    public static readonly SummaryText English = new()
    {
        Language = "en", Title = "Test report summary", Date = "Report date", Machine = "System", Drives = "Drive health (HDD / SSD)", Peaks = "Highest temperatures during the test",
        PeaksNote = "The highest temperature each part reached during this report, and the test it was reached in.", TraceNote = "This report predates exact per-test peaks; each row's peak comes from the report's stored samples.",
        During = "in", Tests = "Results", Test = "Test", Result = "Result", Duration = "Duration", Minutes = "min", Seconds = "s",
        NoRuns = "No test ran in this report.", Benchmark = "Benchmark",
        Drive = "Drive", Health = "Health", NotReported = "not reported", HealthHealthy = "Healthy", HealthWarning = "Warning", HealthUnhealthy = "Failing", HealthUnknown = "Unknown",
        NoDrives = "No drive was reported.", NoTemps = "No temperature was recorded during this report.",
        Footer = "Every value was measured on this machine during this test; a value that was not available reads “not reported”. The full report has the details.",
        Technician = "Technician", Customer = "Customer", Ram = "Memory (RAM)",
        Cpu = "CPU", Gpu = "Graphics card", GpuHotSpot = "GPU hot spot", GpuMemory = "GPU memory", DriveTemp = "Drive", Board = "Motherboard",
        Installed = "Mazesta Test is installed on your system; you can see the full results inside the app."
    };
}

/// <summary>The report summary as one self-contained offline HTML page (no scripts, no external requests), laid out for one A5 sheet: a long report
/// (many tests, cards and drives) is set tighter (<see cref="Fit"/>) rather than run onto a second page.</summary>
public static class SummaryHtml
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    /// <summary>A figure as a short number and its unit: a hundred and more whole, ten and more with one decimal, else two.</summary>
    private static string Figure(SummaryFigure f)
    {
        double v = Math.Abs(f.Value);
        string number = f.Value.ToString(v >= 100 ? "F0" : v >= 10 ? "F1" : "F2", Inv);
        return f.Unit.Length == 0 ? number : $"{number} {f.Unit}";
    }
    private static string Lt(string? s) => $"<bdi class=\"lt\">{E(s)}</bdi>";
    /// <summary>A name without its bracketed detail ("(FP64)", "(256×1440)"): the summary has one line for it, the full report the rest.</summary>
    internal static string Short(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(（][^\)）]*[\)）]", "").Trim() is { Length: > 0 } t ? t : name;

    public static string Write(ReportSummary s, ReportFont? font = null, SummaryText? wording = null, ReportText? reportWording = null)
    {
        var w = wording ?? SummaryText.Persian; var rw = reportWording ?? ReportText.For(w.Language); var r = s.Report;
        string na = $"<span class=\"na\">{E(w.NotReported)}</span>";
        string Temp(double? c) => c is { } v ? Lt($"{v.ToString("F0", Inv)} °C") : na;
        var b = new StringBuilder(24 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"").Append(w.Language).Append("\" dir=\"").Append(w.IsRtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(w.Title)).Append(" — ").Append(E(r.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main class=\"").Append(Fit(s)).Append("\">");

        b.Append("<header><div class=\"brand\">").Append(MazestaLogo.Svg("#0B0B0D", "logo")).Append("<div><h1>").Append(E(w.Title)).Append("</h1><div class=\"shop\">").Append(E(r.ShopName)).Append("</div></div></div><div class=\"meta\">")
         .Append(E(w.Date)).Append(": ").Append(Lt(ReportFormat.Stamp(r.CreatedAt)));
        if (r.ServiceNumber is { } service) b.Append("<br>").Append(E(rw.ServiceNumber)).Append(": <b>").Append(Lt(service)).Append("</b>");
        b.Append("</div></header>");

        // The verdict, with the counts behind it; a benchmark report has no verdict.
        if (r.Verdict is { } verdict)
            b.Append("<div class=\"verdict ").Append(verdict).Append("\">").Append(E(rw.VerdictName(verdict))).Append("<small>")
             .Append(Lt($"{r.Counts.Passed}/{r.Counts.Total}")).Append("</small></div>");
        else b.Append("<div class=\"verdict Benchmark\">").Append(E(w.Benchmark)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(r.ServiceNotes))
            b.Append("<section><h2>").Append(E(rw.WorkDone)).Append("</h2><div style=\"white-space:pre-wrap;line-height:1.9\">").Append(E(r.ServiceNotes.Trim())).Append("</div></section>");
        b.Append("<div class=\"installed\">").Append(E(w.Installed)).Append("</div>");

        // The highest temperatures: one row of tiles (the parts the summary shows), each with the test it was reached in.
        var shown = s.Peaks.Where(p => s.Columns.Contains(p.Part)).ToList();
        b.Append("<section><h2>").Append(E(w.Peaks)).Append("</h2>");
        if (shown.Count == 0) b.Append("<p class=\"na\">").Append(E(w.NoTemps)).Append("</p>");
        else
        {
            b.Append("<div class=\"temps\" style=\"grid-template-columns:repeat(").Append(shown.Count).Append(",1fr)\">");
            foreach (var p in shown)
            {
                b.Append("<div class=\"temp\"><div class=\"part\">").Append(E(w.PartName(p.Part))).Append("</div><div class=\"now\">").Append(Temp(p.MaxC)).Append("</div>");
                if (p.During is { } during) b.Append("<div class=\"max\">").Append(E(Short(during))).Append("</div>");
                b.Append("</div>");
            }
            b.Append("</div>");
            if (s.FromTrace) b.Append("<p class=\"note\">").Append(E(w.TraceNote)).Append("</p>");
        }
        b.Append("</section>");

        // The results as a table, one line a test or benchmark: its name and its few main figures (a network test's speeds and ping, a memory test's
        // bandwidth and latency), never the temperatures of parts it did not stress. A test shows its verdict; a benchmark's figures are its result.
        b.Append("<section><h2>").Append(E(w.Tests)).Append("</h2>");
        if (s.Rows.Count == 0) b.Append("<p class=\"na\">").Append(E(w.NoRuns)).Append("</p>");
        else
        {
            b.Append("<table class=\"res\"><tbody>");
            foreach (var row in s.Rows)
            {
                b.Append("<tr><td class=\"rn\"><b>").Append(E(Short(row.Name))).Append("</b></td><td class=\"figs\">");
                foreach (var f in row.Figures) b.Append("<span class=\"fig\"><small>").Append(E(Short(f.Name))).Append("</small> ").Append(Lt(Figure(f))).Append("</span>");
                b.Append("</td><td class=\"end\">");
                if (row.Outcome is { } o)
                {
                    string cls = o switch { ReportOutcome.Passed => "ok", ReportOutcome.Failed => "bad", _ => "warn" };
                    b.Append("<span class=\"badge ").Append(cls).Append("\">").Append(E(rw.OutcomeName(o))).Append("</span>");
                }
                b.Append("</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        b.Append("</section>");

        // The machine: label/value pairs in two columns.
        b.Append("<section><h2>").Append(E(w.Machine)).Append("</h2><dl class=\"spec\">");
        void Row(string label, string? value, bool wide = false) { if (!string.IsNullOrWhiteSpace(value)) b.Append(wide ? "<div class=\"wide\">" : "<div>").Append("<dt>").Append(E(label)).Append("</dt><dd>").Append(Lt(value)).Append("</dd></div>"); }
        var m = r.Machine;
        if (ReportFormat.DeviceName(m) is { } device) Row(rw.Device, device + (m.Computer?.IsPortable == true ? $" · {rw.Laptop}" : ""), wide: true);
        Row(rw.Cpu, m.Cpu?.Name?.Trim(), wide: true);
        foreach (var g in m.Gpus) Row(rw.Gpu, g.Name + (g.DriverVersion is { } d ? $" · driver {d}" : ""), wide: true);
        // The BIOS sits on the motherboard's line (it belongs to the board), and the memory has a line of its own.
        string? board = m.Motherboard is { } mb ? $"{mb.Manufacturer} {mb.Product}".Trim() : null;
        string? bios = m.Bios is { } bi ? $"{bi.Version}".Trim() + (bi.ReleaseDate is { } rd ? $" · {rd:yyyy-MM-dd}" : "") : null;
        if (!string.IsNullOrWhiteSpace(board) && !string.IsNullOrWhiteSpace(bios))
            b.Append("<div class=\"wide\"><dt>").Append(E(rw.Board)).Append("</dt><dd>").Append(Lt(board)).Append("</dd><dt class=\"sep\">").Append(E(rw.Bios)).Append("</dt><dd class=\"fixed\">").Append(Lt(bios)).Append("</dd></div>");
        else { Row(rw.Board, board, wide: true); Row(rw.Bios, bios, wide: true); }
        if (m.TotalPhysicalMemoryBytes is { } ram) Row(w.Ram, $"{ram / 1073741824.0:F0} GB · {m.MemoryModules.Count} × " + string.Join(", ", m.MemoryModules.Select(x => x.ConfiguredSpeedMts ?? x.SpeedMts).OfType<int>().Distinct().Select(x => $"{x} MT/s")), wide: true);
        if (m.Os is { } os) Row(rw.Os, $"{os.Caption} {os.Version}".Replace("Microsoft ", "").Trim(), wide: true);
        b.Append("</dl></section>");

        // Drives as the report recorded them: Windows' verdict, with the life left where the drive counts wear.
        b.Append("<section><h2>").Append(E(w.Drives)).Append("</h2>");
        if (m.Storage.Count == 0) b.Append("<p class=\"na\">").Append(E(w.NoDrives)).Append("</p>");
        else
        {
            b.Append("<table class=\"drives\"><tbody>");
            foreach (var d in m.Storage)
            {
                string cls = d.HealthStatus switch { "Healthy" => "ok", "Warning" => "warn", "Unhealthy" => "bad", _ => "unk" };
                string health = w.HealthName(d.HealthStatus) + (DriveAttention.HealthPercent(d.WearPercent) is { } pct ? $" ({pct}%)" : "");
                b.Append("<tr><td><b>").Append(Lt(d.FriendlyName ?? "?")).Append("</b><small>")
                 .Append(Lt(string.Join(" · ", new[] { d.MediaType, d.BusType, d.SizeBytes is { } sz ? $"{sz / 1_000_000_000} GB" : null }.Where(x => !string.IsNullOrWhiteSpace(x)))))
                 .Append("</small></td><td class=\"end\"><span class=\"badge ").Append(cls).Append("\">").Append(E(health)).Append("</span></td></tr>");
            }
            b.Append("</tbody></table>");
        }
        b.Append("</section>");

        b.Append("<div class=\"sign\"><div>").Append(E(w.Technician)).Append("</div><div>").Append(E(w.Customer)).Append("</div></div>")
         .Append("<footer>").Append(E(w.Footer)).Append(" ").Append(Lt($"Mazesta Test {r.AppVersion}")).Append("</footer></main></body></html>");
        return b.ToString();
    }

    /// <summary>How tight the sheet is set: by the lines it will hold (a test, a drive and a graphics card each take one, the temperature row one).</summary>
    internal static string Fit(ReportSummary s)
    {
        var m = s.Report.Machine;
        int lines = s.Rows.Count + m.Storage.Count + m.Gpus.Count + (s.Peaks.Count > 0 ? 1 : 0);
        return lines > 20 ? "tightest" : lines > 12 ? "tight" : "";
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
header{{display:flex;justify-content:space-between;align-items:center;gap:10px;border-bottom:3px solid #FDD400;padding-bottom:6px;margin-bottom:9px}}
.brand{{display:flex;align-items:center;gap:9px}} .logo{{height:20px;width:auto}}
h1{{margin:0;font-size:14px;line-height:1.3}} .shop{{color:#55555f;font-size:9.5px}} .meta{{font-size:9.5px;color:#44444c;text-align:end;line-height:1.55}}
section{{border:1.3px solid #16161a;border-radius:7px;padding:8px 8px 5px;margin-bottom:9px;page-break-inside:avoid}}
h2{{margin:-16px 0 3px;font-size:10.5px;display:inline-block;background:#fff;padding:0 5px;border-inline-start:3px solid #FDD400;line-height:1.4}}
.spec{{display:grid;grid-template-columns:1fr 1fr;gap:0 14px;margin:0}} .spec div{{display:flex;gap:6px;padding:1.5px 0;border-bottom:1px dotted #d0cfc8;min-width:0}}
.spec div.wide{{grid-column:1/3}} dt{{color:#55555f;flex:0 0 auto}} dd{{margin:0;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}
table{{width:100%;border-collapse:collapse}} th,td{{padding:2px 3px;border-bottom:1px dotted #d0cfc8;text-align:start;vertical-align:middle}}
thead th{{font-weight:400;color:#55555f;font-size:9px;border-bottom:1px solid #16161a}} tbody tr:last-child td{{border-bottom:0}}
.runs td b{{font-size:9.5px}} .runs td:nth-child(n+3),.runs th:nth-child(n+3){{text-align:center;white-space:nowrap}}
.drives td small{{color:#66666e;font-size:8.5px;margin-inline-start:6px}} .drives td b{{font-size:9.8px}} td.end{{text-align:end}}
.badge{{display:inline-block;border-radius:99px;padding:0 7px;font-weight:700;font-size:9px;border:1px solid;white-space:nowrap}}
.badge.ok{{background:#e6f6ee;color:#0f6b3c;border-color:#98d4b2}} .badge.warn{{background:#fff4d6;color:#8a5a00;border-color:#efd08a}}
.badge.bad{{background:#fde8e7;color:#a0180f;border-color:#f0a39e}} .badge.unk{{background:#f0f0ee;color:#55555f;border-color:#d0d0cc}}
.temps{{display:grid;gap:4px}}
.temp{{border:1px solid #d8d7d0;border-radius:5px;padding:2px 4px;text-align:center;min-width:0}}
.temp .part{{font-weight:700;font-size:8.5px}} .temp .now{{font-size:13.5px;font-weight:700;line-height:1.2}}
.temp .max{{font-size:7.5px;color:#33333a;border-top:1px dotted #d0cfc8;margin-top:1px;padding-top:1px;line-height:1.3;overflow-wrap:anywhere}}
.temp small{{color:#77777f;font-size:7px}}
.note{{margin:2px 0 0;color:#77777f;font-size:8px}}
.installed{{border:1.3px solid #FDD400;background:#fffbe6;border-radius:5px;padding:4px 9px;font-weight:700;font-size:10.5px;margin-bottom:10px;text-align:center}}
.res td{{padding:2px 3px;white-space:nowrap}} .res td.rn{{width:1%}} .res td.rn b{{font-size:9.5px}} .res td.figs{{white-space:normal}} .dur{{color:#66666e;font-size:8.5px;white-space:nowrap}}
.fig{{display:inline-block;margin-inline-end:6px;white-space:nowrap}} .fig small{{color:#55555f;font-size:7.6px}} .fig .lt{{font-weight:700;font-size:9.5px}}
.spec dt.sep{{margin-inline-start:8px}} .spec dd.fixed{{flex:0 0 auto}}
.verdict{{border-radius:5px;padding:4px 9px;font-weight:700;font-size:12px;display:flex;justify-content:space-between;gap:8px;align-items:center;margin-bottom:11px}}
.verdict small{{font-weight:400;color:#44444c;font-size:10px}}
.verdict.Passed{{background:#e6f6ee;color:#0f6b3c}} .verdict.Failed{{background:#fde8e7;color:#a0180f}} .verdict.Incomplete{{background:#fff4d6;color:#8a5a00}} .verdict.Benchmark{{background:#eef1f7;color:#2b3a55}}
.na{{color:#77777f;font-weight:400;font-style:italic}} p.na{{margin:2px 0}}
.sign{{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin:4px 0 4px}} .sign div{{border-top:1px solid #16161a;padding-top:2px;font-size:9px;color:#44444c;text-align:center;margin-top:20px}}
footer{{color:#77777f;font-size:8px;line-height:1.45;border-top:1px solid #d8d7d0;padding-top:3px}}
main.tight{{font-size:9.2px;line-height:1.4}} .tight section{{padding:7px 7px 4px;margin-bottom:8px}} .tight th,.tight td{{padding:1.2px 3px}}
.tight .runs td b,.tight .drives td b{{font-size:9px}} .tight .badge{{font-size:8.5px;line-height:1.35}} .tight .temp .now{{font-size:12.5px}}
.tight .spec div{{padding:1px 0}} .tight .sign div{{margin-top:16px}} .tight header{{margin-bottom:7px}} .tight .verdict{{margin-bottom:10px}}
main.tightest{{font-size:8.6px;line-height:1.32}} .tightest section{{padding:6px 6px 3px;margin-bottom:7px}} .tightest th,.tightest td{{padding:0.6px 3px}}
.tightest .runs td b,.tightest .drives td b{{font-size:8.5px}} .tightest thead th{{font-size:8px}} .tightest .badge{{font-size:8px;line-height:1.3;padding:0 5px}}
.tightest .temp .now{{font-size:12px}} .tightest .temp .max{{display:none}} .tightest .note{{display:none}} .tightest .installed{{padding:2px 8px;font-size:9.5px;margin-bottom:7px}} .tightest .fig small{{font-size:7.8px}} .tightest .fig .lt{{font-size:9px}} .tightest .spec div{{padding:0.5px 0}}
.tightest .sign div{{margin-top:12px}} .tightest header{{margin-bottom:6px;padding-bottom:4px}} .tightest .verdict{{margin-bottom:9px;padding:3px 9px}} .tightest footer{{font-size:7.5px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}";
    }
}
