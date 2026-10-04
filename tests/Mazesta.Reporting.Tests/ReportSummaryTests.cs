using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class ReportSummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly StorageDeviceInfo Nvme = new("MSI M390 1TB", "S1", "SSD", "NVMe", 1_000_204_886_016, "EDFM00.1", "Healthy", 3);
    private static readonly StorageDeviceInfo Hdd = new("WDC WD20PURZ", "W2", "HDD", "SATA", 2_000_398_934_016, "01.01A01", "Healthy");
    private static TestEntry Test(string name, ReportOutcome o, int fromMin, int toMin) => new(name, name, o, T0.AddMinutes(fromMin), T0.AddMinutes(toMin), (toMin - fromMin) * 60, 0, null, new Dictionary<string, string>());
    // A trace sample every minute from T0: the CPU peaks in the first test, the GPU at 71 in the second.
    private static SensorSummary Sensor(string id, string hw, string name, string? role, params double[] perMinute)
        => new(id, hw, name, "Temperature", "°C", perMinute.Min(), perMinute.Average(), perMinute.Max(), perMinute.Length, [.. perMinute.Select((v, i) => new TracePoint(i * 60, v))], role);
    private static SessionReport Report(IReadOnlyList<WindowPeak>? peaks = null) => SessionReport.Create("مازستا", "1.0.0", T0.AddMinutes(20),
        [Test("CPU stress", ReportOutcome.Passed, 0, 5), Test("GPU 3-D scene", ReportOutcome.Failed, 6, 10), Test("Memory", ReportOutcome.NotRun, 10, 10)],
        [Sensor("cpu", "Ryzen 9 3950X", "Core (Tctl/Tdie)", "CpuTctlTdie", 60, 70, 88, 80, 74, 60, 55, 62, 64, 66, 60),
         Sensor("pkg", "Ryzen 9 3950X", "CPU Package", "CpuPackageTemp", 58, 68, 86, 78, 72, 58, 53, 60, 62, 64, 58),
         Sensor("gpu", "GTX 950", "GPU Core", null, 40, 41, 42, 43, 44, 45, 50, 60, 71, 65, 50),
         Sensor("load", "GTX 950", "GPU Core", null, 1, 2) with { Kind = "Load" }],
        HardwareInventory.Empty with { Storage = [Nvme, Hdd] }, serviceNumber: "S-1405-0042") with { Peaks = peaks };

    /// <summary>A long service job: sixteen tests, two graphics cards, five drives and every part's temperature.</summary>
    private static SessionReport Long()
    {
        var tests = Enumerable.Range(0, 16).Select(i => Test($"Test number {i} with a longer name", i % 5 == 4 ? ReportOutcome.Failed : ReportOutcome.Passed, i, i + 1)).ToArray();
        var drives = Enumerable.Range(0, 5).Select(i => Nvme with { FriendlyName = $"Samsung SSD 990 PRO 2TB #{i}", SerialNumber = $"S{i}" }).ToList();
        var machine = HardwareInventory.Empty with
        {
            Storage = drives, Cpu = new CpuInfo("AMD Ryzen 9 7950X3D 16-Core Processor", Mazesta.Core.Hardware.HardwareVendor.Amd, 16, 32, 4200, "AM5"),
            Gpus = [new GpuInfo("NVIDIA GeForce RTX 4090", "32.0.15.6094", 24L << 30, null), new GpuInfo("AMD Radeon(TM) Graphics", "31.0.24002.92", 512L << 20, null)],
        };
        return SessionReport.Create("مازستا", "1.0.0", T0.AddMinutes(20), tests,
            [Sensor("pkg", "Ryzen 9 7950X3D", "CPU Package", "CpuPackageTemp", 58, 68, 86), Sensor("gpu", "RTX 4090", "GPU Core", "GpuCoreTemp", 40, 71, 60),
             Sensor("hot", "RTX 4090", "GPU Hot Spot", "GpuHotSpotTemp", 50, 82, 70), Sensor("mem", "RTX 4090", "GPU Memory", "GpuVramTemp", 50, 76, 70),
             Sensor("ssd", "Samsung SSD 990 PRO 2TB", "Composite", "StorageTemp", 40, 52, 48), Sensor("mb", "X670E", "Motherboard", "BoardTemp", 30, 41, 38)],
            machine, serviceNumber: "S-1405-0042");
    }

    [Fact] public void A_benchmark_shows_its_own_figures_and_none_of_the_conditions_it_ran_in()
    {
        var net = new BenchmarkEntry("bench.network.internet", "Internet", T0.AddMinutes(1), [new("Download", 480, "Mbps", "Bench_Net_Download"), new("Upload", 95.5, "Mbps", "Bench_Net_Upload"),
            new("Ping", 12.3, "ms", "Bench_Net_Ping"), new("GPU temperature", 53, "°C", "Bench_Gpu_TempMax")], null, T0);
        var r = SessionReport.CreateBenchmark("x", "1", T0.AddMinutes(2), [net], [Sensor("gpu", "RTX", "GPU Core", "GpuCoreTemp", 50, 53)], HardwareInventory.Empty, "S-1");
        var row = ReportSummary.Of(r).Rows.Single();
        Assert.Equal(["Download", "Upload", "Ping"], row.Figures.Select(f => f.Name));
        string html = SummaryHtml.Write(ReportSummary.Of(r));
        Assert.Contains("480 Mbps", html); Assert.Contains("95.5 Mbps", html); Assert.DoesNotContain("GPU temperature", html); Assert.Contains(SummaryText.Persian.Installed, html);
    }
    [Fact] public void A_benchmark_line_holds_three_figures_at_most_and_names_lose_their_brackets()
    {
        var mem = new BenchmarkEntry("bench.memory", "پهنای‌باند حافظه (نوشتن، خواندن)", T0.AddMinutes(1), [new("Write", 10.3, "GB/s", "Bench_Mem_Write"), new("Read", 13.1, "GB/s", "Bench_Mem_Read"),
            new("Copy", 13.2, "GB/s", "Bench_Mem_Copy"), new("Latency (RAM)", 119, "ns", "Bench_Mem_Latency")], null, T0);
        var r = SessionReport.CreateBenchmark("x", "1", T0.AddMinutes(2), [mem], [], HardwareInventory.Empty, "S-1");
        var row = ReportSummary.Of(r).Rows.Single();
        Assert.Equal(["Write", "Read", "Latency (RAM)"], row.Figures.Select(f => f.Name));
        Assert.Equal("پهنای‌باند حافظه", SummaryHtml.Short(row.Name)); Assert.Equal("Latency", SummaryHtml.Short("Latency (RAM)"));
        string html = SummaryHtml.Write(ReportSummary.Of(r));
        Assert.Single(html.Split("<tr>").Skip(1), x => x.Contains("class=\"rn\""));   // one table row for the one benchmark
    }
    [Fact] public void A_full_benchmark_report_renders_on_one_sheet()
    {
        BenchmarkEntry B(string name, params (string N, double V, string U, string K)[] m) => new("b." + name, name, T0.AddMinutes(1), [.. m.Select(x => new BenchmarkMetricEntry(x.N, x.V, x.U, x.K))], null, T0);
        var entries = new[]
        {
            B("پردازنده — تک‌رشته", ("توان محاسباتی اعشاری (ماتریس FP64)", 1.58, "GFLOPS", "Bench_Cpu_Gflops")),
            B("پردازنده — چندرشته (همه‌ی رشته‌ها)", ("توان محاسباتی اعشاری (ماتریس FP64)", 23.1, "GFLOPS", "Bench_Cpu_Gflops"), ("به‌ازای هر رشته", 0.72, "GFLOPS", "Bench_Cpu_PerThread")),
            B("پهنای‌باند حافظه", ("نوشتن (تک‌رشته)", 10.3, "GB/s", "Bench_Mem_Write"), ("خواندن (تک‌رشته)", 13.1, "GB/s", "Bench_Mem_Read"), ("کپی", 13.2, "GB/s", "Bench_Mem_Copy"), ("تأخیر دسترسی (۲۵۶ مگابایت، خود رم)", 119, "ns", "Bench_Mem_Latency")),
            B("گرافیک — رندر Direct3D 12", ("نرخ فریم صحنه (۲۵۶×۱۴۴۰)", 1964, "FPS", "Bench_Gpu_Fps"), ("توان پردازش مثلث", 10.3, "Gtri/s", "Bench_Gpu_Triangles")),
            B("گرافیک — ری‌تریسینگ (DXR)", ("نرخ فریم ری‌تریسینگ (۲۵۶×۱۴۴۰)", 1294, "FPS", "Bench_Gpu_Rt_Fps"), ("تعداد پرتو در ثانیه", 11.5, "Grays/s", "Bench_Gpu_Rt_Rays")),
            B("گرافیک — باغ ایرانی (ری‌تریسینگ)", ("میانگین نرخ فریم در باغ (۲۵۶×۱۴۴۰)", 14.1, "FPS", "Bench_Gpu_Scene_Fps"), ("نرخ فریم ۱٪ کندترین فریم‌ها", 10.7, "FPS", "Bench_Gpu_Scene_Low"), ("زمان فریم صدک ۹۹", 91.3, "ms", "Bench_Gpu_Scene_P99")),
            B("گرافیک — هوش مصنوعی (DirectML: FP32, FP16, INT8)", ("FP32 (دقت کامل)", 14.7, "TFLOPS", "Bench_Gpu_Ai_Fp32"), ("FP16 (نیم‌دقت)", 105, "TFLOPS", "Bench_Gpu_Ai_Fp16"), ("INT8 (کوانتیزه)", 12.1, "TOPS", "Bench_Gpu_Ai_Int8")),
            B("ذخیره‌سازی (ترتیبی و تصادفی، بدون کش)", ("خواندن پیوسته (SEQIM QAT1)", 2882, "MB/s", "Bench_Storage_SeqRead"), ("نوشتن پیوسته", 1277, "MB/s", "Bench_Storage_SeqWrite"), ("خواندن تصادفی 4K (QD32)", 173801, "IOPS", "Bench_Storage_Rand4kQ32Read"), ("تأخیر 4K", 116, "µs", "Bench_Storage_Rand4kLatency")),
            B("شبکه — سرعت اینترنت (speed.cloudflare.com)", ("دانلود", 120, "Mbps", "Bench_Net_Download"), ("آپلود", 77.2, "Mbps", "Bench_Net_Upload"), ("تأخیر (پینگ به 1.1.1.1)", 76.3, "ms", "Bench_Net_Ping"), ("نوسان تأخیر (Jitter)", 0.36, "ms", "Bench_Net_Jitter")),
        };
        var machine = HardwareInventory.Empty with
        {
            Cpu = new CpuInfo("AMD Ryzen 9 3950X 16-Core Processor", Mazesta.Core.Hardware.HardwareVendor.Amd, 16, 32, 3500, "AM4"), Gpus = [new GpuInfo("NVIDIA GeForce RTX 3090", "32.0.15.6094", 24L << 30, null)],
            Motherboard = new MotherboardInfo("ASUSTeK COMPUTER INC.", "PRIME B550M-A", null, null), Bios = new BiosInfo("ASUS", "3404", new DateTime(2023, 10, 7), null), TotalPhysicalMemoryBytes = 64L << 30, Storage = [Nvme, Hdd],
        };
        var r = SessionReport.CreateBenchmark("مازستا", "0.8.0", T0.AddMinutes(20), entries, [Sensor("pkg", "Ryzen", "CPU Package", "CpuPackageTemp", 58, 80), Sensor("gpu", "RTX", "GPU Core", "GpuCoreTemp", 40, 77),
            Sensor("hot", "RTX", "GPU Hot Spot", "GpuHotSpotTemp", 50, 89), Sensor("ssd", "WDC", "Composite", "StorageTemp", 40, 50)], machine, "3550");
        var s = ReportSummary.Of(r);
        Assert.Equal("tight", SummaryHtml.Fit(s));
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir) File.WriteAllText(Path.Combine(dir, "summary-bench.html"), SummaryHtml.Write(s));
    }
    [Fact] public void The_board_line_carries_the_bios_and_the_memory_has_its_own_line()
    {
        var machine = HardwareInventory.Empty with
        {
            Motherboard = new MotherboardInfo("ASUSTeK", "PRIME B550M-A", null, null), Bios = new BiosInfo("ASUS", "3404", new DateTime(2023, 10, 7), null), TotalPhysicalMemoryBytes = 64L << 30,
        };
        string html = SummaryHtml.Write(ReportSummary.Of(Report() with { Machine = machine }), wording: SummaryText.English);
        int board = html.IndexOf("PRIME B550M-A"), bios = html.IndexOf("3404"), ram = html.IndexOf("64 GB");
        Assert.True(board > 0 && bios > board && ram > bios);
        Assert.Equal(board > 0 ? html.LastIndexOf("<div", board) : -1, html.LastIndexOf("<div", bios));   // the same line
    }
    [Fact] public void A_long_report_is_set_tighter_so_the_sheet_stays_one_page()
    {
        Assert.Equal("", SummaryHtml.Fit(ReportSummary.Of(Report())));
        var s = ReportSummary.Of(Long());
        Assert.Equal("tightest", SummaryHtml.Fit(s));
        string html = SummaryHtml.Write(s);
        Assert.Contains("<main class=\"tightest\">", html);
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir)
        {
            File.WriteAllText(Path.Combine(dir, "summary-long.html"), html);
            File.WriteAllText(Path.Combine(dir, "summary-short.html"), SummaryHtml.Write(ReportSummary.Of(Report())));
            var mid = Long() with { Tests = [.. Long().Tests.Take(9)], Machine = Long().Machine with { Storage = [.. Long().Machine.Storage.Take(3)] } };
            File.WriteAllText(Path.Combine(dir, "summary-mid.html"), SummaryHtml.Write(ReportSummary.Of(mid)));
        }
    }

    [Fact] public void Each_test_shows_the_highest_temperature_reached_while_it_ran_from_the_trace_of_an_older_report()
    {
        var s = ReportSummary.Of(Report());
        Assert.True(s.FromTrace);
        Assert.Equal(["CPU stress", "GPU 3-D scene"], s.Rows.Select(r => r.Name));   // a test that never ran has no temperatures to show
        Assert.Equal(86, s.Rows[0].Peaks[HeatPart.Cpu]);   // the package sensor, not Tctl/Tdie beside it
        Assert.Equal(71, s.Rows[1].Peaks[HeatPart.Gpu]); Assert.Equal(45, s.Rows[0].Peaks[HeatPart.Gpu]);
        var gpu = s.Peaks.Single(p => p.Part == HeatPart.Gpu);
        Assert.Equal((71.0, "GPU 3-D scene", "GTX 950"), (gpu.MaxC, gpu.During, gpu.Device));
        Assert.Equal([HeatPart.Cpu, HeatPart.Gpu], s.Columns);
    }

    [Fact] public void Recorded_peaks_are_used_over_the_trace_when_the_report_has_them()
    {
        var s = ReportSummary.Of(Report([new(T0, T0.AddMinutes(5), "pkg", 91.5)]));
        Assert.False(s.FromTrace); Assert.Equal(91.5, s.Rows[0].Peaks[HeatPart.Cpu]);
        Assert.False(s.Rows[1].Peaks.ContainsKey(HeatPart.Cpu));   // no recorded peak in that window: absent, never 0 or a guess
    }

    [Fact] public void The_sheet_is_a5_right_to_left_with_the_verdict_peaks_and_drive_health_percent()
    {
        string html = SummaryHtml.Write(ReportSummary.Of(Report()));
        Assert.Contains("dir=\"rtl\"", html); Assert.Contains("size:A5", html);
        Assert.Contains(ReportText.Persian.VerdictFailed, html); Assert.Contains("86 °C", html); Assert.Contains("71 °C", html);
        Assert.Contains("سالم (97%)", html); Assert.Contains(">سالم<", html);   // an HDD has no wear counter: no percentage invented
        Assert.Contains("S-1405-0042", html); Assert.Contains(SummaryText.Persian.TraceNote, html);
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("http", html.Replace("http-equiv", ""));
    }

    [Fact] public void A_report_without_temperatures_says_so() =>
        Assert.Contains(SummaryText.Persian.NoTemps, SummaryHtml.Write(ReportSummary.Of(SessionReport.Create("x", "1", T0, [], [], HardwareInventory.Empty))));

    [Fact] public void English_wording_is_left_to_right() => Assert.Contains("dir=\"ltr\"", SummaryHtml.Write(ReportSummary.Of(Report()), wording: SummaryText.English));
}
