using Mazesta.Core.Health;
namespace Mazesta.Tray;

/// <summary>Persian wording for the tray (the English pass comes with the app's second language).</summary>
internal static class TrayText
{
    public const string Title = "مازستا — پایش سلامت";
    public const string CheckNow = "بررسی همین حالا";
    public const string Exit = "خروج";
    public static string Alert(HealthAlert a) => a.Kind switch
    {
        HealthAlertKind.CpuOverheat => $"دمای پردازنده به {a.Value:0.#}°C رسیده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.GpuOverheat => $"دمای کارت گرافیک به {a.Value:0.#}°C رسیده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.CpuThrottle => $"فرکانس پردازنده در بار کامل به {a.Value:0} مگاهرتز افت کرده است. دما و محدودیت توان را بررسی کنید.",
        _ => a.Kind.ToString()
    };
    public static string Status(DateTimeOffset? last, string? problem) => problem ?? (last is null ? "هنوز بررسی نشده" : $"آخرین بررسی: {last.Value.ToLocalTime():HH:mm}");
}
