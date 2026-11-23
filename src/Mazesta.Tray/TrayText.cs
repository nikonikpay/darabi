using System.Globalization; using Mazesta.Core.Health; using Mazesta.Diagnostics.Storage; using Mazesta.Persistence;
namespace Mazesta.Tray;

/// <summary>The tray's wording in the app's two languages (Persian by default, English when appconfig.json says "en"). Every entry is read when a window or menu is built, so a language change in the app shows in the tray without restarting it. Values keep Latin digits, as in the app.</summary>
internal static class TrayText
{
    private static string? _file; private static DateTime _stamp; private static bool _fa = true;
    /// <summary>Tells the table where the app's settings live; called once at start.</summary>
    public static void Watch(string configFile) => _file = configFile;
    /// <summary>True for Persian. The settings file is re-read only when its timestamp changes.</summary>
    public static bool Fa
    {
        get
        {
            if (_file is null) return _fa;
            try
            {
                var t = File.GetLastWriteTimeUtc(_file);
                if (t != _stamp) { _stamp = t; using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_file)); _fa = !(doc.RootElement.TryGetProperty("language", out var l) && l.GetString() == "en"); }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
            return _fa;
        }
    }
    /// <summary>What every control's RightToLeft is set to; WinForms mirrors docking, alignment and the title bar from it (with RightToLeftLayout on a form).</summary>
    public static RightToLeft Dir => Fa ? RightToLeft.Yes : RightToLeft.No;
    public static bool Rtl => Fa;
    /// <summary>Message boxes read from the right only in Persian.</summary>
    public static MessageBoxOptions MsgOpts => Fa ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
    public static string Comma => Fa ? "، " : ", ";
    public static string KindService => T("سرویس", "Service");
    public static string KindConsole => T("کنسول", "Console");
    public static string KindApp => T("برنامه", "Program");
    private static string T(string fa, string en) => Fa ? fa : en;
    private static string[] T(string[] fa, string[] en) => Fa ? fa : en;
    private static string F(bool fa, FormattableString faText, FormattableString enText) => (fa ? faText : enText).ToString(CultureInfo.InvariantCulture);

    public static string Title => T("مازستا — پایش سلامت", "Mazesta — Health monitor");
    public static string OpenApp => T("باز کردن مازستا", "Open Mazesta");
    public static string Summary => T("خلاصه‌ی وضعیت", "Status summary");
    public static string CheckNow => T("بررسی همین حالا", "Check now");
    public static string Checking => T("در حال بررسی…", "Checking…");
    public static string Exit => T("خروج", "Exit");
    public static string NoApp => T("برنامه‌ی مازستا کنار پایشگر پیدا نشد.", "The Mazesta app was not found next to the monitor.");
    public static string SummaryTitle => T("مازستا — خلاصه‌ی وضعیت سیستم", "Mazesta — System status summary");
    public static string Cpu => T("دمای پردازنده", "Processor temperature");
    public static string Gpu => T("دمای کارت گرافیک", "Graphics card temperature");
    public static string Drives => T("سلامت دیسک‌ها", "Drive health");
    public static string Recent => T("آخرین بررسی‌ها", "Latest checks");
    public static string NoChecks => T("هنوز بررسی‌ای انجام نشده است.", "No check has run yet.");
    public static string NoDrives => T("هنوز سلامت دیسک‌ها خوانده نشده است.", "Drive health has not been read yet.");
    public static string NotAvailable => T("در دسترس نیست", "Not available");
    public static string Temps => T("دما", "Temps");
    public static string Health => T("سلامت دیسک", "Drive health");
    public static string AllGood => T("مشکلی دیده نشد", "No problem found");
    public static string CheckFailed => T("بررسی انجام نشد", "The check did not run");

    // Explorer's right-click tools, each in a window of its own (Shell/LockForm, Shell/RenameForm)
    public static string LockTitle => T("چه برنامه‌ای این فایل را باز نگه داشته؟", "Which program is holding this file open?");
    public static string SearchSub => T("نام فایل یا پوشه را بنویسید؛ با هر حرف نتیجه تازه می‌شود", "Type a file or folder name; the results update with every letter");
    public static string SpaceSub => T("پوشه‌ها و فایل‌های بزرگ هر درایو، از بزرگ به کوچک", "The biggest folders and files of each drive, largest first");
    public static string LockSub => T("برنامه‌هایی که این فایل را در دست دارند و راه بستن آن‌ها", "The programs that hold this file, and a way to close them");
    public static string RenameSub => T("پیش‌نمایش نام‌های تازه؛ تا چیزی تکراری یا نامعتبر نباشد تغییری داده نمی‌شود", "A preview of the new names; nothing is renamed while a name is duplicated or invalid");
    public static string ResizeTitle => T("کوچک کردن عکس‌ها", "Shrink pictures");
    public static string ResizeSub => T("برای پیام‌رسان یا ایمیل: یک نسخهٔ کوچک‌تر کنار هر عکس ساخته می‌شود و اصل دست نمی‌خورد", "For chat or email: a smaller copy is made beside each picture and the original is left alone");
    public static string ResizeSize => T("بلندترین ضلع", "Longest side");
    public static string ResizeCustom => T("اندازهٔ دیگر…", "Other size…");
    public static string ResizeQuality => T("کیفیت", "Quality");
    public static string[] ResizeQualities => T(["بالا (حجم بیشتر)", "معمولی", "کمترین حجم"], ["High (larger file)", "Normal", "Smallest file"]);
    public static string ResizeRun => T("کوچک کن", "Shrink");
    public static string ResizeFile => T("عکس", "Picture");
    public static string ResizeResult => T("نتیجه", "Result");
    public static string ResizeBadSize => T("اندازه‌ای بین ۱۶ تا ۲۰۰۰۰ پیکسل وارد کنید.", "Enter a size between 16 and 20000 pixels.");
    public static string ResizeFormat => T("این قالب اینجا خوانده نمی‌شود (مثلاً HEIC یا WebP).", "This format cannot be read here (for example HEIC or WebP).");
    public static string ResizeProgress(int i, int n) => T($"عکس {i} از {n}…", $"Picture {i} of {n}…");
    public static string ResizeDone(int ok, int n) => T($"{ok} عکس از {n} کوچک شد.", $"{ok} of {n} pictures shrunk.");
    public static string LockPick => T("بررسی‌شده", "Checked");
    public static string LockNone => T("هیچ برنامه‌ای این را باز نگه نداشته است.", "No program is holding this open.");
    public static string LockWorking => T("در حال بررسی…", "Checking…");
    public static string LockAgain => T("بررسی دوباره", "Check again");
    public static string LockCloseOne => T("بستن برنامه‌ی انتخاب‌شده", "Close the selected program");
    public static string LockCloseAll => T("بستن همه", "Close all");
    public static string LockName => T("برنامه", "Program");
    public static string LockKind => T("نوع", "Type");
    public static string LockPath => T("مسیر", "Path");
    public static string LockState => T("وضعیت", "Status");
    public static string LockCan => T("قابل بستن", "Can be closed");
    public static string LockService => T("سرویس ویندوز؛ از اینجا بسته نمی‌شود", "A Windows service; it cannot be closed from here");
    public static string LockSystem => T("بخشی از ویندوز یا همین پایشگر است؛ از اینجا بسته نمی‌شود", "Part of Windows or of this monitor; it cannot be closed from here");
    public static string LockTruncated => T("پوشه بزرگ است؛ فقط بخشی از فایل‌های آن بررسی شد.", "The folder is large; only some of its files were checked.");
    public static string LockFound(int n) => T($"{n} برنامه این را باز نگه داشته‌اند.", n == 1 ? "1 program is holding this open." : $"{n} programs are holding this open.");
    public static string LockFailed(string why) => T($"ویندوز نتوانست بگوید چه برنامه‌ای این را باز نگه داشته ({why}). این به معنی «هیچ برنامه‌ای» نیست.", $"Windows could not say which program holds this open ({why}). That does not mean \"no program\".");
    public static string LockAsk(string name) => T($"«{name}» بسته شود؟ کارهای ذخیره‌نشده‌ی آن از بین می‌رود.", $"Close \"{name}\"? Its unsaved work will be lost.");
    public static string LockAskAll(int n) => T($"{n} برنامه بسته شوند؟ کارهای ذخیره‌نشده‌ی آن‌ها از بین می‌رود.", $"Close {n} programs? Their unsaved work will be lost.");
    public static string LockCloseFailed(string name, string why) => why switch
    {
        "protected" => T($"«{name}» بخشی از ویندوز یا همین پایشگر است و بسته نشد.", $"\"{name}\" is part of Windows or of this monitor and was not closed."),
        "gone" => T($"«{name}» پیش‌تر بسته شده بود.", $"\"{name}\" had already closed."),
        _ => T($"«{name}» بسته نشد: {why}", $"\"{name}\" was not closed: {why}")
    };
    public static string Many(int n) => T($"{n} مورد", n == 1 ? "1 item" : $"{n} items");

    public static string RenameTitle => T("تغییر نام گروهی", "Batch rename");
    public static string RenameOld => T("نام فعلی", "Current name");
    public static string RenameNew => T("نام جدید", "New name");
    public static string RenameStatus => T("وضعیت", "Status");
    public static string RenameApply => T("تغییر نام", "Rename");
    public static string RenameUndo => T("برگرداندن آخرین تغییر", "Undo the last rename");
    public static string RenameClose => T("بستن", "Close");
    public static string RenameSecText => T("جایگزینی متن", "Replace text");
    public static string RenameFind => T("این متن", "Find");
    public static string RenameReplace => T("جایگزین شود با", "Replace with");
    public static string RenameRegex => T("عبارت منظم (Regex)", "Regular expression (Regex)");
    public static string RenameCase => T("حساس به حروف بزرگ و کوچک", "Match case");
    public static string RenameFirst => T("فقط اولین مورد در هر نام", "Only the first match in each name");
    public static string RenameSecAdd => T("افزودن و حذف", "Add and remove");
    public static string RenamePrefix => T("متن اول نام", "Text at the start");
    public static string RenameSuffix => T("متن آخر نام", "Text at the end");
    public static string RenameCutFirst => T("حذف از ابتدا (نویسه)", "Remove from the start (characters)");
    public static string RenameCutLast => T("حذف از انتها (نویسه)", "Remove from the end (characters)");
    public static string RenameSecNumber => T("شماره‌گذاری", "Numbering");
    public static string[] RenameNumberModes => T(["بدون شماره", "نام جدید + شماره (جایگزین کل نام)", "شماره بعد از نام فعلی", "شماره قبل از نام فعلی"], ["No numbering", "New name + number (replaces the whole name)", "Number after the current name", "Number before the current name"]);
    public static string RenameBase => T("نام پایه", "Base name");
    public static string RenameStart => T("شروع از", "Start at");
    public static string RenameStep => T("گام", "Step");
    public static string RenameDigits => T("تعداد رقم (۰ = خودکار)", "Digits (0 = automatic)");
    public static string RenameSep => T("جداکننده", "Separator");
    public static string RenameSecStyle => T("حروف و فاصله", "Letters and spaces");
    public static string[] RenameCases => T(["حروف انگلیسی: بدون تغییر", "همه کوچک", "همه بزرگ", "هر کلمه با حرف بزرگ", "فقط اولین حرف بزرگ"], ["Latin letters: unchanged", "all lowercase", "ALL UPPERCASE", "Each Word Capitalised", "Only the first letter capital"]);
    public static string[] RenameSpaces => T(["فاصله‌ها: بدون تغییر", "فاصله‌های اضافه حذف شود", "فاصله ← زیرخط (_)", "فاصله ← خط تیره (-)", "فاصله ← نقطه (.)"], ["Spaces: unchanged", "Remove extra spaces", "Space → underscore (_)", "Space → hyphen (-)", "Space → dot (.)"]);
    public static string RenameLatin => T("ارقام فارسی و عربی ← لاتین", "Persian and Arabic digits → Latin");
    public static string RenamePersian => T("«ي» و «ك» عربی ← «ی» و «ک» فارسی", "Arabic yeh and kaf → Persian yeh and kaf");
    public static string RenameSecDate => T("تاریخ فایل", "File date");
    public static string[] RenameDates => T(["بدون تاریخ", "تاریخ اول نام", "تاریخ آخر نام"], ["No date", "Date at the start", "Date at the end"]);
    public static string RenameTime => T("با ساعت", "With the time");
    public static string RenameShamsi => T("تاریخ شمسی", "Persian (Solar Hijri) date");
    public static string RenameSecExt => T("پسوند", "Extension");
    public static string RenameExtLower => T("پسوند با حروف کوچک", "Lowercase extension");
    public static string RenameExtNew => T("پسوند تازه (بدون نقطه)", "New extension (no dot)");
    public static string[] RenameOrders => T(["ترتیب: بر اساس نام", "ترتیب: قدیمی‌ترین اول", "ترتیب: جدیدترین اول", "ترتیب: کوچک‌ترین اول", "ترتیب: بزرگ‌ترین اول"], ["Order: by name", "Order: oldest first", "Order: newest first", "Order: smallest first", "Order: largest first"]);
    public static string RenameSame => T("بدون تغییر", "Unchanged");
    public static string RenameClash => T("نام تکراری", "Duplicate name");
    public static string RenameBad => T("نام نامعتبر", "Invalid name");
    public static string RenameReadyN(int ready, int total) => T($"{ready} از {total} فایل تغییر می‌کند.", $"{ready} of {total} files will be renamed.");
    public static string RenameBlockedN(int n) => T($"{n} نام تکراری یا نامعتبر است؛ تا برطرف نشود تغییری داده نمی‌شود.", $"{n} names are duplicated or invalid; nothing is renamed until that is fixed.");
    public static string RenameDone(int n) => T($"نام {n} فایل تغییر کرد.", n == 1 ? "1 file was renamed." : $"{n} files were renamed.");
    public static string RenameFailed(string names) => T($" تغییر این‌ها انجام نشد: {names}", $" These could not be renamed: {names}");
    public static string RenameUndone(int n) => T($"{n} فایل به نام قبلی برگشت.", n == 1 ? "1 file went back to its old name." : $"{n} files went back to their old names.");
    public static string RenameBadRegex(string why) => T($"عبارت منظم درست نیست: {why}", $"The regular expression is not valid: {why}");
    public static string RenameSecOrder => T("ترتیب فایل‌ها", "File order");
    // the "?" beside each group: what it does, in the order the steps run, with one example each
    public static string RenameHelpText => T("در نام هر فایل، «این متن» پیدا می‌شود و به‌جایش «جایگزین شود با» می‌آید؛ پسوند دست نمی‌خورد.\nمثال: این متن = IMG و جایگزین = PHOTO، نام IMG_001.jpg را PHOTO_001.jpg می‌کند. اگر «جایگزین» خالی باشد، متن پیداشده حذف می‌شود.\n«عبارت منظم» برای الگوهای پیشرفته است؛ مثلاً با الگوی «رقم‌ها» می‌توان همه‌ی عددهای نام را یک‌جا پیدا کرد. «فقط اولین مورد» از هر نام یک مورد را عوض می‌کند.",
        "In each file's name, \"Find\" is located and \"Replace with\" takes its place; the extension is not touched.\nExample: Find = IMG and Replace = PHOTO turns IMG_001.jpg into PHOTO_001.jpg. If \"Replace with\" is empty, the text found is deleted.\n\"Regular expression\" is for advanced patterns; for example the pattern \\d+ finds every number in a name at once. \"Only the first match\" changes one match per name.");
    public static string RenameHelpAdd => T("اول از ابتدا و انتهای نام (بدون پسوند) چند نویسه حذف می‌شود، بعد «متن اول» و «متن آخر» اضافه می‌شود.\nمثال: حذف از ابتدا = ۴ و متن آخر = «_final» نام IMG_001.jpg را 001_final.jpg می‌کند.",
        "First some characters are removed from the start and end of the name (without the extension), then \"Text at the start\" and \"Text at the end\" are added.\nExample: Remove from the start = 4 and Text at the end = \"_final\" turns IMG_001.jpg into 001_final.jpg.");
    public static string RenameHelpNumber => T("با «نام جدید + شماره» همه‌ی نام‌ها با «نام پایه» و یک شماره‌ی پشت‌سرهم عوض می‌شود: سفر 01، سفر 02، …\nحالت‌های «بعد از نام فعلی» و «قبل از نام فعلی» نام را نگه می‌دارند و شماره را کنارش می‌گذارند.\n«شروع از» اولین شماره و «گام» فاصله‌ی شماره‌هاست. «تعداد رقم» صفرهای ابتدای شماره را تعیین می‌کند (۰ = خودکار). «جداکننده» بین نام و شماره قرار می‌گیرد. شماره‌ها به ترتیب بخش «ترتیب فایل‌ها» داده می‌شوند.",
        "With \"New name + number\" every name becomes the base name and a running number: Trip 01, Trip 02, …\n\"Number after / before the current name\" keeps the name and puts the number beside it.\n\"Start at\" is the first number and \"Step\" the gap between numbers. \"Digits\" sets the leading zeros (0 = automatic). \"Separator\" goes between the name and the number. Numbers are given in the order chosen under \"File order\".");
    public static string RenameHelpStyle => T("حروف انگلیسی نام را کوچک یا بزرگ می‌کند و فاصله‌ها را مرتب یا با «_»، «-» یا «.» عوض می‌کند.\n«ارقام فارسی و عربی ← لاتین» ۱۲۳ را 123 می‌کند. «ي» و «ك» عربی که از متن‌های کپی‌شده می‌آیند، به «ی» و «ک» فارسی تبدیل می‌شوند.",
        "Changes the case of Latin letters and tidies spaces or swaps them for \"_\", \"-\" or \".\".\n\"Persian and Arabic digits → Latin\" turns ۱۲۳ into 123. The Arabic yeh and kaf that come from copied text are changed to the Persian ones.");
    public static string RenameHelpDate => T("تاریخ آخرین تغییر هر فایل، به شکل 2025-03-21، به ابتدا یا انتهای نام اضافه می‌شود. «با ساعت» ساعت و دقیقه را هم می‌آورد و «تاریخ شمسی» تاریخ را شمسی می‌نویسد.",
        "Each file's last-modified date, written like 2025-03-21, is added at the start or end of the name. \"With the time\" adds hours and minutes too, and \"Persian date\" writes the date in the Solar Hijri calendar.");
    public static string RenameHelpExt => T("«پسوند با حروف کوچک» JPG را jpg می‌کند. «پسوند تازه» پسوند همه‌ی فایل‌ها را عوض می‌کند، بدون نقطه (مثلاً png). فقط نام را عوض می‌کند و نوع فایل را تبدیل نمی‌کند.",
        "\"Lowercase extension\" turns JPG into jpg. \"New extension\" replaces every file's extension, without a dot (for example png). It only changes the name; it does not convert the file.");
    public static string RenameHelpOrder => T("ترتیب فایل‌ها فقط روی شماره‌گذاری اثر دارد: فایل اول در این ترتیب اولین شماره را می‌گیرد. برای عکس‌هایی که با دوربین گرفته‌اید، «قدیمی‌ترین اول» معمولاً همان ترتیب عکس‌برداری است.",
        "File order only affects numbering: the first file in this order gets the first number. For photos from a camera, \"oldest first\" is usually the order they were taken in.");
    public static string RenameSlow => T("این عبارت منظم برای این نام‌ها بیش از حد کند است.", "This regular expression is too slow for these names.");

    // each program's volume (Volume/VolumeForm)
    public static string Mixer => T("صدای برنامه‌ها", "App volume");
    public static string MixerDefault => T("پیش‌فرض", "Default");
    public static string MixerDevice => T("صدای کل این خروجی", "Master volume of this output");
    public static string MixerWindows => T("صداهای ویندوز", "Windows sounds");
    public static string MixerNone => T("هیچ برنامه‌ای روی این خروجی صدا ندارد.", "No program is playing sound on this output.");
    public static string MixerNoDevice => T("هیچ خروجی صدایی پیدا نشد.", "No audio output was found.");

    // the screen colour picker (Colors/ColorForm)
    public static string Color => T("انتخاب رنگ از صفحه", "Pick a colour from the screen");
    public static string ColorAgain => T("انتخاب دوباره", "Pick again");
    public static string ColorCopy => T("کپی", "Copy");
    public static string ColorCopied => T("کپی شد", "Copied");
    public static string ColorRecent => T("رنگ‌های اخیر (کلیک: کپی HEX)", "Recent colours (click to copy HEX)");
    public static string ColorMenu(string? key) => key is null ? Color : $"{Color}  ({key})";

    // where the disk space went (Space/SpaceForm)
    public static string Space => T("فضای دیسک کجا رفته", "Where the disk space went");
    public static string SpaceFind => T("جستجوی نام فایل یا پوشه…", "Search a file or folder name…");
    public static string SpaceTree => T("درخت پوشه‌ها", "Folder tree");
    public static string SpaceBiggest => T("بزرگ‌ترین فایل‌ها", "Largest files");
    public static string SpaceAgain => T("خواندن دوباره", "Read again");
    public static string SpaceFolder => T("پوشه‌ای دیگر…", "Another folder…");
    public static string SpaceShare => T("سهم", "Share");
    public static string SpaceFiles => T("فایل‌ها", "Files");
    public static string SpaceUnread => T("خوانده نشد", "Not read");
    public static string SpaceFree(string free, string total) => T($"{free} آزاد از {total}", $"{free} free of {total}");
    public static string SpaceReading(string root, int? percent) => percent is { } p ? T($"در حال خواندن {root} … {p}%", $"Reading {root} … {p}%") : T($"در حال خواندن {root} …", $"Reading {root} …");
    public static string SpaceWalking(string root, long files) => F(Fa, $"در حال خواندن {root} … {files:N0} فایل", $"Reading {root} … {files:N0} files");
    public static string SpaceDone(string size, int files, double seconds, bool table) => F(Fa,
        $"{size} در {files:N0} فایل · خوانده‌شده در {seconds:0.0} ثانیه{(table ? " از جدول فایل‌های درایو" : "")}"
        , $"{size} in {files:N0} files · read in {seconds:0.0} s{(table ? " from the drive's file table" : "")}");
    public static string SpaceFound(int total, int shown) => total > shown
        ? F(Fa, $"{total:N0} مورد پیدا شد؛ {shown:N0} بزرگ‌ترین نشان داده شده‌اند", $"{total:N0} found; the {shown:N0} largest are shown")
        : F(Fa, $"{total:N0} مورد", $"{total:N0} items");
    public static string SpaceFailed(string why) => T($"خوانده نشد: {why}", $"Could not read: {why}");

    // the quick file search (FileSearch/SearchForm)
    public static string Search => T("جستجوی فایل‌ها", "File search");
    public static string SearchTitle => T("مازستا — جستجوی فایل", "Mazesta — File search");
    public static string SearchHint => T("نام فایل یا پوشه… (مثل report یا *.mp4)", "File or folder name… (like report or *.mp4)");
    public static string SearchName => T("نام", "Name");
    public static string SearchFolder => T("پوشه", "Folder");
    public static string SearchSize => T("اندازه", "Size");
    public static string SearchDate => T("تاریخ تغییر", "Modified");
    public static string SearchAll => T("همه", "All");
    public static string SearchFiles => T("فقط فایل‌ها", "Files only");
    public static string SearchFolders => T("فقط پوشه‌ها", "Folders only");
    public static string SearchAnyKind => T("هر نوع", "Any type");
    public static string SearchDocs => T("سند", "Documents");
    public static string SearchImages => T("تصویر", "Images");
    public static string SearchVideo => T("ویدیو", "Video");
    public static string SearchAudio => T("صدا", "Audio");
    public static string SearchArchives => T("فشرده", "Archives");
    public static string SearchPrograms => T("برنامه", "Programs");
    public static string SearchCode => T("کد", "Code");
    public static string SearchOpen => T("باز کردن", "Open");
    public static string SearchOpenFolder => T("باز کردن پوشه‌ی آن", "Open its folder");
    public static string SearchCopyPath => T("کپی مسیر", "Copy path");
    public static string SearchMenu(string? key) => key is null ? Search : $"{Search}  ({key})";
    public static string SearchReading(int ready, int all) => F(Fa, $"در حال خواندن درایوها: {ready} از {all}", $"Reading drives: {ready} of {all}");
    public static string SearchIndexed(long names, int drives) => F(Fa, $"{names:N0} نام در {drives} درایو", $"{names:N0} names on {drives} drives");
    public static string SearchFound(int total, int shown, long ms) => total > shown
        ? F(Fa, $"{total:N0} یافته، {shown} تای اول نشان داده شده · {ms} میلی‌ثانیه", $"{total:N0} found, the first {shown} shown · {ms} ms")
        : F(Fa, $"{total:N0} یافته · {ms} میلی‌ثانیه", $"{total:N0} found · {ms} ms");

    public static string Alert(HealthAlert a) => a.Kind switch
    {
        HealthAlertKind.CpuOverheat => T($"دمای پردازنده زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.", $"The processor reached {Num(a.Value, "0.#")}°C under full load and stayed there for a while. Check the airflow and the cooler."),
        HealthAlertKind.GpuOverheat => T($"دمای (میانگین) کارت گرافیک زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.", $"The graphics card's (average) temperature reached {Num(a.Value, "0.#")}°C under full load and stayed there for a while. Check the airflow and the cooler."),
        HealthAlertKind.GpuHotSpotOverheat => T($"دمای نقطه‌ی داغ کارت گرافیک زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.", $"The graphics card's hot spot reached {Num(a.Value, "0.#")}°C under full load and stayed there for a while. Check the airflow and the cooler."),
        HealthAlertKind.SsdOverheat => T($"دمای SSD به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. جریان هوای اطراف آن (و هیت‌سینک) را بررسی کنید.", $"An SSD reached {Num(a.Value, "0.#")}°C and stayed there for a while. Check the airflow around it (and its heatsink)."),
        HealthAlertKind.CpuThrottle => T($"فرکانس پردازنده در بار کامل به {Num(a.Value, "0")} مگاهرتز افت کرده است. دما و محدودیت توان را بررسی کنید.", $"The processor's clock dropped to {Num(a.Value, "0")} MHz under full load. Check the temperature and the power limit."),
        _ => a.Kind.ToString()
    };

    public static string MemoryFull(double percent) => T($"حافظه‌ی رم {Num(percent, "0")}٪ پر شده است و چند دقیقه است همین‌طور مانده. سیستم کند می‌شود و ممکن است برنامه‌هایتان از کار بیفتند؛ برنامه‌های اضافه را ببندید.", $"RAM is {Num(percent, "0")}% full and has been for several minutes. The system slows down and programs may crash; close the programs you do not need.");

    public static string HotSpot => T("نقطه‌ی داغ", "Hot spot");

    public static string Overlay => T("نمایش نمودارها روی صفحه", "Show the charts on screen");
    public static string GpuProfiles => T("پروفایل کارت گرافیک", "Graphics card profile");
    public static string Fans => T("پروفایل فن", "Fan profile");
    public static string FanCustom => T("سفارشی (تنظیم دستی در برنامه)", "Custom (set by hand in the app)");
    public static string FanProfile(string name) => name switch
    {
        "auto" => T("خودکار (کنترل خود برد)", "Auto (the board's own control)"),
        "silent" => T("بی‌صدا", "Silent"), "standard" => T("استاندارد", "Standard"), "performance" => T("عملکرد", "Performance"), "full" => T("تمام سرعت", "Full speed"), _ => name
    };
    public static string FanRequested(string name) => T($"پروفایل فن «{name}» درخواست شد.", $"Fan profile \"{name}\" was requested.");
    public static string Lights => T("نورپردازی RGB", "RGB lighting");
    public static string LightsOff => T("خاموش کردن همه‌ی نورها", "Turn all lights off");
    public static string LightsApply => T("اعمال تنظیمات ذخیره‌شده", "Apply the saved settings");
    public static string LightsOn => T("نورها با تنظیمات ذخیره‌شده روشن شدند.", "The lights were turned on with the saved settings.");
    public static string LightsNoProgram => T("OpenRGB کنار مازستا پیدا نشد.", "OpenRGB was not found next to Mazesta.");
    public static string LightsNoServer => T("سرور نورپردازی بالا نیامد.", "The lighting server did not start.");
    public static string Protection => T("محافظت ویندوز", "Windows protection");
    public static string AvOff => T("خاموش کردن آنتی‌ویروس (تنظیمات امنیت ویندوز)", "Turn the antivirus off (Windows Security settings)");
    public const string ScreenOnIgpuLink = "https://www.instagram.com/reel/DJor4kyt6Hi/";
    public static string ScreenOnIgpu(string card) => T($"مانیتور به گرافیک داخلی پردازنده وصل است و {card} بیکار مانده. کابل مانیتور را از پشت کیس، از مادربرد درآورید و به خروجی‌های کارت گرافیک (پایین‌تر) بزنید. برای دیدن ویدئوی راهنما روی همین پیام بزنید.", $"The monitor is plugged into the processor's built-in graphics and {card} sits idle. Move the monitor cable from the motherboard, at the back of the case, to the graphics card's outputs (lower down). Click this message to watch the guide video.");
    public static string FirewallOff => T("خاموش کردن فایروال", "Turn the firewall off");
    public static string FirewallOn => T("روشن کردن فایروال", "Turn the firewall on");
    public static string PowerTimer => T("خواب یا خاموشی زمان‌دار", "Timed sleep or shutdown");
    public static string PowerCancel => T("لغو", "Cancel");
    public static string PowerCustom => T("زمان دلخواه…", "Custom time…");
    public static string PowerCustomHelp => T("چه مدت دیگر؟ دقیقه (مثل 90) یا ساعت:دقیقه (مثل 1:30) بنویسید؛ از یک دقیقه تا ۴۸ ساعت.", "How long from now? Type minutes (like 90) or hours:minutes (like 1:30); from one minute to 48 hours.");
    public static string PowerSet => T("تنظیم", "Set");
    public static string PowerNo => T("انصراف", "Cancel");
    public static string PowerBadWait => T("مدت درست نیست. دقیقه (مثل 90) یا ساعت:دقیقه (مثل 1:30) بنویسید، از یک دقیقه تا ۴۸ ساعت.", "That time is not valid. Type minutes (like 90) or hours:minutes (like 1:30), from one minute to 48 hours.");
    public static string PowerCancelled => T("زمان‌بند خواب یا خاموشی لغو شد.", "The sleep or shutdown timer was cancelled.");
    private static string Name(Mazesta.Core.Tray.PowerAction a) => a == Mazesta.Core.Tray.PowerAction.Sleep ? T("خواب", "Sleep") : T("خاموشی", "Shutdown");
    public static string PowerAfter(Mazesta.Core.Tray.PowerAction a) => a == Mazesta.Core.Tray.PowerAction.Sleep ? T("رفتن به حالت خواب بعد از…", "Go to sleep after…") : T("خاموش شدن بعد از…", "Shut down after…");
    public static string PowerWait(int minutes) => minutes < 60 ? T($"{minutes} دقیقه", $"{minutes} min") : minutes % 60 == 0 ? T($"{minutes / 60} ساعت", $"{minutes / 60} h") : T($"{minutes / 60}:{minutes % 60:00} ساعت", $"{minutes / 60}:{minutes % 60:00} h");
    public static string PowerPlanned(Mazesta.Core.Tray.PowerAction a, string left) => T($"{Name(a)} در {left} دیگر", $"{Name(a)} in {left}");
    public static string PowerSetNote(Mazesta.Core.Tray.PowerAction a, string wait) => T($"{Name(a)} بعد از {wait} انجام می‌شود. از منوی کنار ساعت می‌توانید لغوش کنید.", $"{Name(a)} will happen after {wait}. You can cancel it from the menu beside the clock.");
    public static string PowerSoon(Mazesta.Core.Tray.PowerAction a) => T($"{Name(a)} تا یک دقیقهٔ دیگر انجام می‌شود. کار خود را ذخیره کنید یا از منوی کنار ساعت لغو کنید.", $"{Name(a)} will happen within a minute. Save your work or cancel it from the menu beside the clock.");
    public static string PowerFailed(string why) => T($"انجام نشد: {why}", $"Failed: {why}");
    public static string Reading => T("در حال خواندن…", "Reading…");
    public static string Stock => T("حالت کارخانه (بدون تغییر)", "Factory state (unchanged)");
    public static string NoNvidia => T("کارت گرافیک NVIDIA پیدا نشد", "No NVIDIA graphics card was found");
    public static string NoProfiles => T("هنوز پروفایلی ذخیره نشده (صفحهٔ «تنظیم کارت گرافیک»)", "No profile is saved yet (the \"Graphics card tuning\" page)");
    public static string JournalFound => T("یک تنظیم خودکار کارت گرافیک در برنامه نیمه‌کاره مانده است؛ پروفایل هنگام ورود اعمال نشد. برنامه‌ی مازستا را باز کنید.", "An automatic graphics-card tuning was left unfinished in the app; the profile was not applied at sign-in. Open the Mazesta app.");
    public static string Kind(GpuProfileKind k) => k switch { GpuProfileKind.Undervolt => T("آندرولت", "Undervolt"), GpuProfileKind.Overclock => T("اورکلاک", "Overclock"), GpuProfileKind.OverclockPlus => T("اورکلاک پلاس", "Overclock Plus"), _ => T("دستی", "Manual") };
    public static string ProfileApplied(string name, string gpu) => T($"«{name}» روی {gpu} اعمال شد و هنگام ورود به ویندوز هم اعمال می‌شود.", $"\"{name}\" was applied to {gpu} and will be applied at Windows sign-in too.");
    public static string RuleApplied(string name, string gpu) => T($"پروفایل «{name}» روی {gpu} اعمال شد.", $"Profile \"{name}\" was applied to {gpu}.");
    public static string ProfileFailed(string name, string why) => T($"«{name}» اعمال نشد: {why}", $"\"{name}\" was not applied: {why}");
    public static string ProfileNotKept(string why) => T($"پروفایل اعمال شد ولی برای ورود بعدی ذخیره نشد: {why}", $"The profile was applied but not saved for the next sign-in: {why}");
    public static string ProfileMissing(string name) => T($"پروفایل «{name}» دیگر وجود ندارد؛ کارت در حالت کارخانه ماند.", $"Profile \"{name}\" no longer exists; the card stayed in its factory state.");

    public static string DriveProblem(TrayDrive d) => DriveAttention.HealthPercent(d.WearPercent) is { } p && p < DriveAttention.WatchBelow
        ? T($"دیسک {DriveName(d)}: سلامت آن به {p}٪ رسیده است ({(p < DriveAttention.FailBelow ? "خراب یا نزدیک به پایان عمر" : "رو به کاهش")}). از اطلاعات آن نسخه‌ی پشتیبان بگیرید.",
            $"Drive {DriveName(d)}: its health fell to {p}% ({(p < DriveAttention.FailBelow ? "failed or near the end of its life" : "declining")}). Back up its data.")
        : d.Status is "Warning" or "Unhealthy"
        ? T($"دیسک {DriveName(d)}: ویندوز از روی SMART آن را «{Status(d.Status)}» گزارش کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید.", $"Drive {DriveName(d)}: Windows reports it as \"{Status(d.Status)}\" from its SMART data. Back up its data.")
        : T($"دیسک {DriveName(d)}: خطای اصلاح‌نشده‌ی خواندن یا نوشتن ثبت کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید.", $"Drive {DriveName(d)}: logged an uncorrected read or write error. Back up its data.");

    /// <summary>What the daily practical check of a drive found: it cannot be written to, it fails or returns wrong data, or it is far slower than it should be.</summary>
    public static string DriveProbe(TrayDrive d, DriveProbeResult p) => p.Status switch
    {
        ProbeStatus.ReadOnly => T($"دیسک {DriveName(d)}: فقط‌خواندنی شده است و نمی‌شود روی آن نوشت. این معمولاً نشانهٔ خرابی است؛ از اطلاعات آن نسخهٔ پشتیبان بگیرید.", $"Drive {DriveName(d)}: has become read-only and cannot be written to. That is usually a sign of failure; back up its data."),
        ProbeStatus.IoError => T($"دیسک {DriveName(d)}: در آزمون نوشتن و خواندن خطا داد ({p.Detail}). از اطلاعات آن نسخهٔ پشتیبان بگیرید.", $"Drive {DriveName(d)}: failed a write and read test ({p.Detail}). Back up its data."),
        _ => T($"دیسک {DriveName(d)}: بیش از حد کند است (نوشتن {p.WriteMBps:0} و خواندن {p.ReadMBps:0} مگابایت بر ثانیه، خواندن تصادفی {p.RandomMs:0.0} میلی‌ثانیه)، با اینکه سلامتش خوب گزارش می‌شود. سیستم را کند می‌کند.",
               $"Drive {DriveName(d)}: is far too slow (write {p.WriteMBps:0} and read {p.ReadMBps:0} MB/s, random read {p.RandomMs:0.0} ms) although its health reads fine. It slows the whole system."),
    };

    public static string DriveName(TrayDrive d) => d.Letters is null ? d.Name : $"{d.Name} ({d.Letters})";

    public static string Status(string? status) => status switch
    {
        "Healthy" => T("سالم", "Healthy"), "Warning" => T("هشدار", "Warning"), "Unhealthy" => T("ناسالم", "Unhealthy"), null => NotAvailable, _ => T("نامشخص", "Unknown")
    };

    /// <summary>The verdict with the life left in brackets when the drive has a wear counter: «سالم (98%)».</summary>
    public static string DriveHealth(TrayDrive d) => Status(d.Status) + (DriveAttention.HealthPercent(d.WearPercent) is { } p ? $" ({p}%)" : "");

    public static string Tooltip(TrayCheck? temps)
    {
        if (temps is null) return Title;
        string Part(string name, double? v) => $"{name} {(v is { } x ? Num(x, "0") + "°C" : "—")}";
        var text = $"{T("مازستا", "Mazesta")} · {Part("CPU", temps.CpuTempC)} · {Part("GPU", temps.GpuTempC)}";
        return text.Length > 120 ? text[..120] : text;   // NotifyIcon.Text is limited to 127 characters
    }

    public static string Status(DateTimeOffset? last, string? problem) => problem ?? (last is null ? T("هنوز بررسی نشده", "Not checked yet") : T($"آخرین بررسی: {Time(last.Value)}", $"Last check: {Time(last.Value)}"));
    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
}
