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

/// <summary>What the power test drew: the CPU package and the GPU, each its highest reading while the test ran (null when the machine did not report it). The total is their sum, so
/// it is the draw of those two parts only - the board, drives and fans are not counted.</summary>
public sealed record PowerDraw(double? CpuW, double? GpuW)
{
    public double? Total => CpuW is null && GpuW is null ? null : (CpuW ?? 0) + (GpuW ?? 0);
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
    public PowerDraw? Power { get; init; }
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
        "Bench_Gpu_Ai_Fp32", "Bench_Gpu_Ai_Fp16", "Bench_Gpu_Ai_Int8", "Bench_Gpu_Video_EncH264", "Bench_Gpu_Video_EncHevc", "Bench_Gpu_Video_EncAv1", "Bench_Gpu_Video_DecH264", "Bench_Gpu_Video_DecHevc", "Bench_Gpu_Video_DecAv1", "Bench_Net_Download", "Bench_Net_Upload", "Bench_Net_Ping",
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
        PowerDraw? power = null;
        if (r.Tests.FirstOrDefault(t => t.Id == "power.combined" && t.Outcome != ReportOutcome.NotRun) is { } pt)
        {
            double? Side(string role) => r.Sensors.Where(x => x.Role == role).Select(x => PeakIn(x, pt.StartedAt, pt.FinishedAt)).Max();
            power = new PowerDraw(Side("CpuPackagePower"), Side("GpuPower"));
            if (power.Total is null) power = null;
        }
        return new(r, peaks, rows, [.. shown.Where(byPart.ContainsKey)], fromTrace) { Power = power };
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

