using System.Globalization;
namespace Mazesta.Setup;

/// <summary>The installer's words, Persian or English by the system's language.</summary>
internal static class SetupText
{
    public static readonly bool English = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    private static string T(string fa, string en) => English ? en : fa;
    public static string Title => T("نصب مازستا تست", "Install Mazesta Test");
    public static string Lede => T("مازستا تست دما، بار و سلامت قطعات سیستم را زنده نشان می‌دهد و آزمون و بنچمارک دارد. برنامه در پوشه‌ی زیر نصب می‌شود.",
        "Mazesta Test shows your PC's temperatures, load and health live and has tests and benchmarks. It will be installed in the folder below.");
    public static string Folder => T("پوشه‌ی نصب", "Install folder");
    public static string Browse => T("انتخاب…", "Browse…");
    public static string Desktop => T("میان‌بر روی دسکتاپ بساز", "Create a desktop shortcut");
    public static string Launch => T("بعد از نصب برنامه را باز کن", "Open the program when done");
    public static string Install => T("نصب", "Install");
    public static string Update => T("به‌روزرسانی", "Update");
    public static string Close => T("بستن", "Close");
    public static string Cancel => T("انصراف", "Cancel");
    public static string UpdateNote => T("نسخه‌ای از قبل نصب شده است؛ فایل‌های برنامه جایگزین می‌شود و اطلاعات شما (پوشه‌ی Data) می‌ماند.",
        "A copy is already installed: its files are replaced and your data (the Data folder) is kept.");
    public static string BadFolder => T("پوشه‌ی نصب درست نیست (مسیر کامل یک پوشه‌ی تازه را بدهید، نه ریشه‌ی یک درایو).", "That is not a usable folder (give the full path of a new folder, not a drive's root).");
    public static string NotEmpty => T("این پوشه خالی نیست و برنامه‌ی ما در آن نصب نشده؛ برای امنیت، چیزی در آن نصب نمی‌شود. پوشه‌ی دیگری انتخاب کنید.",
        "This folder is not empty and does not hold our program, so nothing is installed into it. Choose another folder.");
    public static string Running => T("مازستا تست در حال اجراست. آن را ببندید (نوار کنار ساعت هم) و دوباره نصب را بزنید.", "Mazesta Test is running. Close it (also in the tray) and install again.");
    public static string NoPayload => T("این فایل نصب ناقص است (برنامه داخلش نیست).", "This installer is incomplete (the program is not inside it).");
    public static string Replacing => T("آماده‌سازی…", "Preparing…");
    public static string Copying => T("در حال کپی فایل‌ها…", "Copying files…");
    public static string Shortcuts => T("ساخت میان‌بر…", "Creating shortcuts…");
    public static string Registering => T("ثبت در برنامه‌های نصب‌شده‌ی ویندوز…", "Registering in Installed apps…");
    public static string Done => T("نصب شد.", "Installed.");
    public static string Failed(string why) => T("نصب انجام نشد: ", "Installation failed: ") + why;
    public static string Admin => T("برنامه هنگام باز شدن اجازه‌ی مدیر ویندوز می‌خواهد (برای خواندن سنسورها)؛ این طبیعی است.", "The program asks for administrator permission when it opens (to read the sensors); that is normal.");
}
