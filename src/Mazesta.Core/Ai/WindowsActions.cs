namespace Mazesta.Core.Ai;

/// <summary>A window of Windows the assistant may open (This PC, Device Manager…). <see cref="File"/> and <see cref="Arguments"/> are fixed here:
/// the model names an id, never a command line, so nothing but these can be started. <see cref="TitleKey"/> is its name as Windows shows it.</summary>
public sealed record WinPlace(string Id, string TitleKey, string File, string? Arguments, string[] Words);

/// <summary>A Windows command the assistant gives (or, if <see cref="Run"/> is set, runs after the user confirmed on the page): checked by hand, with
/// what it does (<c>WinCmd_{Id}_What</c>) and, when it changes something or cannot be undone, a warning (<c>WinCmd_{Id}_Warn</c>). A small model
/// made commands up ("Unlock-Item" for a drive's lock), so the commands it hands out come from here.</summary>
/// <param name="Run">The program and its arguments when the app may run it itself; only commands that read, or change nothing that matters, are.</param>
/// <param name="Admin">It needs a terminal opened as administrator.</param>
public sealed record WinCommand(string Id, string Command, bool Warn, (string File, string Args)? Run, bool Admin, string[] Words);

public static class WindowsActions
{
    private static WinPlace P(string id, string file, string? args, params string[] words) => new(id, "Win_" + id, file, args, words);
    private static WinCommand C(string id, string command, bool warn, (string, string)? run, bool admin, params string[] words) => new(id, command, warn, run, admin, words);

    public static IReadOnlyList<WinPlace> Places { get; } =
    [
        P("thispc", "explorer.exe", "shell:MyComputerFolder", "مای کامپیوتر", "مای کامپیوتری", "کامپیوتر من", "this pc", "my computer", "mycomputer", "دیس پی سی", "دیس پیسی"),
        P("explorer", "explorer.exe", null, "فایل اکسپلورر", "اکسپلورر", "file explorer", "explorer"),
        P("downloads", "explorer.exe", "shell:Downloads", "پوشه دانلود", "دانلودها", "دانلود ها", "downloads"),
        P("recycle", "explorer.exe", "shell:RecycleBinFolder", "سطل زباله", "سطل اشغال", "ریسایکل بین", "recycle bin"),
        P("devmgr", "devmgmt.msc", null, "دیوایس منیجر", "دیوایس منجر", "مدیریت دستگاه", "مدیریت دستگاهها", "device manager"),
        P("taskmgr", "taskmgr.exe", null, "تسک منیجر", "تسک منجر", "task manager"),
        P("diskmgmt", "diskmgmt.msc", null, "دیسک منیجمنت", "مدیریت دیسک", "disk management", "پارتیشن بندی"),
        P("control", "control.exe", null, "کنترل پنل", "control panel"),
        P("programs", "control.exe", "appwiz.cpl", "حذف برنامه", "اناینستال", "آن اینستال", "برنامه های نصب شده", "programs and features", "uninstall"),
        P("services", "services.msc", null, "سرویس ها", "سرویسهای ویندوز", "services"),
        P("events", "eventvwr.msc", null, "ایونت ویور", "event viewer", "رویدادهای ویندوز"),
        P("reliability", "perfmon.exe", "/rel", "تاریخچه پایداری", "ریلایبیلیتی", "reliability monitor", "reliability"),
        P("about", "ms-settings:about", null, "درباره سیستم", "سیستم پراپرتیز", "system properties", "about this pc"),
        P("display", "ms-settings:display", null, "تنظیمات نمایش", "تنظیمات صفحه نمایش", "رزولوشن", "display settings", "resolution"),
        P("sound", "control.exe", "mmsys.cpl", "تنظیمات صدا", "صدای ویندوز", "sound settings"),
        P("netconn", "control.exe", "ncpa.cpl", "اتصالات شبکه", "کارتهای شبکه", "network connections", "ncpa"),
        P("wifi", "ms-settings:network-wifi", null, "وای فای", "وایفای", "wifi", "wi-fi"),
        P("bluetooth", "ms-settings:bluetooth", null, "بلوتوث", "bluetooth"),
        P("printers", "ms-settings:printers", null, "پرینتر", "چاپگر", "printer", "printers"),
        P("startup", "ms-settings:startupapps", null, "استارتاپ", "برنامه های استارت اپ", "startup apps", "startup"),
        P("storagesense", "ms-settings:storagesense", null, "تنظیمات ذخیره سازی", "استوریج سنس", "storage sense", "storage settings"),
        P("bitlocker", "control.exe", "/name Microsoft.BitLockerDriveEncryption", "بیت لاکر", "بیتلاکر", "bitlocker", "قفل درایو", "رمز درایو"),
        P("defender", "windowsdefender:", null, "ویندوز دیفندر", "دیفندر", "امنیت ویندوز", "windows security", "defender", "انتی ویروس"),
        P("firewall", "control.exe", "firewall.cpl", "فایروال", "firewall"),
        P("poweroptions", "control.exe", "powercfg.cpl", "پاور آپشن", "پاور اپشن", "power options"),
        P("datetime", "ms-settings:dateandtime", null, "تاریخ و ساعت", "ساعت ویندوز", "date and time"),
        P("dxdiag", "dxdiag.exe", null, "dxdiag", "دایرکت ایکس دیاگ"),
        P("msinfo", "msinfo32.exe", null, "msinfo", "msinfo32", "سیستم اینفورمیشن", "system information"),
        P("winver", "winver.exe", null, "winver", "نسخه ویندوز", "ورژن ویندوز", "windows version"),
    ];