    public string PsuTitle => IsRtl ? "توان مصرفی (پاور)" : "Power draw (PSU)";
    public string PsuTotal => IsRtl ? "بیشترین توان مصرفی" : "Peak power draw";
    public string PsuNote => IsRtl ? "جمع بیشترین توان پردازنده و کارت گرافیک در آزمون هم‌زمان؛ مادربرد، دیسک‌ها و فن‌ها حساب نشده‌اند." : "CPU and graphics card peaks added together during the combined test; board, drives and fans are not counted.";
    public string DriveMax => IsRtl ? "بیشترین دما" : "Peak";
    public string NotTested => IsRtl ? "تست نشده" : "Not tested";
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

/// <summary>The report summary as one self-contained offline HTML page (no scripts, no external requests), laid out for one A4 sheet, a card for each part: a long report
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
    /// <summary>The report date: Solar Hijri with the local time in Persian, ISO otherwise; the digits stay Latin (spec 7.1).</summary>
    internal static string Stamp(DateTimeOffset t, bool persian)
    {
        var local = t.ToLocalTime().DateTime;
        if (!persian) return local.ToString("yyyy-MM-dd HH:mm", Inv);
        var pc = new PersianCalendar();
        return string.Create(Inv, $"{pc.GetYear(local)}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00}  {local:HH:mm}");
    }
    private static string Lt(string? s) => $"<bdi class=\"lt\">{E(s)}</bdi>";
    /// <summary>A name without its bracketed detail ("(FP64)", "(256×1440)"): the summary has one line for it, the full report the rest.</summary>
    internal static string Short(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(（][^\)）]*[\)）]", "").Trim() is { Length: > 0 } t ? t : name;

    /// <summary>Which card of the sheet a test or benchmark belongs to, by the prefix of its id (the same split <see cref="PartHistory"/> uses).</summary>
    private static readonly (string Key, string[] Prefixes)[] Cards =
        [("cpu", ["cpu.", "bench.cpu."]), ("gpu", ["gpu.", "bench.gpu."]), ("ram", ["memory.", "bench.memory"]), ("storage", ["storage.", "bench.storage"]), ("psu", ["power."])];
    /// <summary>The temperature at which a part's figure turns amber, then red: a reading aid on the sheet, not a verdict (the tests decide that).</summary>
    private static (double Warn, double Bad) Limits(HeatPart p) => p switch
    {
        HeatPart.Cpu => (80, 90), HeatPart.Gpu => (75, 85), HeatPart.GpuHotSpot => (90, 105), HeatPart.GpuMemory => (90, 100), HeatPart.Drive => (60, 70), _ => (65, 80),
    };
    private static string Level(HeatPart p, double c) => c >= Limits(p).Bad ? "bad" : c >= Limits(p).Warn ? "warn" : "ok";
    private static string Cls(ReportOutcome o) => o switch { ReportOutcome.Passed => "ok", ReportOutcome.Failed => "bad", _ => "warn" };

    public static string Write(ReportSummary s, ReportFont? font = null, SummaryText? wording = null, ReportText? reportWording = null)
    {
        var w = wording ?? SummaryText.Persian; var rw = reportWording ?? ReportText.For(w.Language); var r = s.Report; var m = r.Machine;
        string na = $"<span class=\"na\">{E(w.NotReported)}</span>";
        var b = new StringBuilder(32 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"").Append(w.Language).Append("\" dir=\"").Append(w.IsRtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(w.Title)).Append(" — ").Append(E(r.ShopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main class=\"").Append(Fit(s)).Append("\">");

        b.Append("<header><div class=\"brand\">").Append(MazestaLogo.Svg("#0B0B0D", "logo")).Append("<div><h1>").Append(E(w.Title)).Append("</h1><div class=\"shop\">").Append(E(r.ShopName)).Append("</div></div></div><div class=\"meta\">")
         .Append(E(w.Date)).Append(": ").Append(Lt(Stamp(r.CreatedAt, w.IsRtl)));
        if (r.ServiceNumber is { } service) b.Append("<br>").Append(E(rw.ServiceNumber)).Append(": <b>").Append(Lt(service)).Append("</b>");
        b.Append("</div></header>");

        // The verdict, with the counts behind it; a benchmark report has no verdict.
        if (r.Verdict is { } verdict)
            b.Append("<div class=\"verdict ").Append(verdict).Append("\">").Append(E(rw.VerdictName(verdict))).Append("<small>").Append(Lt($"{r.Counts.Passed}/{r.Counts.Total}")).Append("</small></div>");
        else b.Append("<div class=\"verdict Benchmark\">").Append(E(w.Benchmark)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(r.ServiceNotes))
            b.Append("<section class=\"notes\"><h2>").Append(E(rw.WorkDone)).Append("</h2><div style=\"white-space:pre-wrap;line-height:1.9\">").Append(E(r.ServiceNotes.Trim())).Append("</div></section>");

        // The machine: device, board with its BIOS on one line, the system.
        int machineAt = b.Length; b.Append("<section class=\"machine\"><dl class=\"spec\">"); int machineRows = b.Length;
        void Row(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) b.Append("<div class=\"wide\"><dt>").Append(E(label)).Append("</dt><dd>").Append(Lt(value)).Append("</dd></div>"); }
        if (ReportFormat.DeviceName(m) is { } device) Row(rw.Device, device + (m.Computer?.IsPortable == true ? $" · {rw.Laptop}" : ""));
        string? board = m.Motherboard is { } mb ? $"{mb.Manufacturer} {mb.Product}".Trim() : null;
        string? bios = m.Bios is { } bi ? $"{bi.Version}".Trim() + (bi.ReleaseDate is { } rd ? $" · {rd:yyyy-MM-dd}" : "") : null;
        if (!string.IsNullOrWhiteSpace(board) && !string.IsNullOrWhiteSpace(bios))
            b.Append("<div class=\"wide\"><dt>").Append(E(rw.Board)).Append("</dt><dd>").Append(Lt(board)).Append("</dd><dt class=\"sep\">").Append(E(rw.Bios)).Append("</dt><dd class=\"fixed\">").Append(Lt(bios)).Append("</dd></div>");
        else { Row(rw.Board, board); Row(rw.Bios, bios); }
        if (m.Os is { } os) Row(rw.Os, $"{os.Caption} {os.Version}".Replace("Microsoft ", "").Trim());
        if (b.Length == machineRows) b.Length = machineAt; else b.Append("</dl></section>");   // nothing known about the machine: no empty box

        // One card a part: what it is, the hottest it got, and each test of it with its result and figures.
        var ids = r.Tests.ToDictionary(t => t.Name, t => t.Id, StringComparer.Ordinal);
        foreach (var x in r.Benchmarks ?? []) ids[x.Name] = x.Id;
        IReadOnlyList<SummaryRow> RowsOf(string key) => [.. s.Rows.Where(x => ids.TryGetValue(x.Name, out var id) && Cards.First(c => c.Key == key).Prefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal)))];
        var claimed = Cards.SelectMany(c => RowsOf(c.Key)).ToHashSet();
        var other = s.Rows.Where(x => !claimed.Contains(x)).ToList();
        PartPeak? Peak(HeatPart p) => s.Peaks.FirstOrDefault(x => x.Part == p);

        string Tile(string label, PartPeak? p)
            => p is null ? $"<div class=\"tile\"><div class=\"tl\">{E(label)}</div><div class=\"tv\">{na}</div></div>"
             : $"<div class=\"tile {Level(p.Part, p.MaxC)}\"><div class=\"tl\">{E(label)}</div><div class=\"tv\">{Lt($"{p.MaxC.ToString("F0", Inv)} °C")}</div>" + (p.During is { } d ? $"<div class=\"td\">{E(w.During)} {E(Short(d))}</div>" : "") + "</div>";
        string Status(IReadOnlyList<SummaryRow> rows)
        {
            var tested = rows.Where(x => x.Outcome is not null).ToList();
            if (tested.Count == 0) return rows.Count > 0 ? $"<span class=\"badge unk\">{E(w.Benchmark)}</span>" : $"<span class=\"badge unk\">{E(w.NotTested)}</span>";
            var o = tested.Any(x => x.Outcome == ReportOutcome.Failed) ? ReportOutcome.Failed : tested.All(x => x.Outcome == ReportOutcome.Passed) ? ReportOutcome.Passed : ReportOutcome.Inconclusive;
            return $"<span class=\"badge {Cls(o)}\">{E(rw.OutcomeName(o))}</span>";
        }
        void Tests(IReadOnlyList<SummaryRow> rows)
        {
            if (rows.Count == 0) return;
            b.Append("<table class=\"res\"><tbody>");
            foreach (var row in rows)
            {
                b.Append("<tr><td class=\"rn\"><b>").Append(E(Short(row.Name))).Append("</b></td><td class=\"figs\">");
                foreach (var f in row.Figures) b.Append("<span class=\"fig\"><small>").Append(E(Short(f.Name))).Append("</small> ").Append(Lt(Figure(f))).Append("</span>");
                b.Append("</td><td class=\"end\">");
                if (row.Outcome is { } o) b.Append("<span class=\"badge ").Append(Cls(o)).Append("\">").Append(E(rw.OutcomeName(o))).Append("</span>");
                b.Append("</td></tr>");
            }
            b.Append("</tbody></table>");
        }
        void Card(string title, string status, string? name, string? facts, string tiles, IReadOnlyList<SummaryRow> rows, string? extra = null, bool wide = false)
        {
            b.Append(wide ? "<article class=\"card wide\">" : "<article class=\"card\">").Append("<div class=\"ch\"><h3>").Append(E(title)).Append("</h3>").Append(status).Append("</div>");
            if (!string.IsNullOrWhiteSpace(name)) b.Append("<div class=\"cn\">").Append(Lt(name)).Append("</div>");
            if (!string.IsNullOrWhiteSpace(facts)) b.Append("<div class=\"cf\">").Append(Lt(facts)).Append("</div>");
            if (tiles.Length > 0) b.Append("<div class=\"tiles\">").Append(tiles).Append("</div>");
            if (extra is not null) b.Append(extra);
            Tests(rows); b.Append("</article>");
        }
        b.Append("<div class=\"cards\">");
        var cpu = m.Cpu; var cpuRows = RowsOf("cpu");
        Card(rw.Cpu, Status(cpuRows), cpu?.Name?.Trim(), cpu is null ? null : string.Join(" · ", new[] { cpu.PhysicalCores is { } c ? $"{c} cores" : null, cpu.LogicalProcessors is { } t ? $"{t} threads" : null, cpu.Socket }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Peak(HeatPart.Cpu) is null ? "" : Tile(w.PartName(HeatPart.Cpu), Peak(HeatPart.Cpu)), cpuRows);
        var gpuRows = RowsOf("gpu");
        string gpus = string.Concat(m.Gpus.Select(g => $"<div class=\"cn\">{Lt(g.Name)}</div><div class=\"cf\">{Lt(string.Join(" · ", new[] { g.AdapterRamBytes is > 0 and var v ? $"{v / 1073741824.0:F0} GB" : null, g.DriverVersion is { } d ? $"driver {d}" : null }.Where(x => x is not null)))}</div>"));
        string gpuTiles = string.Concat(new[] { HeatPart.Gpu, HeatPart.GpuHotSpot, HeatPart.GpuMemory }.Where(p => Peak(p) is not null).Select(p => Tile(w.PartName(p), Peak(p))));
        Card(w.Gpu, Status(gpuRows), null, null, gpuTiles, gpuRows, gpus);
        var ramRows = RowsOf("ram");
        if (m.TotalPhysicalMemoryBytes is { } ramBytes || ramRows.Count > 0)
            Card(w.Ram, Status(ramRows), m.TotalPhysicalMemoryBytes is { } total ? $"{total / 1073741824.0:F0} GB" : null,
                m.MemoryModules.Count > 0 ? $"{m.MemoryModules.Count} × " + string.Join(", ", m.MemoryModules.Select(x => x.ConfiguredSpeedMts ?? x.SpeedMts).OfType<int>().Distinct().Select(x => $"{x} MT/s")) : null, "", ramRows, ModuleList());
        string ModuleList() => string.Concat(m.MemoryModules.Select(x => "<div class=\"drive\"><div><b>" + Lt(string.Join(" ", new[] { x.Manufacturer, x.PartNumber }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()))) + "</b><small>"
            + Lt(string.Join(" · ", new[] { x.Slot, x.CapacityBytes is { } cb ? $"{cb / 1073741824.0:F0} GB" : null, x.TypeName, (x.ConfiguredSpeedMts ?? x.SpeedMts) is { } sp ? $"{sp} MT/s" : null }.Where(v => !string.IsNullOrWhiteSpace(v)))) + "</small></div></div>"));
        var stoRows = RowsOf("storage");
        var drives = new StringBuilder();
        foreach (var d in m.Storage)
        {
            string cls = d.HealthStatus switch { "Healthy" => "ok", "Warning" => "warn", "Unhealthy" => "bad", _ => "unk" };
            string health = w.HealthName(d.HealthStatus) + (DriveAttention.HealthPercent(d.WearPercent) is { } pct ? $" ({pct}%)" : "");
            double? driveMax = r.Sensors.Where(x => x.Role == "StorageTemp" && d.FriendlyName is { Length: > 0 } fn && (x.Hardware.Contains(fn, StringComparison.OrdinalIgnoreCase) || fn.Contains(x.Hardware, StringComparison.OrdinalIgnoreCase))).Select(x => (double?)x.Max).Max();
            string heat = driveMax is { } dm ? $"<span class=\"dt {Level(HeatPart.Drive, dm)}\"><small>{E(w.DriveMax)}</small> {Lt($"{dm.ToString("F0", Inv)} °C")}</span>" : "";
            drives.Append("<div class=\"drive\"><div><b>").Append(Lt(d.FriendlyName ?? "?")).Append("</b><small>")
             .Append(Lt(string.Join(" · ", new[] { d.MediaType, d.BusType, d.SizeBytes is { } sz ? $"{sz / 1_000_000_000} GB" : null }.Where(x => !string.IsNullOrWhiteSpace(x)))))
             .Append("</small></div><div class=\"dr\">").Append(heat).Append("<span class=\"badge ").Append(cls).Append("\">").Append(E(health)).Append("</span></div></div>");
        }
        if (m.Storage.Count == 0) drives.Append("<p class=\"na\">").Append(E(w.NoDrives)).Append("</p>");
        Card(w.Drives, Status(stoRows), null, null, Peak(HeatPart.Drive) is null ? "" : Tile(w.PartName(HeatPart.Drive), Peak(HeatPart.Drive)), stoRows, drives.ToString());
        var psuRows = RowsOf("psu");
        if (s.Power is { } pw)
            Card(w.PsuTitle, Status(psuRows), null, null,
                $"<div class=\"tile big\"><div class=\"tl\">{E(w.PsuTotal)}</div><div class=\"tv\">{Lt($"{pw.Total!.Value.ToString("F0", Inv)} W")}</div><div class=\"td\">" + string.Join(" + ", new[] { pw.CpuW is { } cw ? $"{E(rw.Cpu)} {Lt($"{cw:F0} W")}" : null, pw.GpuW is { } gw ? $"{E(w.Gpu)} {Lt($"{gw:F0} W")}" : null }.OfType<string>()) + "</div></div>", psuRows, $"<div class=\"cf\">{E(w.PsuNote)}</div>", wide: true);
        else if (psuRows.Count > 0) Card(w.PsuTitle, Status(psuRows), null, null, "", psuRows, null, wide: true);
        if (other.Count > 0) Card(w.Tests, Status(other), null, null, "", other, null, wide: true);
        if (Peak(HeatPart.Board) is { } bp) Card(w.PartName(HeatPart.Board), "", null, null, Tile(w.PartName(HeatPart.Board), bp), [], null, wide: true);
        b.Append("</div>");
        if (s.Peaks.Count == 0) b.Append("<p class=\"note\">").Append(E(w.NoTemps)).Append("</p>");
        if (s.FromTrace) b.Append("<p class=\"note\">").Append(E(w.TraceNote)).Append("</p>");

        b.Append("<div class=\"installed\">").Append(E(w.Installed)).Append("</div>");
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
@page{{size:A4;margin:10mm}}
*{{box-sizing:border-box}}
body{{margin:0;background:#eceae3;color:#16161a;font:12px/1.55 {family}}}
main{{max-width:760px;margin:0 auto;background:#fff;padding:18px 22px}}
.lt{{font-family:'Segoe UI',Tahoma,sans-serif;direction:ltr;unicode-bidi:isolate}}
header{{display:flex;justify-content:space-between;align-items:center;gap:12px;border-bottom:3px solid #FDD400;padding-bottom:9px;margin-bottom:12px}}
.brand{{display:flex;align-items:center;gap:11px}} .logo{{height:28px;width:auto}}
h1{{margin:0;font-size:19px;line-height:1.3}} .shop{{color:#55555f;font-size:12px}} .meta{{font-size:11.5px;color:#44444c;text-align:end;line-height:1.6}}
.verdict{{border-radius:8px;padding:8px 14px;font-weight:700;font-size:16px;display:flex;justify-content:space-between;gap:8px;align-items:center;margin-bottom:12px}}
.verdict small{{font-weight:400;color:#44444c;font-size:13px}}
.verdict.Passed{{background:#e6f6ee;color:#0f6b3c}} .verdict.Failed{{background:#fde8e7;color:#a0180f}} .verdict.Incomplete{{background:#fff4d6;color:#8a5a00}} .verdict.Benchmark{{background:#eef1f7;color:#2b3a55}}
section{{border:1px solid #d8d7d0;border-radius:8px;padding:8px 12px;margin-bottom:12px;page-break-inside:avoid}}
h2{{margin:0 0 4px;font-size:13px;border-inline-start:3px solid #FDD400;padding-inline-start:6px}}
.spec{{display:grid;grid-template-columns:1fr;gap:0;margin:0}} .spec div{{display:flex;gap:7px;padding:2px 0;border-bottom:1px dotted #d0cfc8;min-width:0}} .spec div:last-child{{border-bottom:0}}
dt{{color:#55555f;flex:0 0 auto}} dd{{margin:0;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}
.spec dt.sep{{margin-inline-start:12px}} .spec dd.fixed{{flex:0 0 auto}}
.cards{{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:10px}}
.card{{border:1px solid #cfcec6;border-top:3px solid #16161a;border-radius:8px;padding:9px 11px;page-break-inside:avoid;min-width:0}} .card.wide{{grid-column:1/3}}
.ch{{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-bottom:3px}} h3{{margin:0;font-size:14px}}
.cn{{font-weight:700;font-size:12.5px;line-height:1.35;margin-top:2px}} .cf{{color:#55555f;font-size:11px;margin-bottom:2px}}
.tiles{{display:flex;gap:6px;margin:7px 0 5px;flex-wrap:wrap}}
.tile{{flex:1 1 0;min-width:84px;border-radius:7px;padding:4px 8px;border:1px solid #d8d7d0;background:#f7f7f4;text-align:center}}
.tile .tl{{font-size:10.5px;color:#44444c;font-weight:700}} .tile .tv{{font-size:20px;font-weight:700;line-height:1.25}} .tile .td{{font-size:9.5px;color:#55555f;line-height:1.3}}
.tile.ok{{background:#e9f7ef;border-color:#9bd5b4}} .tile.ok .tv{{color:#0f6b3c}} .tile.warn{{background:#fff5da;border-color:#ecd08c}} .tile.warn .tv{{color:#8a5a00}} .tile.bad{{background:#fde8e7;border-color:#f0a39e}} .tile.bad .tv{{color:#a0180f}}
table{{width:100%;border-collapse:collapse;margin-top:4px}} td{{padding:3px 2px;border-bottom:1px dotted #d0cfc8;vertical-align:middle}} tbody tr:last-child td{{border-bottom:0}}
.res td.rn{{width:1%;white-space:nowrap}} .res td.rn b{{font-size:11.5px}} .res td.figs{{padding-inline:8px}} td.end{{text-align:end;white-space:nowrap}}
.fig{{display:inline-block;margin-inline-end:9px;white-space:nowrap}} .fig small{{color:#55555f;font-size:10px}} .fig .lt{{font-weight:700;font-size:11.5px}}
.drive{{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:3px 0;border-bottom:1px dotted #d0cfc8}} .drive small{{display:block;color:#66666e;font-size:10.5px}} .dr{{display:flex;align-items:center;gap:8px}} .dt{{font-weight:700;font-size:12px;border-radius:6px;padding:0 6px;border:1px solid}} .dt small{{display:inline;font-size:9.5px;font-weight:400}} .dt.ok{{color:#0f6b3c;background:#e9f7ef;border-color:#9bd5b4}} .dt.warn{{color:#8a5a00;background:#fff5da;border-color:#ecd08c}} .dt.bad{{color:#a0180f;background:#fde8e7;border-color:#f0a39e}} .tile.big{{flex-basis:100%}} .tile.big .tv{{font-size:26px}} .drive b{{font-size:11.5px}}
.badge{{display:inline-block;border-radius:99px;padding:0 9px;font-weight:700;font-size:10.5px;border:1px solid;white-space:nowrap;line-height:1.6}}
.badge.ok{{background:#e6f6ee;color:#0f6b3c;border-color:#98d4b2}} .badge.warn{{background:#fff4d6;color:#8a5a00;border-color:#efd08a}}
.badge.bad{{background:#fde8e7;color:#a0180f;border-color:#f0a39e}} .badge.unk{{background:#f0f0ee;color:#55555f;border-color:#d0d0cc}}
.note{{margin:2px 0 8px;color:#77777f;font-size:10px}} .na{{color:#77777f;font-weight:400;font-style:italic}} p.na{{margin:2px 0}}
.installed{{border:1.3px solid #FDD400;background:#fffbe6;border-radius:7px;padding:6px 12px;font-weight:700;font-size:12px;margin:4px 0 6px;text-align:center}}
.sign{{display:grid;grid-template-columns:1fr 1fr;gap:28px;margin:6px 0 6px}} .sign div{{border-top:1px solid #16161a;padding-top:3px;font-size:11px;color:#44444c;text-align:center;margin-top:34px}}
footer{{color:#77777f;font-size:9.5px;line-height:1.45;border-top:1px solid #d8d7d0;padding-top:4px}}
main.tight{{font-size:11px;line-height:1.4}} .tight .card{{padding:7px 9px}} .tight .tile .tv{{font-size:17px}} .tight td{{padding:2px}} .tight .sign div{{margin-top:22px}}
main.tightest{{font-size:10px;line-height:1.3}} .tightest .card{{padding:5px 8px}} .tightest .tile .tv{{font-size:15px}} .tightest .tile .td,.tightest .note{{display:none}} .tightest td{{padding:1px 2px}} .tightest .sign div{{margin-top:14px}} .tightest .cards{{gap:6px;margin-bottom:6px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}";
    }
}
