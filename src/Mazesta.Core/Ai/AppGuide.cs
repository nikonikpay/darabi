using System.Text; using System.Text.RegularExpressions; using Mazesta.Core.Software;
namespace Mazesta.Core.Ai;

/// <summary>
/// A place in the app the assistant can take the user to: a page, or one control on it (<see cref="Target"/>, the page marks it with
/// <c>data-a</c>). <see cref="TitleKey"/> is the localisation key of the name the page itself shows; <see cref="Words"/> are what people call it,
/// in Persian and English, as typed (spelling variants included). <see cref="What"/> tells the model what is there. <see cref="HintKey"/>, when
/// set, is what to tell the user about the control (a long run, a change to Windows) since the assistant shows it and does not press it.
/// </summary>
public sealed record AppPlace(string Page, string? Target, string TitleKey, string[] Words, string What, string? HintKey = null);

/// <summary>What a message asks for, when that is plain enough to act on without the model: the model reads words badly (it opened the overlay
/// for "graphics overclock"), so the app decides these itself and the model only words the answer from what the app read.</summary>
public enum AiIntent { None, Navigate, HowTo, Specs, Sensors, Software, SoftwareList, Report, ReportFile, Tests, Overlay, Dns, Games, TestsInfo, Help, Tray, Alert, WinOpen, WinCommand, WinCommandUnknown, Drivers, Benchmarks, PcieErrors, Crashes, Checkup }

/// <param name="Part">For <see cref="AiIntent.Specs"/>: cpu, ram, gpu, vram, storage, board, os, or all; for <see cref="AiIntent.Sensors"/>: the part
/// whose readings are asked for (cpu, gpu, memory, storage, network), or null for every part.</param>
/// <param name="Format">For <see cref="AiIntent.ReportFile"/>: pdf, html or summary.</param>
/// <param name="Index">For the reports: which one, 0 the newest, 1 the one before it.</param>
/// <param name="Category">For <see cref="AiIntent.SoftwareList"/>: the group asked about, or null for all.</param>
/// <param name="On">For <see cref="AiIntent.Overlay"/>: shown or hidden.</param>
/// <param name="Kind">For <see cref="AiIntent.Sensors"/>: Temperature, Load, Clock, Power or Fan, or null for all.</param>
/// <param name="Value">For <see cref="AiIntent.Alert"/>: the temperature (°C) asked for; <see cref="Part"/> is cpu or gpu, or null for both.</param>
/// <param name="Ids">For <see cref="AiIntent.WinOpen"/>: the window's id; for <see cref="AiIntent.WinCommand"/>: the commands' ids (see <see cref="WindowsActions"/>).</param>
public sealed record AiRoute(AiIntent Intent, AppPlace? Place = null, string? Part = null, SoftApp? App = null, IReadOnlyList<string>? Areas = null, string? Format = null,
    SoftCategory? Category = null, bool On = false, string? Kind = null, int Index = 0, int? Value = null, IReadOnlyList<string>? Ids = null)
{
    /// <summary>For <see cref="AiIntent.Tests"/>: every test of the areas was asked for ("همشو"), not the usual set.</summary>
    public bool All { get; init; }
    /// <summary>For <see cref="AiIntent.Tests"/>: the parts are to be loaded at the same time ("همزمان").</summary>
    public bool Together { get; init; }
    /// <summary>For <see cref="AiIntent.Tests"/>: the length asked for, in seconds ("۵ دقیقه", "۳۰ ثانیه", "۱ ساعت و ۳۰ دقیقه"), or null for the page's.</summary>
    public int? Seconds { get; init; }
    /// <summary>For <see cref="AiIntent.Tests"/>: <see cref="Seconds"/> is the whole run's, shared by the load tests ("به ترتیب در ۵ دقیقه"), not each one's.</summary>
    public bool Total { get; init; }
}

/// <summary>
/// The app's pages and the controls on them that people ask for, and the rules that read a message against them. Matching is on normalised
/// text (Arabic letters to Persian, the half space to a space, آ to ا, digits to Latin) at the start of a word, so a Persian suffix ("گرافیکو",
/// "رممو") still matches; the longest name found wins, so "آندرولت خودکار" beats "آندرولت" and "مانیتورینگ" beats "مانیتور".
/// </summary>
public static class AppGuide
{
    private static AppPlace P(string page, string key, string what, params string[] words) => new(page, null, key, words, what);
    private static AppPlace T(string page, string target, string key, string what, string? hint, params string[] words) => new(page, target, key, words, what, hint);

