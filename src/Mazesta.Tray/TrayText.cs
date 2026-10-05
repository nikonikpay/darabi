using System.Globalization; using Mazesta.Core.Health; using Mazesta.Persistence;
namespace Mazesta.Tray;

/// <summary>Persian wording for the tray (the English pass comes with the app's second language). Values keep Latin digits, as in the app.</summary>
internal static class TrayText
{
    public const string Title = "مازستا — پایش سلامت";
    public const string OpenApp = "باز کردن مازستا";
    public const string Summary = "خلاصه‌ی وضعیت";
    public const string CheckNow = "بررسی همین حالا";
    public const string Checking = "در حال بررسی…";
    public const string Exit = "خروج";
    public const string NoApp = "برنامه‌ی مازستا کنار پایشگر پیدا نشد.";
    public const string SummaryTitle = "مازستا — خلاصه‌ی وضعیت سیستم";
    public const string Cpu = "دمای پردازنده";
    public const string Gpu = "دمای کارت گرافیک";
    public const string Drives = "سلامت دیسک‌ها";
    public const string Recent = "آخرین بررسی‌ها";
    public const string NoChecks = "هنوز بررسی‌ای انجام نشده است.";
    public const string NoDrives = "هنوز سلامت دیسک‌ها خوانده نشده است.";
    public const string NotAvailable = "در دسترس نیست";
    public const string Temps = "دما";
    public const string Health = "سلامت دیسک";
    public const string AllGood = "مشکلی دیده نشد";
    public const string CheckFailed = "بررسی انجام نشد";

    public static string Alert(HealthAlert a) => a.Kind switch
    {
        HealthAlertKind.CpuOverheat => $"دمای پردازنده به {Num(a.Value, "0.#")}°C رسیده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.GpuOverheat => $"دمای کارت گرافیک به {Num(a.Value, "0.#")}°C رسیده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.CpuThrottle => $"فرکانس پردازنده در بار کامل به {Num(a.Value, "0")} مگاهرتز افت کرده است. دما و محدودیت توان را بررسی کنید.",
        _ => a.Kind.ToString()
    };

    public const string HotSpot = "نقطه‌ی داغ";

    public const string Overlay = "نمایش اورلی روی صفحه";
    public const string GpuProfiles = "پروفایل کارت گرافیک";
    public const string Lights = "نورپردازی RGB";
    public const string LightsOff = "خاموش کردن همه‌ی نورها";
    public const string LightsApply = "اعمال تنظیمات ذخیره‌شده";
    public const string LightsOn = "نورها با تنظیمات ذخیره‌شده روشن شدند.";
    public const string LightsNoProgram = "OpenRGB کنار مازستا پیدا نشد.";
    public const string LightsNoServer = "سرور نورپردازی بالا نیامد.";
    public const string Reading = "در حال خواندن…";
    public const string Stock = "حالت کارخانه (بدون تغییر)";
    public const string NoNvidia = "کارت گرافیک NVIDIA پیدا نشد";
    public const string NoProfiles = "هنوز پروفایلی ذخیره نشده (صفحه‌ی اورکلاک و آندرولت)";
    public const string JournalFound = "یک تنظیم خودکار کارت گرافیک در برنامه نیمه‌کاره مانده است؛ پروفایل هنگام ورود اعمال نشد. برنامه‌ی مازستا را باز کنید.";
    public static string Kind(GpuProfileKind k) => k switch { GpuProfileKind.Undervolt => "آندرولت", GpuProfileKind.Overclock => "اورکلاک", _ => "دستی" };
    public static string ProfileApplied(string name, string gpu) => $"«{name}» روی {gpu} اعمال شد و هنگام ورود به ویندوز هم اعمال می‌شود.";
    public static string ProfileFailed(string name, string why) => $"«{name}» اعمال نشد: {why}";
    public static string ProfileNotKept(string why) => $"پروفایل اعمال شد ولی برای ورود بعدی ذخیره نشد: {why}";
    public static string ProfileMissing(string name) => $"پروفایل «{name}» دیگر وجود ندارد؛ کارت در حالت کارخانه ماند.";

    public static string DriveProblem(TrayDrive d) => d.Status is "Warning" or "Unhealthy"
        ? $"دیسک {d.Name}: ویندوز از روی SMART آن را «{Status(d.Status)}» گزارش کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید."
        : $"دیسک {d.Name}: خطای اصلاح‌نشده‌ی خواندن یا نوشتن ثبت کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید.";

    public static string Status(string? status) => status switch
    {
        "Healthy" => "سالم", "Warning" => "هشدار", "Unhealthy" => "ناسالم", null => NotAvailable, _ => "نامشخص"
    };

    /// <summary>The verdict with the life left in brackets when the drive has a wear counter: «سالم (98%)».</summary>
    public static string DriveHealth(TrayDrive d) => Status(d.Status) + (DriveAttention.HealthPercent(d.WearPercent) is { } p ? $" ({p}%)" : "");

    public static string Tooltip(TrayCheck? temps)
    {
        if (temps is null) return Title;
        string Part(string name, double? v) => $"{name} {(v is { } x ? Num(x, "0") + "°C" : "—")}";
        var text = $"مازستا · {Part("CPU", temps.CpuTempC)} · {Part("GPU", temps.GpuTempC)}";
        return text.Length > 120 ? text[..120] : text;   // NotifyIcon.Text is limited to 127 characters
    }

    public static string Status(DateTimeOffset? last, string? problem) => problem ?? (last is null ? "هنوز بررسی نشده" : $"آخرین بررسی: {Time(last.Value)}");
    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
}