    public static IReadOnlyList<WinCommand> Commands { get; } =
    [
        C("bitlockerstatus", "manage-bde -status", false, ("manage-bde.exe", "-status"), true, "بیت لاکر", "بیتلاکر", "bitlocker", "قفل", "علامت قفل", "عکس قفل", "رمزگذاری درایو", "رمز درایو"),
        C("bitlockeroff", "manage-bde -off D:", true, null, true, "بیت لاکر", "بیتلاکر", "bitlocker", "قفل", "علامت قفل", "عکس قفل", "رمزگذاری درایو", "رمز درایو"),
        C("flushdns", "ipconfig /flushdns", false, ("ipconfig.exe", "/flushdns"), false, "flushdns", "فلاش dns", "پاک کردن dns", "کش dns", "dns cache"),
        C("ipconfig", "ipconfig /all", false, ("ipconfig.exe", "/all"), false, "ipconfig", "ای پی", "آی پی", "ip", "مک آدرس", "mac address"),
        C("netreset", "netsh winsock reset && netsh int ip reset", true, null, true, "ریست شبکه", "ریست کردن شبکه", "winsock", "وینسوک", "network reset", "اینترنت قطع", "شبکه مشکل"),
        C("sfc", "sfc /scannow", false, null, true, "sfc", "فایلهای خراب ویندوز", "فایل های خراب ویندوز", "تعمیر ویندوز", "repair windows"),
        C("dism", "DISM /Online /Cleanup-Image /RestoreHealth", false, null, true, "dism", "تعمیر ویندوز", "repair windows", "ایمیج ویندوز"),
        C("chkdsk", "chkdsk C: /scan", false, null, true, "chkdsk", "چک دیسک", "خطای دیسک", "خطای هارد", "بد سکتور", "bad sector"),
        C("battery", "powercfg /batteryreport", false, ("powercfg.exe", "/batteryreport /output \"%TEMP%\\battery-report.html\""), false, "باتری", "گزارش باتری", "سلامت باتری", "battery", "battery report"),
        C("hibernateoff", "powercfg /h off", true, null, true, "هایبرنیت", "هایبرنت", "hiberfil", "hibernate"),
        C("drivers", "driverquery /v", false, null, false, "driverquery", "لیست درایورها", "فهرست درایورها", "list drivers"),
        C("systeminfo", "systeminfo", false, null, false, "systeminfo", "اطلاعات ویندوز", "زمان نصب ویندوز", "ساعت روشن بودن", "uptime"),
        C("activation", "slmgr /xpr", false, ("cscript.exe", "//nologo %SystemRoot%\\System32\\slmgr.vbs /xpr"), false, "اکتیو", "فعالسازی ویندوز", "فعال سازی ویندوز", "لایسنس ویندوز", "activation", "activate"),
        C("bios", "shutdown /r /fw /t 0", true, null, true, "ورود به بایوس", "رفتن به بایوس", "بایوس", "bios", "uefi"),
        C("wsreset", "wsreset", false, null, false, "wsreset", "مایکروسافت استور", "microsoft store", "استور"),
        C("tempclean", "del /q /f /s %TEMP%\\*", true, null, false, "فایلهای temp", "فایل های temp", "فایلهای موقت", "فایل های موقت", "temp", "تمپ"),
    ];

    public static WinPlace? Place(string id) => Places.FirstOrDefault(p => p.Id == id);
    public static WinCommand? Command(string id) => Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>The window a normalised message names: the one with the longest matching name.</summary>
    public static WinPlace? FindPlace(string norm) => Places.Select(p => (p, l: Longest(norm, p.Words))).Where(x => x.l > 0).OrderByDescending(x => x.l).Select(x => x.p).FirstOrDefault();

    /// <summary>The commands a normalised message is about (all those sharing the longest matching name: the status and the removal of BitLocker
    /// go together), or none.</summary>
    public static IReadOnlyList<WinCommand> FindCommands(string norm)
    {
        var found = Commands.Select(c => (c, l: Longest(norm, c.Words))).Where(x => x.l > 0).ToList();
        if (found.Count == 0) return [];
        int best = found.Max(x => x.l);
        return [.. found.Where(x => x.l == best).Select(x => x.c)];
    }

    private static int Longest(string norm, IEnumerable<string> words) => words.Where(w => AppGuide.Has(norm, w)).Select(w => AppGuide.Normalize(w).Trim().Length).DefaultIfEmpty(0).Max();
}
