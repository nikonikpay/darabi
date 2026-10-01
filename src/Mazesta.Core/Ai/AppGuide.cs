using System.Text; using Mazesta.Core.Software;
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
public enum AiIntent { None, Navigate, HowTo, Specs, Software, SoftwareList, Report, ReportFile, Tests }

/// <param name="Part">For <see cref="AiIntent.Specs"/>: cpu, ram, gpu, vram, storage, board, os, or all.</param>
/// <param name="Format">For <see cref="AiIntent.ReportFile"/>: pdf, html or summary.</param>
/// <param name="Category">For <see cref="AiIntent.SoftwareList"/>: the group asked about, or null for all.</param>
public sealed record AiRoute(AiIntent Intent, AppPlace? Place = null, string? Part = null, SoftApp? App = null, IReadOnlyList<string>? Areas = null, string? Format = null, SoftCategory? Category = null);

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
        P("tests", "Nav_Tests", "hardware tests (processor, memory, drives, network, graphics card) with a report", "تستها", "تست ها", "صفحه تست", "بخش تست", "tests"),
        T("tests", "start", "Test_Start", "start the ticked tests", null, "شروع تست", "شروع تستها"),
        P("benchmarks", "Nav_Benchmarks", "speed benchmarks and comparison with other computers", "بنچمارک", "بنچ مارک", "بنچمارکها", "benchmark", "benchmarks"),
        P("checkup", "Nav_Checkup", "one-click diagnosis: runs the benchmarks and judges the computer from them", "عیب یابی", "عیبیابی", "عیب یابی هوشمند", "چکاپ", "checkup", "diagnosis"),
        T("checkup", "run", "Checkup_Run", "start the diagnosis", null, "اجرای عیب یابی", "شروع عیب یابی"),
        P("checks", "Nav_Checks", "hands-on checks a person judges: screen, keyboard, mouse, speakers, microphone", "بررسی دستی", "بررسیهای دستی", "تست دستی", "hands-on", "checks"),
        T("checks", "display", "Checks_Display", "full-screen colours for dead or stuck pixels", null, "پیکسل سوخته", "پیکسل", "نمایشگر", "صفحه نمایش", "مانیتور", "ال سی دی", "lcd", "dead pixel", "display"),
        T("checks", "keys", "Checks_Keys", "every key of the keyboard lights up when pressed", null, "کیبورد", "کیبرد", "صفحه کلید", "keyboard"),
        T("checks", "mouse", "Checks_Mouse", "mouse and touchpad buttons, wheel, double clicks", null, "ماوس", "موس", "تاچ پد", "تاچپد", "mouse", "touchpad"),
        T("checks", "speakers", "Checks_Speakers", "left, right and both speakers, and a frequency sweep", null, "بلندگو", "اسپیکر", "هدفون", "هدست", "صدا", "speaker", "speakers", "headphone"),
        T("checks", "mic", "Checks_Mic", "records the microphone and shows its level", null, "میکروفون", "میکروفن", "میکرفون", "مایک", "microphone", "mic"),
        P("overlay", "Nav_Overlay", "settings of the on-screen overlay: temperatures, loads and frame rate shown over games", "اورلی", "اورلای", "اوورلی", "overlay", "نمایش روی صفحه", "fps"),
        P("tuning", "Nav_Tuning", "graphics card overclock and undervolt: clocks, voltage curve, power limit, fans, profiles; RAM XMP", "اورکلاک", "اور کلاک", "اندرولت", "اندروالت", "آندر ولت", "اندر ولت", "تیونینگ", "undervolt", "overclock", "tuning"),
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
        P("updates", "Nav_Updates", "Windows Update: default, recommended (deferred) or off", "اپدیت ویندوز", "آپدیت ویندوز", "به روزرسانی ویندوز", "بروزرسانی ویندوز", "windows update"),
        P("reports", "Nav_Reports", "saved test and benchmark reports: summary, PDF, HTML, before/after comparison", "گزارش", "گزارشها", "گزارش ها", "ریپورت", "reports", "report"),
        T("reports", "compare", "Reports_Compare", "compare two ticked reports, before and after", null, "مقایسه گزارش", "مقایسه قبل و بعد", "compare"),
        P("apps", "Nav_Apps", "which professional programs (rendering, architecture, civil, animation, editing) run on this computer, and at what level", "برنامه ها", "برنامههای تخصصی", "نرم افزار", "نرمافزار", "نرم افزارها", "برنامه های رندرینگ", "software", "apps"),
        P("ai", "Nav_Ai", "local AI models: which run here, downloads, speed benchmark; image, video, audio and 3D models", "مدل هوش مصنوعی", "مدلهای هوش مصنوعی", "مدل های هوش مصنوعی", "هوش مصنوعی", "llm", "مدل زبانی", "ai models"),
        P("settings", "Nav_Settings", "the app's settings: language, units, render mode, tray, data folder", "تنظیمات", "تنظیمات برنامه", "ستینگ", "settings"),
        P("appupdate", "Nav_AppUpdate", "update this app", "اپدیت برنامه", "آپدیت برنامه", "به روزرسانی برنامه", "نسخه برنامه", "app update"),
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
        ("all", ["مشخصات", "سیستمم", "سیستم من", "کامپیوترم", "لپ تاپم", "specs", "specification"]),
    ];
    private static readonly (string Area, string[] Words)[] TestAreas =
    [
        ("gpu", ["کارت گرافیک", "گرافیک", "gpu", "graphics"]), ("cpu", ["cpu", "سی پی یو", "پردازنده", "processor"]),
        ("memory", ["رم", "ram", "حافظه", "memory"]), ("storage", ["هارد", "ssd", "دیسک", "درایو", "storage", "drive"]), ("network", ["شبکه", "اینترنت", "network"]),
    ];
    private static readonly (SoftCategory Category, string[] Words)[] Categories =
    [
        (SoftCategory.Visualization, ["رندرینگ", "رندر", "ریل تایم", "render", "rendering"]), (SoftCategory.Architecture, ["معماری", "architecture", "bim"]),
        (SoftCategory.Civil, ["عمران", "سازه", "civil", "structural"]), (SoftCategory.Animation, ["انیمیشن", "جلوه ویژه", "جلوه های ویژه", "سه بعدی", "animation", "vfx"]),
        (SoftCategory.Video, ["تدوین", "ادیت", "ویدیو", "ویدئو", "editing", "video"]), (SoftCategory.Graphics, ["گرافیکی", "طراحی گرافیک", "graphic design"]),
    ];

    /// <summary>What a message asks for, when it is plain; <see cref="AiIntent.None"/> leaves it to the model. The order matters: a question of
    /// "how" is answered, not acted on; the reports and the programs have their own words; a part's figure is a question about this computer;
    /// a test of the keyboard or the microphone is the hands-on page, a test of a part is a run; last, a page named with a word of going there.</summary>
    public static AiRoute Route(string text)
    {
        string s = Normalize(text);
        var place = FindPlace(s); var app = FindApp(s);
        bool go = Any(s, GoWords), page = Any(s, PageWords), doIt = Any(s, DoWords), how = Any(s, HowWords), ask = Any(s, AskWords);

        if (how && !go) return new(AiIntent.HowTo, place, App: app);

        bool report = Has(s, "گزارش") || Has(s, "ریپورت") || Has(s, "report") || Has(s, "نتیجه تست") || Has(s, "نتایج تست") || Has(s, "نتیجه بنچمارک");
        if (report)
        {
            string? format = Has(s, "pdf") || Has(s, "پی دی اف") || Has(s, "پیدیاف") ? (Has(s, "خلاصه") || Has(s, "summary") ? "summary" : "pdf")
                : Has(s, "html") || Has(s, "اچ تی ام ال") || Has(s, "صفحه وب") ? "html" : null;
            if (format is not null) return new(AiIntent.ReportFile, Format: format);
            if (Any(s, SummaryWords) || ask && !go) return new(AiIntent.Report);
            return new(AiIntent.Navigate, place is { Page: "reports" } ? place : Page("reports"));
        }

        if (app is not null)
            return go && !Any(s, RunWords) ? new(AiIntent.Navigate, Page("apps"), App: app) : new(AiIntent.Software, App: app);
        if (Any(s, AppsWords) && (Any(s, RunWords) || ask) && !go)
            return new(AiIntent.SoftwareList, Category: Categories.Where(c => Any(s, c.Words)).Select(c => (SoftCategory?)c.Category).FirstOrDefault());

        bool test = Any(s, TestWords);
        if (!go && !test && ask && SpecParts.FirstOrDefault(p => Any(s, p.Words)) is { Part: not null } spec)
            return new(AiIntent.Specs, Part: spec.Part);

        if (test && place is { Page: "checks" }) return new(AiIntent.Navigate, place);
        if (test && !go && !page)
        {
            var areas = TestAreas.Where(a => Any(s, a.Words)).Select(a => a.Area).ToList();
            // "رم گرافیک" is the card's memory: the card's test covers it, not the RAM's.
            if (areas.Contains("gpu") && (Has(s, "رم گرافیک") || Has(s, "حافظه گرافیک"))) areas.Remove("memory");
            if (areas.Count > 0) return new(AiIntent.Tests, Areas: areas);
        }

        // A short message that is little more than a place's name ("اورلی", "گرافیکو اندرولت کن") means going there.
        bool short_ = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4 && !ask;
        if (place is not null && (go || page || short_ || doIt && place.Target is not null)) return new(AiIntent.Navigate, place);
        return new(AiIntent.None, place, App: app);
    }

    /// <summary>The pages as the model is told of them: id, the name the page shows (in the user's language) and what is there.</summary>
    public static string PageList(Func<string, string> name) =>
        string.Join("; ", Places.Where(p => p.Target is null).Select(p => $"{p.Page} = «{name(p.TitleKey)}»: {p.What}"));
}
