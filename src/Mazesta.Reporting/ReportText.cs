namespace Mazesta.Reporting;

/// <summary>Persian wording of the report (the report follows the app's language; English is added with the app's second language).</summary>
internal static class ReportText
{
    public const string Title = "گزارش آزمون و بررسی سیستم", Verdict = "نتیجه‌ی کلی", Results = "نتایج آزمون‌ها", Sensors = "اندازه‌گیری حین آزمون", Machine = "مشخصات سیستم",
        Name = "آزمون", Outcome = "وضعیت", Duration = "مدت", Errors = "خطاها", Detail = "شواهد اندازه‌گیری", Options = "تنظیمات",
        Sensor = "سنسور", Min = "کمینه", Avg = "میانگین", Max = "بیشینه", Samples = "نمونه", Total = "کل آزمون‌ها", Passed = "موفق", Failed = "ناموفق", NotDone = "انجام‌نشده",
        Started = "شروع", Finished = "پایان", ReportId = "شناسه‌ی گزارش", NoSensors = "در بازه‌ی آزمون هیچ سنسوری ثبت نشد.",
        Cpu = "پردازنده", Gpu = "کارت گرافیک", Ram = "حافظه‌ی RAM", Board = "مادربرد", Bios = "بایوس", Storage = "ذخیره‌سازی", Network = "شبکه", Os = "سیستم‌عامل",
        Footer = "همه‌ی مقادیر این گزارش از اندازه‌گیری واقعی همین دستگاه آمده‌اند؛ سنسور یا داده‌ای که در دسترس نبوده، نشان داده نشده است.";

    public static string OutcomeName(ReportOutcome o) => o switch
    {
        ReportOutcome.Passed => "موفق", ReportOutcome.Failed => "ناموفق", ReportOutcome.Cancelled => "لغو شد", ReportOutcome.Unsupported => "پشتیبانی نمی‌شود", _ => "اجرا نشده"
    };
    public static string VerdictName(ReportVerdict v) => v switch
    {
        ReportVerdict.Passed => "همه‌ی آزمون‌های انجام‌شده موفق بودند",
        ReportVerdict.Failed => "دست‌کم یک آزمون ناموفق بود؛ سیستم نیاز به بررسی دارد",
        _ => "آزمون‌ها کامل انجام نشد؛ نتیجه‌ی قطعی نیست"
    };
}