    public static IReadOnlyList<AppPlace> Places { get; } =
    [
        P("dashboard", "Nav_Dashboard", "summary of the computer and its live state", "داشبورد", "صفحه اصلی", "خانه", "dashboard", "home"),
        P("monitoring", "Nav_Monitoring", "every sensor with live charts: temperatures, loads, clocks, fans, power", "مانیتورینگ", "سنسور", "سنسورها", "دماها", "دما ها", "نمودار", "monitoring", "sensors"),
        P("system", "Nav_SystemInfo", "the whole specification of the computer, with export to PDF", "اطلاعات سیستم", "مشخصات سیستم", "مشخصات", "سخت افزار", "system info", "specs", "specification"),
        T("system", "export", "System_Export", "export the specification as PDF, HTML or JSON", null, "خروجی مشخصات", "pdf مشخصات", "export specs"),
        P("cpu", "Nav_Cpu", "the processor's specification and sensors", "صفحه cpu", "صفحه سی پی یو", "صفحه پردازنده", "بخش cpu", "بخش پردازنده", "پردازنده"),
        P("gpu", "Nav_Gpu", "the graphics card's specification and sensors", "صفحه gpu", "صفحه کارت گرافیک", "بخش کارت گرافیک", "بخش گرافیک", "صفحه گرافیک"),
        P("ram", "Dashboard_Ram", "the memory modules and their sensors", "صفحه رم", "بخش رم", "صفحه حافظه"),
        P("storage", "Nav_Storage", "the drives, their health and sensors", "ذخیره سازی", "هارد", "هاردها", "درایوها", "storage", "ssd"),
        P("network", "Nav_Network", "the network adapters", "شبکه", "کارت شبکه", "network"),
        P("tests", "Nav_Tests", "hardware tests with a report: the memory test checks the RAM's health, the drive tests the disks' (SMART and speed), the processor, network and graphics card tests theirs; a switch runs processor, memory and graphics card tests together",
            "تستها", "تست ها", "صفحه تست", "بخش تست", "tests", "سلامت رم", "سلامت حافظه", "سلامت هارد", "سلامت گرافیک", "سلامت cpu", "تست رم", "تست حافظه", "تست هارد", "تست گرافیک", "تست cpu", "تست پردازنده"),
        T("tests", "start", "Test_Start", "start the ticked tests", null, "شروع تست", "شروع تستها"),
        P("benchmarks", "Nav_Benchmarks", "speed benchmarks and comparison with other computers; the memory benchmark also measures access latency (at 32 KB, 256 KB, 2 MB, 16 MB, 64 MB and the RAM itself); the Iranian-garden 3D scene benchmarks (normal and ray-traced) walk the garden once at walking pace and need no setting", "بنچمارک", "بنچ مارک", "بنچمارکها", "benchmark", "benchmarks"),
        P("checkup", "Nav_Checkup", "one-click smart diagnosis: runs the benchmarks and judges the computer from them (the assistant can run it itself and tell the findings)", "عیب یابی", "عیبیابی", "عیب یابی هوشمند", "چکاپ", "checkup", "diagnosis"),
        T("checkup", "run", "Checkup_Run", "start the diagnosis", null, "اجرای عیب یابی", "شروع عیب یابی"),
        P("checks", "Nav_Checks", "hands-on checks a person judges: screen, keyboard, mouse, speakers, microphone, and the laptop battery's health", "بررسی دستی", "بررسیهای دستی", "تست دستی", "hands-on", "checks"),
        T("checks", "display", "Checks_Display", "full-screen colours for dead or stuck pixels", null, "پیکسل سوخته", "پیکسل", "نمایشگر", "صفحه نمایش", "مانیتور", "ال سی دی", "lcd", "dead pixel", "display"),
        T("checks", "keys", "Checks_Keys", "every key of the keyboard lights up when pressed", null, "کیبورد", "کیبرد", "صفحه کلید", "keyboard"),
        T("checks", "mouse", "Checks_Mouse", "mouse and touchpad buttons, wheel, double clicks", null, "ماوس", "موس", "تاچ پد", "تاچپد", "mouse", "touchpad"),
        T("checks", "speakers", "Checks_Speakers", "left, right and both speakers, and a frequency sweep", null, "بلندگو", "اسپیکر", "هدفون", "هدست", "صدا", "speaker", "speakers", "headphone"),
        T("checks", "mic", "Checks_Mic", "records the microphone and shows its level", null, "میکروفون", "میکروفن", "میکرفون", "مایک", "microphone", "mic"),
        T("checks", "battery", "Checks_Battery", "the laptop battery: its health percent (full capacity against capacity when new), charge cycles, and a drain test", null,
            "باتری", "باطری", "سلامت باتری", "سلامت باطری", "تست باتری", "تست باطری", "battery", "battery health"),
        P("overlay", "Nav_Overlay", "settings of the on-screen overlay: temperatures, loads, frame rate (with the 1% and 0.1% lows), and the link (ping, packet loss, jitter, download and upload) shown over games; four sizes (small, medium, large, extra large) and a panel that is as narrow as its rows; the game preset shows frame rate with its lows, GPU temperature, hot spot, load, clock, memory and power, and CPU temperature, load, clock, power and busiest thread, with the average clock of the P-cores and of the E-cores on an Intel CPU that has both", "اورلی", "اورلای", "اوورلی", "overlay", "نمایش روی صفحه", "fps"),
        P("tuning", "Nav_Tuning", "graphics card overclock and undervolt: clocks, voltage curve, power limit, fans, profiles; RAM XMP", "اورکلاک", "اور کلاک", "اندرولت", "اندروالت", "آندر ولت", "اندر ولت", "تیونینگ", "undervolt", "overclock", "tuning"),
        P("lights", "Nav_Lights", "RGB lighting colours and effects of memory, graphics card, board, fans and strips (Corsair, ASUS, Gigabyte, MSI), through OpenRGB; the LED count of a board header", "آر جی بی", "نور", "رنگ رم", "رنگ فن", "لایت", "rgb", "lighting", "lights"),
        P("fans", "Nav_Fans", "motherboard fan control: automatic, fixed speed, or a temperature curve you drag (silent, standard, performance, full speed)", "کنترل فن", "فن مادربرد", "منحنی فن", "فن", "fan control", "fan curve", "fans"),
        T("tuning", "autoundervolt", "Tuning_AutoUndervolt", "finds the lowest stable voltage by itself (10 to 25 minutes)", "Assist_Hint_Tuning", "اندرولت خودکار", "اندروالت خودکار", "اندر ولت خودکار", "auto undervolt"),
        T("tuning", "autooverclock", "Tuning_AutoOverclock", "raises the clocks step by step while the card stays correct (10 to 25 minutes)", "Assist_Hint_Tuning", "اورکلاک خودکار", "اور کلاک خودکار", "auto overclock"),
        T("tuning", "curve", "Tuning_Curve_Title", "measures and shows the card's voltage-frequency curve", null, "منحنی ولتاژ", "منحنی فرکانس", "کرو", "منحنی", "voltage curve", "curve"),
        T("tuning", "profiles", "Tuning_Profiles", "saved clock profiles of this card", null, "پروفایل اورکلاک", "پروفایلها", "پروفایل ها", "profiles"),
        T("tuning", "memory", "Tuning_Memory", "RAM XMP / DOCP / EXPO (set in the BIOS; a button restarts into it)", null, "xmp", "docp", "expo", "پروفایل رم", "فرکانس رم"),
        T("tuning", "fan", "Tuning_Fan", "the graphics card's fan speed", null, "فن کارت", "فن گرافیک", "سرعت فن", "fan"),
        P("tools", "Nav_WindowsTools", "Windows tools and gaming: DNS, power plan, Game Mode, SFC/DISM repair, hibernation, virtual memory, hosts file", "ابزارهای ویندوز", "ابزار ویندوز", "ابزارها", "tools"),
        T("tools", "dns", "Dns_Title", "switch the DNS server, or test them all and pick the fastest", null, "dns", "دی ان اس", "دیاناس", "دی ان اس جامپر", "dns jumper"),
        T("tools", "power", "Gaming_PowerPlan", "Windows power plans (High, Ultimate Performance)", null, "پلن انرژی", "پاور پلن", "برق", "power plan", "ultimate performance"),
        T("tools", "gamemode", "Gaming_GameMode", "Game Mode and hardware GPU scheduling (HAGS)", null, "گیم مود", "game mode", "hags", "بازی"),
        T("tools", "repair", "Tools_Repair", "Windows' own repair: SFC and DISM", "Assist_Hint_Repair", "sfc", "dism", "تعمیر ویندوز", "تعمیر", "repair"),
        T("tools", "hibernate", "Tools_Hib_Title", "hibernation and Fast Startup on or off", null, "هایبرنیت", "هایبرنت", "fast startup", "فست استارتاپ", "hibernate"),
        T("tools", "vm", "Tools_Vm_Title", "virtual memory (the page file)", null, "حافظه مجازی", "پیج فایل", "page file", "pagefile", "virtual memory"),
        T("tools", "hosts", "Tools_Hosts_Title", "edit the hosts file", null, "hosts", "هاست", "فایل هاست"),
        T("tools", "cleanup", "Tools_Windows", "Disk Cleanup and Windows Update's window", null, "پاکسازی دیسک", "پاک سازی دیسک", "disk cleanup"),
        P("tweaks", "Nav_Tweaks", "Windows tweaks (telemetry, widgets, background apps) and preferences, each undoable", "ترفند", "ترفندها", "ترفند ویندوز", "تنظیمات ویندوز", "بهینه سازی ویندوز", "tweaks"),
        P("drivers", "Nav_Drivers", "drivers: the graphics card's driver against NVIDIA's newest (Game Ready or Studio, suggested from the installed programs), the drivers Windows Update offers, and the devices without a working driver; download and install each with a button",
            "درایور", "درایورها", "درایور ها", "اپدیت درایور", "آپدیت درایور", "به روزرسانی درایور", "driver", "drivers", "game ready", "studio driver"),
        P("updates", "Nav_Updates", "Windows Update: default, recommended (deferred) or off", "اپدیت ویندوز", "آپدیت ویندوز", "به روزرسانی ویندوز", "بروزرسانی ویندوز", "windows update"),
        P("reports", "Nav_Reports", "saved test and benchmark reports: summary (one A5 sheet: the highest temperatures in one row, one line a test with its main figures, the system, drive health, and a note that Mazesta Test is installed on the customer's system), PDF, HTML, before/after comparison; the company's copy needs a service number to send a report to the site, where both the summary and the whole report are kept", "گزارش", "گزارشها", "گزارش ها", "ریپورت", "reports", "report"),
        T("reports", "compare", "Reports_Compare", "compare two ticked reports, before and after", null, "مقایسه گزارش", "مقایسه قبل و بعد", "compare"),
        P("apps", "Nav_Apps", "which professional programs (rendering, architecture, civil, animation, editing) run on this computer, and at what level", "برنامه ها", "برنامههای تخصصی", "نرم افزار", "نرمافزار", "نرم افزارها", "برنامه های رندرینگ", "software", "apps"),
        P("games", "Nav_Games", "which games run on this computer, at their publishers' minimum, recommended and high tiers and the publishers' own targets (resolution, preset, frame rate)",
            "بازی ها", "بازیها", "لیست بازی", "سیستم مورد نیاز بازی", "سیستم بازی", "مشخصات بازی", "games", "game requirements"),
        P("ai", "Nav_Ai", "local AI models: which run here, downloads, speed benchmark; image, video, audio and 3D models", "مدل هوش مصنوعی", "مدلهای هوش مصنوعی", "مدل های هوش مصنوعی", "هوش مصنوعی", "llm", "مدل زبانی", "ai models"),
        P("settings", "Nav_Settings", "the app's settings: language, units, render mode, the tray monitor and its temperature warnings, data folder", "تنظیمات", "تنظیمات برنامه", "ستینگ", "settings", "tray", "ترای", "پایشگر"),
        T("tools", "gameboost", "GameBoost_Title", "game mode: stops background services a game does not need (Windows Update, downloads, telemetry, search indexing) while it is on, with a tick per service; switching it off puts them back", null,
            "حالت گیم", "حالت بازی", "مود بازی", "سرویس های غیر ضروری", "سرویسهای غیرضروری", "game boost", "gaming mode"),
        T("tools", "netfix", "NetFix_Title", "internet connection troubleshooter: checks adapter, router, internet, DNS, a web page and the proxy, then clears the proxy, sets the DNS, empties the DNS cache or resets the network", null,
            "اینترنت وصل نمیشه", "اینترنت وصل نمی شود", "اینترنت قطع", "عیب یابی اینترنت", "مشکل اینترنت", "پراکسی", "پروکسی", "ریست شبکه", "proxy", "internet troubleshooter", "no internet"),
        T("tools", "crashes", "Bsod_Title", "blue screens (stop errors): lists the ones Windows recorded, with the stop code, its name, its parameters and the likely causes to check", null,
            "بلو اسکرین", "بلواسکرین", "بلو اسکرین ها", "صفحه ابی", "صفحه ابی مرگ", "bsod", "blue screen", "bluescreen", "stop code", "کد خطای ویندوز", "minidump", "مینی دامپ"),
        P("appupdate", "Nav_AppUpdate", "update this app", "اپدیت برنامه", "آپدیت برنامه", "به روزرسانی برنامه", "نسخه برنامه", "app update"),
        T("appupdate", "site", "Site_Title", "the link to the shop's site: the site key is entered here; a report's summary is sent to the site from the reports page (to be printed there for the serviced case) and benchmark results from the benchmark page, where anyone can also share their latest results as a page with a link (no key needed)", null,
            "اشتراک گذاری نتیجه", "اشتراک نتیجه", "شیر کردن نتیجه", "share results",
            "کلید سایت", "اتصال به سایت", "ارسال به سایت", "ارسال گزارش به سایت", "گزارش آنلاین", "اپلود بنچمارک", "آپلود بنچمارک", "ارسال نتایج", "site key", "send to site", "upload results"),
    ];

