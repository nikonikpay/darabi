namespace Mazesta.Reporting;

/// <summary>The report's wording in one language, with its direction. Persian is the default; the app passes the language it runs in.
/// Component names and measured values stay in English/Latin in both (spec §7.1).</summary>
public sealed class ReportText
{
    public required string Language { get; init; }
    public bool IsRtl => Language == "fa";
    public required string Title, BenchmarkTitle, Verdict, Results, Sensors, BenchmarkSensors, Benchmarks, Metric, MeasuredAt, Value, Machine,
        Name, Outcome, Duration, Errors, Detail, Options, Sensor, Min, Avg, Max, Samples, Total, Passed, Failed, NotDone,
        Started, Finished, ReportId, NoSensors, Cpu, Gpu, Ram, Board, Bios, Storage, Network, Os, Footer, MeasurementsOnly,
        VerdictPassed, VerdictFailed, VerdictIncomplete, OutcomePassed, OutcomeFailed, OutcomeCancelled, OutcomeUnsupported, OutcomeNotRun;

    public string TitleOf(ReportKind kind) => kind == ReportKind.Benchmark ? BenchmarkTitle : Title;
    public string VerdictName(ReportVerdict v) => v switch { ReportVerdict.Passed => VerdictPassed, ReportVerdict.Failed => VerdictFailed, _ => VerdictIncomplete };
    public string OutcomeName(ReportOutcome o) => o switch
    {
        ReportOutcome.Passed => OutcomePassed, ReportOutcome.Failed => OutcomeFailed, ReportOutcome.Cancelled => OutcomeCancelled, ReportOutcome.Unsupported => OutcomeUnsupported, _ => OutcomeNotRun
    };

    public static ReportText For(string language) => language == "en" ? English : Persian;

    public static readonly ReportText Persian = new()
    {
        Language = "fa", Title = "گزارش آزمون و بررسی سیستم", BenchmarkTitle = "گزارش بنچمارک", Verdict = "نتیجه‌ی کلی", Results = "نتایج آزمون‌ها", Sensors = "اندازه‌گیری حین آزمون",
        BenchmarkSensors = "اندازه‌گیری حین بنچمارک", Benchmarks = "بنچمارک‌ها", Metric = "معیار", MeasuredAt = "زمان اندازه‌گیری", Value = "مقدار", Machine = "مشخصات سیستم",
        Name = "آزمون", Outcome = "وضعیت", Duration = "مدت", Errors = "خطاها", Detail = "شواهد اندازه‌گیری", Options = "تنظیمات",
        Sensor = "سنسور", Min = "کمینه", Avg = "میانگین", Max = "بیشینه", Samples = "نمونه", Total = "کل آزمون‌ها", Passed = "موفق", Failed = "ناموفق", NotDone = "انجام‌نشده",
        Started = "شروع", Finished = "پایان", ReportId = "شناسه‌ی گزارش", NoSensors = "در بازه‌ی آزمون هیچ سنسوری ثبت نشد.",
        Cpu = "پردازنده", Gpu = "کارت گرافیک", Ram = "حافظه‌ی RAM", Board = "مادربرد", Bios = "بایوس", Storage = "ذخیره‌سازی", Network = "شبکه", Os = "سیستم‌عامل",
        Footer = "همه‌ی مقادیر این گزارش از اندازه‌گیری واقعی همین دستگاه آمده‌اند؛ سنسور یا داده‌ای که در دسترس نبوده، نشان داده نشده است.",
        MeasurementsOnly = "فقط اندازه‌گیری سرعت؛ بنچمارک قبول یا رد ندارد و آزمون سلامت نیست",
        VerdictPassed = "همه‌ی آزمون‌های انجام‌شده موفق بودند", VerdictFailed = "دست‌کم یک آزمون ناموفق بود؛ سیستم نیاز به بررسی دارد", VerdictIncomplete = "آزمون‌ها کامل انجام نشد؛ نتیجه‌ی قطعی نیست",
        OutcomePassed = "موفق", OutcomeFailed = "ناموفق", OutcomeCancelled = "لغو شد", OutcomeUnsupported = "پشتیبانی نمی‌شود", OutcomeNotRun = "اجرا نشده"
    };

    public static readonly ReportText English = new()
    {
        Language = "en", Title = "System test and inspection report", BenchmarkTitle = "Benchmark report", Verdict = "Overall result", Results = "Test results", Sensors = "Measured during the tests",
        BenchmarkSensors = "Measured during the benchmark", Benchmarks = "Benchmarks", Metric = "Metric", MeasuredAt = "Measured at", Value = "Value", Machine = "System specification",
        Name = "Test", Outcome = "Result", Duration = "Duration", Errors = "Errors", Detail = "Measured evidence", Options = "Settings",
        Sensor = "Sensor", Min = "Min", Avg = "Avg", Max = "Max", Samples = "Samples", Total = "Tests", Passed = "Passed", Failed = "Failed", NotDone = "Not done",
        Started = "Started", Finished = "Finished", ReportId = "Report ID", NoSensors = "No sensor was recorded during the tests.",
        Cpu = "Processor", Gpu = "Graphics card", Ram = "Memory (RAM)", Board = "Motherboard", Bios = "BIOS", Storage = "Storage", Network = "Network", Os = "Operating system",
        Footer = "Every value in this report was measured on this machine; a sensor or value that was not available is not shown.",
        MeasurementsOnly = "Speed measurements only: a benchmark has no pass or fail and is not a health test",
        VerdictPassed = "Every test that ran passed", VerdictFailed = "At least one test failed; the system needs attention", VerdictIncomplete = "The tests were not completed; there is no definite result",
        OutcomePassed = "Passed", OutcomeFailed = "Failed", OutcomeCancelled = "Cancelled", OutcomeUnsupported = "Not supported", OutcomeNotRun = "Not run"
    };
}
