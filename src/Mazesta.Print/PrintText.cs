using System.Globalization;
namespace Mazesta.Print;

/// <summary>The program's words in Persian (the secretary's language) or English; a small window with few words, so a table here rather than resource files.</summary>
internal static class PrintText
{
    public static readonly bool English = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    private static string T(string fa, string en) => English ? en : fa;

    public static string Title => T("چاپ گزارش‌های مازستا", "Mazesta report printing");
    public static string Refresh => T("تازه‌سازی", "Refresh");
    public static string Settings => T("کلید اتصال", "Connection key");
    public static string Search => T("جستجوی شماره‌ی سرویس، مدل یا سیستم…", "Search service number, model or system…");
    public static string Service => T("شماره‌ی سرویس", "Service no.");
    public static string Date => T("تاریخ گزارش", "Report date");
    public static string Device => T("مدل دستگاه", "Device model");
    public static string Laptop => T("لپ‌تاپ", "Laptop");
    public static string Machine => T("سیستم", "System");
    public static string Result => T("نتیجه", "Result");
    public static string Print => T("چاپ خلاصه", "Print summary");
    public static string SavePdf => T("ذخیره PDF", "Save PDF");
    public static string Paper => T("کاغذ:", "Paper:");
    public static string LayoutName(SheetLayout l) => l switch
    {
        SheetLayout.A5 => T("A5 عمودی", "A5 portrait"),
        SheetLayout.A4Landscape => T("A4 افقی (یک برگه)", "A4 landscape (one sheet)"),
        _ => T("A4 افقی (دو برگه کنار هم)", "A4 landscape (two sheets side by side)"),
    };
    public static string NoService => T("بدون شماره", "no number");
    public static string Loading => T("در حال دریافت…", "Loading…");
    public static string Empty => T("گزارشی روی سایت نیست.", "The site holds no reports.");
    public static string NoMatch => T("گزارشی با این جستجو پیدا نشد.", "No report matches.");
    public static string PickOne => T("یک گزارش را از فهرست انتخاب کنید.", "Choose a report from the list.");
    public static string Updated(DateTime at, int count) => English ? $"{count} reports · updated {at:HH:mm}" : $"{count} گزارش · به‌روزرسانی {at:HH:mm}";
    public static string Failed(string why) => T("دریافت نشد: ", "Could not load: ") + why;
    public static string NeedKey => T("برای دیدن گزارش‌ها «کلید اتصال» را وارد کنید (از افزونه‌ی سایت، بخش «کلید خواندن گزارش‌ها»).",
        "Enter the connection key to see the reports (from the site plugin, “reading key”).");
    public static string KeyTitle => T("کلید اتصال به سایت", "Site connection key");
    public static string KeyHelp => T("کلید خواندن گزارش‌ها را از صفحه‌ی افزونه‌ی مازستا در سایت کپی و اینجا بچسبانید. با این کلید فقط می‌شود گزارش‌ها را دید و چاپ کرد.",
        "Paste the reading key from the Mazesta plugin's page on the site. It can only read and print reports.");
    public static string KeyBad => T("این شبیه کلید سایت نیست (با mz_ شروع می‌شود).", "That does not look like a site key (it starts with mz_).");
    public static string Save => T("ذخیره", "Save");
    public static string Cancel => T("انصراف", "Cancel");
    public static string SaveDialog => T("ذخیره‌ی خلاصه‌ی گزارش به‌صورت PDF", "Save the summary as PDF");
    public static string Saved(string path) => T("ذخیره شد: ", "Saved: ") + path;
    public static string WrongKey => T("سایت کلید را نپذیرفت.", "The site did not accept the key.");
    public static string NoBrowser => T("مرورگر داخلی (WebView2) در این سیستم نصب نیست.", "The built-in browser (WebView2) is not installed on this computer.");

    public static string Verdict(string? verdict, string kind) => verdict switch
    {
        "Passed" => T("موفق", "Passed"), "Failed" => T("ناموفق", "Failed"), "Incomplete" => T("ناقص", "Incomplete"),
        _ => kind == "Benchmark" ? T("بنچمارک", "Benchmark") : "—",
    };
}