    public static AppPlace? Page(string id) => Places.FirstOrDefault(p => p.Page == id && p.Target is null);

    /// <summary>Folds the ways one word is typed into one: Arabic yeh and kaf, alef with madda or hamza, the half space and the tatweel,
    /// Persian and Arabic digits, diacritics, punctuation; lower case, single spaces.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length + 2); sb.Append(' ');
        foreach (char c0 in text.ToLowerInvariant())
        {
            char c = c0 switch
            {
                'ي' or 'ى' or 'ئ' => 'ی', 'ك' => 'ک', 'ة' => 'ه', 'آ' or 'أ' or 'إ' or 'ٱ' => 'ا', 'ؤ' => 'و',
                >= '۰' and <= '۹' => (char)('0' + (c0 - '۰')), >= '٠' and <= '٩' => (char)('0' + (c0 - '٠')),
                '‌' or '‍' or '‎' or '‏' or ' ' or '\t' or '\n' or '\r' => ' ',
                '?' or '؟' or '!' or '.' or '،' or ',' or ':' or ';' or '«' or '»' or '"' or '(' or ')' or '[' or ']' or '*' => ' ',
                _ => c0,
            };
            if (c == 'ـ' || c is >= 'ً' and <= 'ٟ' || c == 'ٰ') continue;
            if (c == ' ' && sb[^1] == ' ') continue;
            sb.Append(c);
        }
        if (sb[^1] != ' ') sb.Append(' ');
        return sb.ToString();
    }

    /// <summary>Whether a normalised text has the word or phrase at the start of a word (a suffix after it is allowed).</summary>
    public static bool Has(string norm, string word) => norm.Contains(' ' + Normalize(word).Trim(), StringComparison.Ordinal);
    private static bool Any(string norm, IEnumerable<string> words) => words.Any(w => Has(norm, w));
    /// <summary>The longest of the words the text has (by length), or 0 when none. "صفحه" or "بخش" at the start of a name does not count: "بخش تست"
    /// is the tests page, but in "بخش تست کیبورد" the keyboard is what is named.</summary>
    private static int Best(string norm, IEnumerable<string> words) => words.Where(w => Has(norm, w)).Select(Weight).DefaultIfEmpty(0).Max();
    private static int Weight(string word)
    {
        string w = Normalize(word).Trim();
        foreach (var lead in new[] { "صفحه ", "بخش " }) if (w.StartsWith(lead, StringComparison.Ordinal)) return w.Length - lead.Length;
        return w.Length;
    }

    /// <summary>The place a message names: the one with the longest matching name; a control wins a tie with its page.</summary>
    public static AppPlace? FindPlace(string norm)
    {
        AppPlace? best = null; int length = 0;
        foreach (var p in Places)
        {
            int l = Best(norm, p.Words);
            if (l > length || l == length && l > 0 && p.Target is not null && best?.Target is null) { best = p; length = l; }
        }
        return best;
    }

    /// <summary>The program a message names (the longest name found), or null.</summary>
    public static SoftApp? FindApp(string norm)
    {
        SoftApp? best = null; int length = 0;
        foreach (var a in SoftwareCatalog.Apps) { int l = Best(norm, a.Words); if (l > length) { best = a; length = l; } }
        return best;
    }

    private static readonly string[] GoWords = ["برو", "باز کن", "بازکن", "باز بشه", "باز شه", "بیار", "ببر", "نشون بده", "نشان بده", "نشونم بده", "نمایش بده", "open", "go to", "show", "take me"];
    private static readonly string[] PageWords = ["صفحه", "بخش", "منو", "قسمت", "تب", "page", "section", "menu", "tab"];
    private static readonly string[] DoWords = ["انجام بده", "انجام بدی", "بزن", "اجرا کن", "اجرا بشه", "شروع کن", "راه بنداز", "فعال کن", "run", "start", "do it", "press", "click"];
    private static readonly string[] HowWords = ["چطور", "چطوری", "چگونه", "چجوری", "چه جوری", "چه طور", "کجاست", "کجا", "راهنمایی", "how", "where"];
    private static readonly string[] AskWords = ["چقدر", "چقد", "چنده", "چند", "چیه", "چی", "چه", "کدوم", "کدام", "کدومه", "مدل", "بگو", "what", "which", "how much", "tell"];
    private static readonly string[] TestWords = ["تست", "ازمایش", "امتحان", "چک کن", "بررسی کن", "سلامت", "test", "check"];
    private static readonly string[] SummaryWords = ["خلاصه", "نتیجه", "نتایج", "بگو", "چی شد", "چقدر", "بالاترین", "بیشترین", "حداکثر", "دما", "summary", "summarize", "result", "results", "highest", "max"];
    private static readonly string[] RunWords = ["اجرا", "اجرا میشه", "اجرا می شود", "اجرا کنم", "کار میکنه", "کار می کند", "میکشه", "می کشه", "جواب میده", "مناسب", "بازش کنم", "run", "runs", "work", "handle", "enough"];
    private static readonly string[] AppsWords = ["برنامه", "برنامه ها", "نرم افزار", "نرم افزارها", "software", "programs", "apps"];

    private static readonly (string Part, string[] Words)[] SpecParts =
    [
        ("vram", ["رم گرافیک", "رم کارت گرافیک", "حافظه گرافیک", "حافظه کارت گرافیک", "حافظه کارت", "حافظه ویدیو", "حافظه ویدئو", "vram", "video memory"]),
        ("cpu", ["cpu", "سی پی یو", "سیپییو", "پردازنده", "processor"]),
        ("gpu", ["کارت گرافیک", "گرافیک", "gpu", "graphics card"]),
        ("ram", ["رم", "ram", "حافظه"]),
        ("storage", ["هارد", "ssd", "دیسک", "حافظه ذخیره", "درایو", "storage", "disk"]),
        ("board", ["مادربرد", "مادر برد", "بایوس", "motherboard", "bios"]),
        ("os", ["ویندوز", "سیستم عامل", "windows", "os"]),
        ("all", ["مشخصات", "کانفیگ", "specs", "specification"]),
    ];
    private static readonly (string Area, string[] Words)[] TestAreas =
    [
        ("gpu", ["کارت گرافیک", "گرافیک", "gpu", "graphics"]), ("cpu", ["cpu", "سی پی یو", "پردازنده", "processor"]),
        ("memory", ["رم", "ram", "حافظه", "memory"]), ("storage", ["هارد", "ssd", "دیسک", "درایو", "storage", "drive"]), ("network", ["شبکه", "اینترنت", "network"]),
    ];
    private static readonly (SoftCategory Category, string[] Words)[] Categories =
    [
        (SoftCategory.Visualization, ["رندرینگ", "رندر", "ریل تایم", "render", "rendering"]), (SoftCategory.Architecture, ["معماری", "architecture", "bim"]),
        (SoftCategory.Civil, ["عمران", "سازه", "civil", "structural"]), (SoftCategory.Mechanical, ["مکانیک", "طراحی مکانیکی", "طراحی صنعتی", "mechanical", "cad cam"]),
        (SoftCategory.Game, ["بازی ها", "بازیها", "گیم ها", "games"]), (SoftCategory.Animation, ["انیمیشن", "جلوه ویژه", "جلوه های ویژه", "سه بعدی", "animation", "vfx"]),
        (SoftCategory.Video, ["تدوین", "ادیت", "ویدیو", "ویدئو", "editing", "video"]), (SoftCategory.Graphics, ["گرافیکی", "طراحی گرافیک", "graphic design"]),
    ];

    private static readonly string[] AllWords = ["همه", "همش", "همشو", "همشون", "تمام", "کامل", "تک تک", "all", "every", "full"];
    private static readonly string[] TogetherWords = ["همزمان", "هم زمان", "با هم", "باهم", "together", "simultaneous", "at once", "at the same time"];
    private static readonly string[] CrashWords = ["بلو اسکرین", "بلواسکرین", "بلو اسکیرین", "صفحه ابی", "bsod", "blue screen", "bluescreen", "stop code", "استاپ کد", "minidump", "مینی دامپ"];
    private static readonly string[] OverlayWords = ["اورلی", "اورلای", "overlay", "بالای صفحه", "بالای مانیتور", "روی صفحه", "روی بازی", "گوشه صفحه", "fps"];
    // A command said not to be done ("اجرا نکن", "فقط توضیح بده") is never acted on: the model explains, and nothing starts.
    private static readonly string[] NotWords = ["نکن", "نکنی", "نزن", "نزنی", "نده", "نشه", "نشود", "نمیخوام", "نمی خوام", "نمیخواهم", "نباید", "فقط توضیح", "فقط بگو", "توضیح بده", "don't", "dont", "do not", "not run", "only explain", "just explain", "explain"];
    private static readonly string[] EarlierWords = ["قبلی", "قبل", "قبلیه", "ماقبل", "پیشین", "previous", "earlier", "before last"];
    private static readonly string[] OnWords = ["روشن", "فعال", "بیاد", "بیار", "نشون بده", "نشان بده", "نمایش بده", "show", "turn on", "enable"];
    private static readonly string[] OffWords = ["خاموش", "غیرفعال", "غیر فعال", "ببند", "بردار", "قطع", "مخفی", "hide", "turn off", "disable"];
    private static readonly (string Kind, string[] Words)[] SensorKinds =
    [
        ("Temperature", ["دما", "دمای", "حرارت", "داغ", "temperature", "temp"]), ("Fan", ["فن", "دور فن", "fan"]), ("Load", ["لود", "بار پردازنده", "درصد استفاده", "load", "usage"]),
        ("Clock", ["کلاک", "فرکانس", "clock"]), ("Power", ["توان", "وات", "مصرف برق", "power", "watt"]),
    ];
    // "What can you do": the app answers with what it really has, not the model with what it imagines.
    private static readonly string[] HelpWords = ["چه کارهای", "چه کار های", "چه کارایی", "چه کاری", "چکار میتونی", "چیکار میتونی", "چی کار میتونی", "چه سوالاتی", "چه سوالی", "چه سوال هایی",
        "چه چیزهایی بپرسم", "کمکم کنی", "what can you do", "what can i ask", "help me", "your abilities"];
    private static readonly string[] TrayWords = ["tray", "ترای", "پایشگر", "سیستم تری", "کنار ساعت", "system tray"];
    private static readonly string[] AlertWords = ["خبر بده", "خبرم کن", "خبر کن", "هشدار بده", "هشدار", "اطلاع بده", "اطلاع بدی", "بهم بگو", "گزارش بده", "گزارش بدی", "اعلان", "notify", "alert", "warn"];
    private static readonly string[] WhenWords = ["وقتی", "اگر", "اگه", "هر وقت", "هروقت", "بالای", "بیشتر از", "رسید", "when", "if", "above", "over"];
    private static readonly string[] CommandWords = ["دستور", "دستوری", "کامند", "فرمان", "command", "cmd", "سی ام دی", "پاورشل", "powershell", "ترمینال", "terminal"];
    private static readonly string[] NowWords = ["الان", "اکنون", "همین الان", "در حال حاضر", "فعلا", "now", "current", "چنده", "چقدره", "چقدر"];

    /// <summary>What a message asks for, when it is plain; <see cref="AiIntent.None"/> leaves it to the model. The order matters: a question of
    /// "how" is answered, not acted on; the reports and the programs have their own words; a part's figure is a question about this computer;
    /// a test of the keyboard or the microphone is the hands-on page, a test of a part is a run; last, a page named with a word of going there.</summary>
    public static AiRoute Route(string text)
    {
        string s = Normalize(text);
        var place = FindPlace(s); var app = FindApp(s);
        bool go = Any(s, GoWords), page = Any(s, PageWords), doIt = Any(s, DoWords), how = Any(s, HowWords), ask = Any(s, AskWords);
        bool not = Any(s, NotWords);

        if (Any(s, HelpWords) && place is null && app is null) return new(AiIntent.Help);

        // "Tell me when the CPU passes 80": a warning the tray and the app give, at the temperature asked for.
        if (Any(s, AlertWords) && Any(s, WhenWords) && SensorKinds[0].Words.Any(w => Has(s, w)))
        {
            int? limit = null;
            foreach (var w in s.Split(' ', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(w.TrimEnd('c', '°', 'ی'), out int n) && n is >= 30 and <= 130) limit = n;
            string? part = Any(s, TestAreas[0].Words) && !Any(s, TestAreas[1].Words) ? "gpu" : Any(s, TestAreas[1].Words) && !Any(s, TestAreas[0].Words) ? "cpu" : null;
            return new(AiIntent.Alert, Part: part, Value: limit);
        }

        // The tray monitor (by the clock) is not the overlay over games: "پایشگر tray رو فعال کن" turned the overlay on.
        if (Any(s, TrayWords) && !Any(s, OverlayWords))
        {
            bool trayOff = Any(s, OffWords) || not && !Any(s, OnWords);
            if (trayOff || Any(s, OnWords) || doIt) return new(AiIntent.Tray, On: !trayOff);
        }

        // Drivers ("درایور" also starts with "درایو", a drive): the app checks them and answers; the page is opened to install.
        if ((Has(s, "درایور") || Has(s, "driver") || Has(s, "گیم ردی") || Has(s, "game ready")) && !Any(s, CommandWords) && !(go || page)) return new(AiIntent.Drivers);

        // A Windows command: given from the app's checked list; one that is not there is the model's, and is said to be unchecked.
        if (Any(s, CommandWords))
            return WindowsActions.FindCommands(s) is { Count: > 0 } cmds ? new(AiIntent.WinCommand, Ids: [.. cmds.Select(c => c.Id)]) : new(AiIntent.WinCommandUnknown);

        // A window of Windows (This PC, Device Manager): opened when its name is longer than any of the app's places it shares words with.
        if ((go || doIt || Words(s) <= 3) && !not && WindowsActions.FindPlace(s) is { } win && (place is null || Normalize(win.Words.First(w => Has(s, w))).Length > Best(s, place.Words)))
            return new(AiIntent.WinOpen, Ids: [win.Id]);

        if (how && !go) return new(AiIntent.HowTo, place, App: app);

        // "Diagnose the system": the app runs its smart diagnosis itself (the Diagnosis page's benchmarks and its judgment) and tells what it found,
        // instead of only opening the page. A bare "عیب یابی" or "صفحه عیب یابی" still goes to the page.
        if (!not && !go && !page && WantsCheckup(s, place)) return new(AiIntent.Checkup);

        bool report = Has(s, "گزارش") || Has(s, "ریپورت") || Has(s, "report") || Has(s, "نتیجه تست") || Has(s, "نتایج تست") || Has(s, "نتیجه بنچمارک");
        if (report)
        {
            string? format = Has(s, "pdf") || Has(s, "پی دی اف") || Has(s, "پیدیاف") ? (Has(s, "خلاصه") || Has(s, "summary") ? "summary" : "pdf")
                : Has(s, "html") || Has(s, "اچ تی ام ال") || Has(s, "صفحه وب") ? "html" : null;
            int index = Any(s, EarlierWords) ? 1 : 0;
            if (format is not null) return new(AiIntent.ReportFile, Format: format, Index: index);
            // The temperatures, of the part named if one is ("بالاترین دمای گرافیکم"): Kind marks the question, Part the part.
            if (SensorKinds[0].Words.Any(w => Has(s, w))) return new(AiIntent.Report, Part: TestAreas.FirstOrDefault(a => Any(s, a.Words)).Area, Kind: "Temperature", Index: index);
            if (Any(s, SummaryWords) || ask && !go) return new(AiIntent.Report, Index: index);
            return new(AiIntent.Navigate, place is { Page: "reports" } ? place : Page("reports"));
        }

        if (app is not null)
            return go && !Any(s, RunWords) ? new(AiIntent.Navigate, Page(app.Category == SoftCategory.Game ? "games" : "apps"), App: app) : new(AiIntent.Software, App: app);
        if (Any(s, AppsWords) && Any(s, RunWords) && !go)
            return new(AiIntent.SoftwareList, Category: Categories.Where(c => Any(s, c.Words)).Select(c => (SoftCategory?)c.Category).FirstOrDefault());

        // The overlay over games: shown or hidden by a word of turning it on or off ("دماها بالای صفحه بیاد" is the overlay, not a page).
        // "صفحهٔ اورلی" is its settings page; "بالای صفحه" is the screen.
        // "غیر فعال" holds "فعال": the word of turning off decides.
        bool off = Any(s, OffWords) || not, on = !off && Any(s, OnWords), screen = Has(s, "بالای صفحه") || Has(s, "روی صفحه") || Has(s, "گوشه صفحه");
        if (Any(s, OverlayWords) && on != off && (screen || !page) && !(not && !Any(s, OffWords))) return new(AiIntent.Overlay, On: on);

        // The DNS: finding the fastest is a test the app runs (it changes nothing).
        if ((Has(s, "dns") || Has(s, "دی ان اس")) && (Has(s, "بهترین") || Has(s, "سریع") || Has(s, "تست") || Has(s, "پیدا") || Has(s, "best") || Has(s, "fastest") || Has(s, "test")))
            return new(AiIntent.Dns);

        // Games: the app has no list of them, so the answer is this computer's parts and where to measure it, never a guess at settings.
        if ((Has(s, "بازی") || Has(s, "گیم") || Has(s, "game")) && !go && !page && (ask || Any(s, RunWords))) return new(AiIntent.Games);

        // The graphics card's PCI Express error counters ("خطاهای pcie کارت گرافیک چنده"): read from the driver, with the diagnosis' verdict.
        if ((Has(s, "pcie") || Has(s, "pci express") || Has(s, "pci-e") || Has(s, "لینک") || Has(s, "لین ")) && (Has(s, "خطا") || Has(s, "ارور") || Has(s, "error")) && !go)
            return new(AiIntent.PcieErrors);

        // Blue screens ("چرا سیستمم بلو اسکرین میده"): what Windows recorded is read, with the usual causes of each stop code.
        if (Any(s, CrashWords) && !go) return new(AiIntent.Crashes);

        bool test = Any(s, TestWords);
        // A reading now (a temperature, a fan, a load) is the sensors', not the specification's.
        if (!go && !test && SensorKinds.FirstOrDefault(k => Any(s, k.Words)) is { Kind: not null } sensor && (Any(s, NowWords) || ask))
            return new(AiIntent.Sensors, Kind: sensor.Kind, Part: TestAreas.FirstOrDefault(a => Any(s, a.Words)).Area);
        // A benchmark's number is its history, not the part's specification ("آخرین بنچمارک پردازنده چند بود").
        if (!go && !test && ask && !Has(s, "بنچمارک") && !Has(s, "بنچ مارک") && !Has(s, "benchmark") && SpecParts.FirstOrDefault(p => Any(s, p.Words)) is { Part: not null } spec)
            return new(AiIntent.Specs, Part: spec.Part);

        // "بنچمارک سیستم رو انجام بده": the benchmarks of the parts named, or all of them, asked about together on one card.
        bool bench = Has(s, "بنچمارک") || Has(s, "بنچ مارک") || Has(s, "benchmark");
        if (bench && (doIt || Has(s, "بگیر")) && !go && !page && !not && !ask)
        {
            var areas = TestAreas.Where(a => Any(s, a.Words)).SelectMany(a => a.Area switch
            {
                "cpu" => new[] { "cpu_single", "cpu_multi" }, "gpu" => ["gpu", "gpu_rt", "gpu_scene", "gpu_scene_rt", "gpu_ai"], var x => [x],
            }).ToList();
            return new(AiIntent.Benchmarks, Areas: areas.Count > 0 ? areas : ["all"]);
        }

        if (test && place is { Page: "checks" }) return new(AiIntent.Navigate, place);
        // "Don't run it, only explain": the tests of those parts are listed, as the app has them, and nothing starts.
        if (test && !go && !page && not && TestAreas.Where(a => Any(s, a.Words)).Select(a => a.Area).ToList() is { Count: > 0 } asked)
            return new(AiIntent.TestsInfo, Areas: asked);
        if (test && !go && !page && !not)
        {
            var areas = TestAreas.Where(a => Any(s, a.Words)).Select(a => a.Area).ToList();
            // "رم گرافیک" is the card's memory: the card's test covers it, not the RAM's.
            if (areas.Contains("gpu") && (Has(s, "رم گرافیک") || Has(s, "حافظه گرافیک"))) areas.Remove("memory");
            if (areas.Count > 0)
            {
                bool together = Any(s, TogetherWords);
                var (seconds, inTotal) = Length(s);
                return new(AiIntent.Tests, Areas: areas) { All = Any(s, AllWords) && !together, Together = together, Seconds = seconds, Total = inTotal && !together && areas.Count > 1 };
            }
        }

        // A short message that is little more than a place's name ("اورلی", "گرافیکو اندرولت کن") means going there.
        bool short_ = Words(s) <= 4 && !ask;
        if (place is not null && (go || page || short_ || doIt && place.Target is not null)) return new(AiIntent.Navigate, place);
        return new(AiIntent.None, place, App: app);
    }

    private static readonly Regex LengthPart = new(@"(?<in>(?:در|تو|طی|ظرف|in|within)\s+)?(?<n>\d{1,5})\s*(?<u>ساعت|hours?|hrs?|h|دقیقه|minutes?|mins?|m|ثانیه|seconds?|secs?|s)(?![a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] TotalWords = ["مجموع", "جمعا", "کلا", "total", "overall"];

    /// <summary>The length a message names ("۱۰ دقیقه", "۹۰ ثانیه", "۲ ساعت و ۱۵ دقیقه", "نیم ساعت"), 5 seconds to 3 hours, or null; and whether it is the
    /// whole run's ("در ۵ دقیقه", "مجموع ۵ دقیقه") rather than each test's.</summary>
    internal static (int? Seconds, bool Total) Length(string s)
    {
        int sum = 0; bool any = false, total = Any(s, TotalWords);
        foreach (Match m in LengthPart.Matches(s))
        {
            if (!int.TryParse(m.Groups["n"].Value, out int n)) continue;
            string u = m.Groups["u"].Value;
            sum += u[0] is 'س' or 'h' ? n * 3600 : u[0] is 'د' or 'm' ? n * 60 : n; any = true;
            if (m.Groups["in"].Success) total = true;
        }
        if (!any && (s.Contains("نیم ساعت", StringComparison.Ordinal) || s.Contains("half an hour", StringComparison.Ordinal))) { sum = 1800; any = true; }
        return any && sum is >= 5 and <= 10800 ? (sum, total) : (null, false);
    }

    private static readonly string[] CheckupWords = ["عیب یاب", "عیبیاب", "چکاپ", "چک اپ", "check up", "checkup", "diagnos", "troubleshoot"];
    private static readonly string[] SystemWords = ["سیستم", "کامپیوتر", "کامپیوترم", "لپتاپ", "لپ تاپ", "پی سی", "pc", "computer", "laptop", "system"];
    /// <summary>The words that make a sentence a command (whole words: "کن" must not match "کنار").</summary>
    private static readonly HashSet<string> CommandTokens = new(["کن", "کنی", "کنید", "بکن", "بگیر", "بگیری", "بزن", "بزنی", "انجام", "اجرا", "شروع", "do", "run", "start", "perform", "check", "test", "scan", "diagnose"], StringComparer.Ordinal);

    /// <summary>Whether a message asks the whole computer to be diagnosed: the diagnosis named with a word of doing ("سیستم رو عیب یابی کن", "چکاپ بگیر"),
    /// or the computer as a whole checked with no part named ("سیستمم رو چک کن").</summary>
    private static bool WantsCheckup(string s, AppPlace? place)
    {
        bool command = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(CommandTokens.Contains);
        // The diagnosis covers the processor, memory and graphics card; the internet or a drive named is another job (its own troubleshooter or test).
        if (Any(s, CheckupWords)) return command && (place is null || place is { Page: "checkup", Target: null }) && !TestAreas.Any(x => x.Area is "network" or "storage" && Any(s, x.Words));
        if (place is not null || TestAreas.Any(a => Any(s, a.Words)) || !Any(s, SystemWords)) return false;
        return Has(s, "چک کن") || Has(s, "بررسی کن") || command && Any(s, TestWords);
    }

    private static int Words(string norm) => norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The pages as the model is told of them: id, the name the page shows (in the user's language) and what is there.</summary>
    public static string PageList(Func<string, string> name) =>
        string.Join("; ", Places.Where(p => p.Target is null).Select(p => $"{p.Page} = «{name(p.TitleKey)}»: {p.What}"));
}
