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
public enum AiIntent { None, Navigate, HowTo, Specs, Sensors, Software, SoftwareList, Report, ReportFile, Tests, Overlay, Dns, Games, TestsInfo, Help, Complaint, Tray, Alert, WinOpen, WinCommand, WinCommandUnknown, Drivers, Benchmarks, PcieErrors, Crashes, Checkup }

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
    private static readonly AiLexicon Lex = AiLexicon.Load();
    private static AppPlace P(string page, string key, string what) => new(page, null, key, Lex.Places(page), what);
    private static AppPlace T(string page, string target, string key, string what, string? hint) => new(page, target, key, Lex.Places(page, target), what, hint);

    public static IReadOnlyList<AppPlace> Places { get; } =
    [
        P("dashboard", "Nav_Dashboard", "summary of the computer and its live state"),
        P("monitoring", "Nav_Monitoring", "every sensor with live charts: temperatures, loads, clocks, fans, power"),
        P("system", "Nav_SystemInfo", "the whole specification of the computer, with export to PDF"),
        T("system", "export", "System_Export", "export the specification as PDF, HTML or JSON", null),
        P("cpu", "Nav_Cpu", "the processor's specification and sensors"),
        P("gpu", "Nav_Gpu", "the graphics card's specification and sensors"),
        P("ram", "Dashboard_Ram", "the memory modules and their sensors"),
        P("storage", "Nav_Storage", "the drives, their health and sensors"),
        P("network", "Nav_Network", "the network adapters"),
        P("tests", "Nav_Tests", "hardware tests with a report: the memory tests check the RAM's health over nearly all of it (other programs' memory is paged out first), the drive tests the disks' (SMART and speed), the processor, network and graphics card tests theirs; the graphics card's VRAM test fills nearly all of its memory and shows the bandwidth, VRAM temperature and memory clock; separate video decode and encode tests (H.264, HEVC, VP9, AV1 at 1080p, 4K and 8K, 8 and 10 bit, 4:2:0 and 4:2:2) check every frame; ready-made sets (quick check, everyday, Mazesta default); a switch runs processor, memory and graphics card tests together; when the tests end a window sums up each part (processor, memory, graphics card, drives): healthy or not, highest temperature, load, what needs a look, with a button to the report's summary"),
        T("tests", "start", "Test_Start", "start the ticked tests", null),
        P("benchmarks", "Nav_Benchmarks", "speed benchmarks and comparison with other computers; the memory benchmark also measures access latency (at 32 KB, 256 KB, 2 MB, 16 MB, 64 MB and the RAM itself); the Iranian-garden 3D scene benchmarks (normal and ray-traced, at 2560x1440) walk the garden once at walking pace and need no setting; the video benchmark measures the card's hardware decoding (H.264, HEVC, VP9, AV1; 1080p, 4K, 8K; 8 and 10 bit; 4:2:0 and 4:2:2) and encoding (H.264, HEVC, AV1) in frames a second; 7-Zip, AI compute (FP32, FP16, INT8), ray tracing and internet speed benchmarks too"),
        P("checkup", "Nav_Checkup", "one-click smart diagnosis: runs the benchmarks and judges the computer from them (the assistant can run it itself and tell the findings)"),
        T("checkup", "run", "Checkup_Run", "start the diagnosis", null),
        P("checks", "Nav_Checks", "hands-on checks a person judges: screen, keyboard, mouse, speakers, microphone, and the laptop battery's health"),
        T("checks", "display", "Checks_Display", "full-screen colours for dead or stuck pixels", null),
        T("checks", "keys", "Checks_Keys", "every key of the keyboard lights up when pressed", null),
        T("checks", "mouse", "Checks_Mouse", "mouse and touchpad buttons, wheel, double clicks", null),
        T("checks", "speakers", "Checks_Speakers", "left, right and both speakers, and a frequency sweep", null),
        T("checks", "mic", "Checks_Mic", "records the microphone and shows its level", null),
        T("checks", "battery", "Checks_Battery", "the laptop battery: its health percent (full capacity against capacity when new), charge cycles, and a drain test", null),
        P("overlay", "Nav_Overlay", "settings of the on-screen overlay: temperatures, loads, frame rate (with the 1% and 0.1% lows), and the link (ping, packet loss, jitter, download and upload) shown over games; four sizes (small, medium, large, extra large) and a panel that is as narrow as its rows; the game preset shows frame rate with its lows, GPU temperature, hot spot, load, clock, memory and power, and CPU temperature, load, clock, power and busiest thread, with the average clock of the P-cores and of the E-cores on an Intel CPU that has both, and VRAM temperature; the troubleshooting preset shows everything (every temperature including each AMD CCD's, clocks, voltages, power, fans, VRAM temperature and clock, RAM and drives); any of the processor's or graphics card's sensors from the monitoring page can be added one by one; shortcuts: show/hide, move, Alt+U cycles layouts, Alt+P cycles presets"),
        P("tuning", "Nav_Tuning", "graphics card tuning (the page is called «تنظیم کارت گرافیک»): automatic and manual overclock and undervolt, clocks, voltage curve, power limit, the card's fan, saved profiles that switch by program; RAM XMP"),
        P("lights", "Nav_Lights", "RGB lighting colours and effects of memory, graphics card, board, fans and strips (Corsair, ASUS, Gigabyte, MSI), through OpenRGB; the LED count of a board header"),
        P("fans", "Nav_Fans", "motherboard fan control: automatic, fixed speed, or a temperature curve you drag (silent, standard, performance, full speed)"),
        T("tuning", "autoundervolt", "Tuning_AutoUndervolt", "finds the lowest stable voltage by itself (10 to 25 minutes)", "Assist_Hint_Tuning"),
        T("tuning", "autooverclock", "Tuning_AutoOverclock", "raises the clocks step by step while the card stays correct (10 to 25 minutes)", "Assist_Hint_Tuning"),
        T("tuning", "curve", "Tuning_Curve_Title", "measures and shows the card's voltage-frequency curve", null),
        T("tuning", "profiles", "Tuning_Profiles", "saved clock profiles of this card", null),
        T("tuning", "memory", "Tuning_Memory", "RAM XMP / DOCP / EXPO (set in the BIOS; a button restarts into it)", null),
        T("tuning", "fan", "Tuning_Fan", "the graphics card's fan speed", null),
        P("tools", "Nav_WindowsTools", "Windows tools: DNS, the internet troubleshooter, blue screens, SFC/DISM repair, Disk Cleanup, hibernation, virtual memory, hosts file"),
        T("tools", "dns", "Dns_Title", "switch the DNS server, or test them all and pick the fastest", null),
        P("play", "Nav_Gaming", "gaming: power plan, Game Mode and HAGS, and the game mode that stops background services"),
        T("play", "power", "Gaming_PowerPlan", "Windows power plans (High, Ultimate Performance)", null),
        T("play", "gamemode", "Gaming_GameMode", "Game Mode and hardware GPU scheduling (HAGS)", null),
        T("tools", "repair", "Tools_Repair", "Windows' own repair: SFC and DISM", "Assist_Hint_Repair"),
        T("tools", "hibernate", "Tools_Hib_Title", "hibernation and Fast Startup on or off", null),
        T("tools", "vm", "Tools_Vm_Title", "virtual memory (the page file)", null),
        T("tools", "hosts", "Tools_Hosts_Title", "edit the hosts file", null),
        T("tools", "cleanup", "Tools_Windows", "Disk Cleanup and Windows Update's window", null),
        P("tweaks", "Nav_Tweaks", "Windows tweaks (telemetry, widgets, background apps) and preferences, each undoable"),
        P("shield", "Nav_Shield", "firewall and antivirus: block a program in the Windows firewall (incoming and outgoing), leave a file or folder out of Windows Defender's scans, and put both in Explorer's right-click menu"),
        P("drivers", "Nav_Drivers", "drivers: the graphics card's driver against NVIDIA's newest (Game Ready or Studio, suggested from the installed programs), the drivers Windows Update offers, and the devices without a working driver; download and install each with a button"),
        P("updates", "Nav_Updates", "Windows Update: default, recommended (deferred) or off"),
        P("reports", "Nav_Reports", "saved test and benchmark reports: summary (one A5 sheet: the highest temperatures in one row, one line a test with its main figures, the system, drive health, and a note that Mazesta Test is installed on the customer's system), PDF, HTML, before/after comparison; the company's copy needs a service number to send a report to the site, where both the summary and the whole report are kept"),
        T("reports", "compare", "Reports_Compare", "compare two ticked reports, before and after", null),
        P("apps", "Nav_Apps", "which professional programs (rendering, architecture, civil, animation, editing) run on this computer, and at what level"),
        P("games", "Nav_Games", "which games run on this computer, at their publishers' minimum, recommended and high tiers and the publishers' own targets (resolution, preset, frame rate)"),
        P("ai", "Nav_Ai", "local AI models: which run here, downloads, speed benchmark; image, video, audio and 3D models"),
        P("help", "Nav_Help", "the in-app help: what every page and box does, with a search"),
        P("messages", "Nav_Messages", "messages from the shop to this computer's owner"),
        P("settings", "Nav_Settings", "the app's settings: language, units, render mode, the tray monitor and its temperature warnings, data folder"),
        T("play", "gameboost", "GameBoost_Title", "game mode: stops background services a game does not need (Windows Update, downloads, telemetry, search indexing) while it is on, with a tick per service; switching it off puts them back", null),
        T("tools", "netfix", "NetFix_Title", "internet connection troubleshooter: checks adapter, router, internet, DNS, a web page and the proxy, then clears the proxy, sets the DNS, empties the DNS cache or resets the network", null),
        T("tools", "crashes", "Bsod_Title", "blue screens (stop errors): lists the ones Windows recorded, with the stop code, its name, its parameters and the likely causes to check", null),
        P("appupdate", "Nav_AppUpdate", "update this app"),
        T("appupdate", "site", "Site_Title", "the link to the shop's site: the site key is entered here; a report's summary is sent to the site from the reports page (to be printed there for the serviced case) and benchmark results from the benchmark page, where anyone can also share their latest results as a page with a link (no key needed)", null),
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

    private static readonly string[] GoWords = Lex.List("GoWords");
    private static readonly string[] PageWords = Lex.List("PageWords");
    private static readonly string[] DoWords = Lex.List("DoWords");
    private static readonly string[] HowWords = Lex.List("HowWords");
    private static readonly string[] AskWords = Lex.List("AskWords");
    private static readonly string[] TestWords = Lex.List("TestWords");
    private static readonly string[] SummaryWords = Lex.List("SummaryWords");
    private static readonly string[] RunWords = Lex.List("RunWords");
    private static readonly string[] AppsWords = Lex.List("AppsWords");

    private static readonly (string Part, string[] Words)[] SpecParts = Lex.Group("SpecParts");
    private static readonly (string Area, string[] Words)[] TestAreas = Lex.Group("TestAreas");
    private static readonly (SoftCategory Category, string[] Words)[] Categories = [.. Lex.Group("Categories").Select(g => (Enum.Parse<SoftCategory>(g.Key), g.Words))];

    private static readonly string[] AllWords = Lex.List("AllWords");
    private static readonly string[] TogetherWords = Lex.List("TogetherWords");
    private static readonly string[] CrashWords = Lex.List("CrashWords");
    private static readonly string[] OverlayWords = Lex.List("OverlayWords");
    // A command said not to be done ("اجرا نکن", "فقط توضیح بده") is never acted on: the model explains, and nothing starts.
    private static readonly string[] NotWords = Lex.List("NotWords");
    private static readonly string[] EarlierWords = Lex.List("EarlierWords");
    private static readonly string[] OnWords = Lex.List("OnWords");
    private static readonly string[] OffWords = Lex.List("OffWords");
    private static readonly (string Kind, string[] Words)[] SensorKinds = Lex.Group("SensorKinds");
    // "What can you do": the app answers with what it really has, not the model with what it imagines.
    private static readonly string[] HelpWords = Lex.List("HelpWords");
    private static readonly string[] TrayWords = Lex.List("TrayWords");
    private static readonly string[] AlertWords = Lex.List("AlertWords");
    private static readonly string[] WhenWords = Lex.List("WhenWords");
    private static readonly string[] CommandWords = Lex.List("CommandWords");
    private static readonly string[] NowWords = Lex.List("NowWords");
    // A complaint ("slow", "freezes") is for the model with its tools; a question about hot parts reads the sensors; "buy / price / phone" is the shop's.
    private static readonly string[] ProblemWords = Lex.List("ProblemWords"), HeatWords = Lex.List("HeatWords"), BuyWords = Lex.List("BuyWords");
    // «چطوره» asks how something is (its state), unlike «چطور» how to do it; a past tense with «highest» asks for what was recorded.
    private static readonly string[] StateWords = Lex.List("StateWords"), PastWords = Lex.List("PastWords"), MaxWords = Lex.List("MaxWords");

    /// <summary>What a message asks for, when it is plain; <see cref="AiIntent.None"/> leaves it to the model. The order matters: a question of
    /// "how" is answered, not acted on; the reports and the programs have their own words; a part's figure is a question about this computer;
    /// a test of the keyboard or the microphone is the hands-on page, a test of a part is a run; last, a page named with a word of going there.</summary>
    public static AiRoute Route(string text)
    {
        string s = Normalize(text);
        var place = FindPlace(s); var app = FindApp(s);
        bool go = Any(s, GoWords), page = Any(s, PageWords), doIt = Any(s, DoWords), how = Any(s, HowWords) && !Any(s, StateWords), ask = Any(s, AskWords);
        bool not = Any(s, NotWords);

        if (Any(s, HelpWords) && place is null && app is null) return new(AiIntent.Help);

        // The shop, a price or a computer to buy: the model answers from company_info / ready_systems, never from a page ("کیس گیمینگ" is not the gaming page).
        if (Any(s, BuyWords) && app is null && !go) return new(AiIntent.None);
        // "It gets hot": the temperatures now, of the part named; the model then tells what they mean.
        if (Any(s, HeatWords) && !go && !not) return new(AiIntent.Sensors, Kind: "Temperature", Part: TestAreas.FirstOrDefault(a => Any(s, a.Words)).Area);

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

        // A complaint ("my Windows is slow, what do I do"): not a question about the specification ("چیکار" starts with "چی"); the app answers with what it
        // can check, since a small model talked of removing Windows. A control that names the trouble itself (the internet troubleshooter) is still pointed at.
        if (Any(s, ProblemWords) && place?.Target is null && !go && !(SensorKinds.Any(k => Any(s, k.Words)) && (Any(s, NowWords) || ask))) return new(AiIntent.Complaint, place, App: app);

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
        if (!go && !test && SensorKinds[0].Words.Any(w => Has(s, w)) && Any(s, MaxWords) && Any(s, PastWords))
            return new(AiIntent.Report, Part: TestAreas.FirstOrDefault(a => Any(s, a.Words)).Area, Kind: "Temperature", Index: Any(s, EarlierWords) ? 1 : 0);
        if (!go && !test && SensorKinds.FirstOrDefault(k => Any(s, k.Words)) is { Kind: not null } sensor && (Any(s, NowWords) || ask))
            return new(AiIntent.Sensors, Kind: sensor.Kind, Part: TestAreas.FirstOrDefault(a => Any(s, a.Words)).Area);
        // A benchmark's number is its history, not the part's specification ("آخرین بنچمارک پردازنده چند بود").
        if (!go && !test && ask && !Has(s, "بنچمارک") && !Has(s, "بنچ مارک") && !Has(s, "benchmark") && SpecParts.FirstOrDefault(p => Any(s, p.Words)) is { Part: not null } spec)
            return new(AiIntent.Specs, Part: spec.Part);

        // "بنچمارک سیستم رو انجام بده": the benchmarks of the parts named, or all of them, asked about together on one card.
        bool bench = Has(s, "بنچمارک") || Has(s, "بنچ مارک") || Has(s, "benchmark");
        if (bench && (doIt || Words(s) <= 3 && TestAreas.Any(a => Any(s, a.Words))) && !go && !page && !not && !ask)
        {
            var areas = TestAreas.Where(a => Any(s, a.Words)).SelectMany(a => a.Area switch
            {
                "cpu" => new[] { "cpu_single", "cpu_multi" }, "gpu" => ["gpu_rt", "gpu_scene", "gpu_ai", "gpu_video"], var x => [x],
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
        // A verb of doing or switching ("فن رو روی سایلنت بذار", "حالت بازی رو روشن کن") shows where it is done; the assistant does not press it.
        bool act = !not && (doIt || Any(s, OnWords) || Any(s, OffWords) || s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(CommandTokens.Contains));
        if (place is not null && (go || page || short_ || act || place.Target is not null && !ask)) return new(AiIntent.Navigate, place);
        return new(AiIntent.None, place, App: app);
    }

    private static readonly Regex LengthPart = new(@"(?<in>(?:در|تو|طی|ظرف|in|within)\s+)?(?<n>\d{1,5})\s*(?<u>ساعت|hours?|hrs?|h|دقیقه|minutes?|mins?|m|ثانیه|seconds?|secs?|s)(?![a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] TotalWords = Lex.List("TotalWords");

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

    private static readonly string[] CheckupWords = Lex.List("CheckupWords");
    private static readonly string[] SystemWords = Lex.List("SystemWords");
    /// <summary>The words that make a sentence a command (whole words: "کن" must not match "کنار").</summary>
    private static readonly HashSet<string> CommandTokens = new(["کن", "کنی", "کنید", "بکن", "بگیر", "بگیری", "بزن", "بزنی", "انجام", "اجرا", "شروع", "کردن", "پایداری", "do", "run", "start", "perform", "check", "test", "scan", "diagnose"], StringComparer.Ordinal);

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
