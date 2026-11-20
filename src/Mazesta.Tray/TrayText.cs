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

    // Explorer's right-click tools, each in a window of its own (Shell/LockForm, Shell/RenameForm)
    public const string LockTitle = "چه برنامه‌ای این فایل را باز نگه داشته؟";
    public const string SearchSub = "نام فایل یا پوشه را بنویسید؛ با هر حرف نتیجه تازه می‌شود";
    public const string SpaceSub = "پوشه‌ها و فایل‌های بزرگ هر درایو، از بزرگ به کوچک";
    public const string LockSub ="برنامه‌هایی که این فایل را در دست دارند و راه بستن آن‌ها";
    public const string RenameSub = "پیش‌نمایش نام‌های تازه؛ تا چیزی تکراری یا نامعتبر نباشد تغییری داده نمی‌شود";
    public const string ResizeTitle = "کوچک کردن عکس‌ها";
    public const string ResizeSub = "برای پیام‌رسان یا ایمیل: یک نسخهٔ کوچک‌تر کنار هر عکس ساخته می‌شود و اصل دست نمی‌خورد";
    public const string ResizeSize = "بلندترین ضلع";
    public const string ResizeCustom = "اندازهٔ دیگر…";
    public const string ResizeQuality = "کیفیت";
    public static readonly string[] ResizeQualities = ["بالا (حجم بیشتر)", "معمولی", "کمترین حجم"];
    public const string ResizeRun = "کوچک کن";
    public const string ResizeFile = "عکس";
    public const string ResizeResult = "نتیجه";
    public const string ResizeBadSize = "اندازه‌ای بین ۱۶ تا ۲۰۰۰۰ پیکسل وارد کنید.";
    public const string ResizeFormat = "این قالب اینجا خوانده نمی‌شود (مثلاً HEIC یا WebP).";
    public static string ResizeProgress(int i, int n) => $"عکس {i} از {n}…";
    public static string ResizeDone(int ok, int n) => $"{ok} عکس از {n} کوچک شد.";
    public const string LockPick = "بررسی‌شده";
    public const string LockNone = "هیچ برنامه‌ای این را باز نگه نداشته است.";
    public const string LockWorking = "در حال بررسی…";
    public const string LockAgain = "بررسی دوباره";
    public const string LockCloseOne = "بستن برنامه‌ی انتخاب‌شده";
    public const string LockCloseAll = "بستن همه";
    public const string LockName = "برنامه";
    public const string LockKind = "نوع";
    public const string LockPath = "مسیر";
    public const string LockState = "وضعیت";
    public const string LockCan = "قابل بستن";
    public const string LockService = "سرویس ویندوز؛ از اینجا بسته نمی‌شود";
    public const string LockSystem = "بخشی از ویندوز یا همین پایشگر است؛ از اینجا بسته نمی‌شود";
    public const string LockTruncated = "پوشه بزرگ است؛ فقط بخشی از فایل‌های آن بررسی شد.";
    public static string LockFound(int n) => $"{n} برنامه این را باز نگه داشته‌اند.";
    public static string LockFailed(string why) => $"ویندوز نتوانست بگوید چه برنامه‌ای این را باز نگه داشته ({why}). این به معنی «هیچ برنامه‌ای» نیست.";
    public static string LockAsk(string name) => $"«{name}» بسته شود؟ کارهای ذخیره‌نشده‌ی آن از بین می‌رود.";
    public static string LockAskAll(int n) => $"{n} برنامه بسته شوند؟ کارهای ذخیره‌نشده‌ی آن‌ها از بین می‌رود.";
    public static string LockCloseFailed(string name, string why) => why switch { "protected" => $"«{name}» بخشی از ویندوز یا همین پایشگر است و بسته نشد.", "gone" => $"«{name}» پیش‌تر بسته شده بود.", _ => $"«{name}» بسته نشد: {why}" };
    public static string Many(int n) => $"{n} مورد";

    public const string RenameTitle = "تغییر نام گروهی";
    public const string RenameOld = "نام فعلی";
    public const string RenameNew = "نام جدید";
    public const string RenameStatus = "وضعیت";
    public const string RenameApply = "تغییر نام";
    public const string RenameUndo = "برگرداندن آخرین تغییر";
    public const string RenameClose = "بستن";
    public const string RenameSecText = "جایگزینی متن";
    public const string RenameFind = "این متن";
    public const string RenameReplace = "جایگزین شود با";
    public const string RenameRegex = "عبارت منظم (Regex)";
    public const string RenameCase = "حساس به حروف بزرگ و کوچک";
    public const string RenameFirst = "فقط اولین مورد در هر نام";
    public const string RenameSecAdd = "افزودن و حذف";
    public const string RenamePrefix = "متن اول نام";
    public const string RenameSuffix = "متن آخر نام";
    public const string RenameCutFirst = "حذف از ابتدا (نویسه)";
    public const string RenameCutLast = "حذف از انتها (نویسه)";
    public const string RenameSecNumber = "شماره‌گذاری";
    public static readonly string[] RenameNumberModes = ["بدون شماره", "نام جدید + شماره (جایگزین کل نام)", "شماره بعد از نام فعلی", "شماره قبل از نام فعلی"];
    public const string RenameBase = "نام پایه";
    public const string RenameStart = "شروع از";
    public const string RenameStep = "گام";
    public const string RenameDigits = "تعداد رقم (۰ = خودکار)";
    public const string RenameSep = "جداکننده";
    public const string RenameSecStyle = "حروف و فاصله";
    public static readonly string[] RenameCases = ["حروف انگلیسی: بدون تغییر", "همه کوچک", "همه بزرگ", "هر کلمه با حرف بزرگ", "فقط اولین حرف بزرگ"];
    public static readonly string[] RenameSpaces = ["فاصله‌ها: بدون تغییر", "فاصله‌های اضافه حذف شود", "فاصله ← زیرخط (_)", "فاصله ← خط تیره (-)", "فاصله ← نقطه (.)"];
    public const string RenameLatin = "ارقام فارسی و عربی ← لاتین";
    public const string RenamePersian = "«ي» و «ك» عربی ← «ی» و «ک» فارسی";
    public const string RenameSecDate = "تاریخ فایل";
    public static readonly string[] RenameDates = ["بدون تاریخ", "تاریخ اول نام", "تاریخ آخر نام"];
    public const string RenameTime = "با ساعت";
    public const string RenameShamsi = "تاریخ شمسی";
    public const string RenameSecExt = "پسوند";
    public const string RenameExtLower = "پسوند با حروف کوچک";
    public const string RenameExtNew = "پسوند تازه (بدون نقطه)";
    public static readonly string[] RenameOrders = ["ترتیب: بر اساس نام", "ترتیب: قدیمی‌ترین اول", "ترتیب: جدیدترین اول", "ترتیب: کوچک‌ترین اول", "ترتیب: بزرگ‌ترین اول"];
    public const string RenameSame = "بدون تغییر";
    public const string RenameClash = "نام تکراری";
    public const string RenameBad = "نام نامعتبر";
    public static string RenameReadyN(int ready, int total) => $"{ready} از {total} فایل تغییر می‌کند.";
    public static string RenameBlockedN(int n) => $"{n} نام تکراری یا نامعتبر است؛ تا برطرف نشود تغییری داده نمی‌شود.";
    public static string RenameDone(int n) => $"نام {n} فایل تغییر کرد.";
    public static string RenameFailed(string names) => $" تغییر این‌ها انجام نشد: {names}";
    public static string RenameUndone(int n) => $"{n} فایل به نام قبلی برگشت.";
    public static string RenameBadRegex(string why) => $"عبارت منظم درست نیست: {why}";
    public const string RenameSecOrder = "ترتیب فایل‌ها";
    // the "?" beside each group: what it does, in the order the steps run, with one example each
    public const string RenameHelpText = "در نام هر فایل، «این متن» پیدا می‌شود و به‌جایش «جایگزین شود با» می‌آید؛ پسوند دست نمی‌خورد.\nمثال: این متن = IMG و جایگزین = PHOTO، نام IMG_001.jpg را PHOTO_001.jpg می‌کند. اگر «جایگزین» خالی باشد، متن پیداشده حذف می‌شود.\n«عبارت منظم» برای الگوهای پیشرفته است؛ مثلاً با الگوی «رقم‌ها» می‌توان همه‌ی عددهای نام را یک‌جا پیدا کرد. «فقط اولین مورد» از هر نام یک مورد را عوض می‌کند.";
    public const string RenameHelpAdd = "اول از ابتدا و انتهای نام (بدون پسوند) چند نویسه حذف می‌شود، بعد «متن اول» و «متن آخر» اضافه می‌شود.\nمثال: حذف از ابتدا = ۴ و متن آخر = «_final» نام IMG_001.jpg را 001_final.jpg می‌کند.";
    public const string RenameHelpNumber = "با «نام جدید + شماره» همه‌ی نام‌ها با «نام پایه» و یک شماره‌ی پشت‌سرهم عوض می‌شود: سفر 01، سفر 02، …\nحالت‌های «بعد از نام فعلی» و «قبل از نام فعلی» نام را نگه می‌دارند و شماره را کنارش می‌گذارند.\n«شروع از» اولین شماره و «گام» فاصله‌ی شماره‌هاست. «تعداد رقم» صفرهای ابتدای شماره را تعیین می‌کند (۰ = خودکار). «جداکننده» بین نام و شماره قرار می‌گیرد. شماره‌ها به ترتیب بخش «ترتیب فایل‌ها» داده می‌شوند.";
    public const string RenameHelpStyle = "حروف انگلیسی نام را کوچک یا بزرگ می‌کند و فاصله‌ها را مرتب یا با «_»، «-» یا «.» عوض می‌کند.\n«ارقام فارسی و عربی ← لاتین» ۱۲۳ را 123 می‌کند. «ي» و «ك» عربی که از متن‌های کپی‌شده می‌آیند، به «ی» و «ک» فارسی تبدیل می‌شوند.";
    public const string RenameHelpDate = "تاریخ آخرین تغییر هر فایل، به شکل 2025-03-21، به ابتدا یا انتهای نام اضافه می‌شود. «با ساعت» ساعت و دقیقه را هم می‌آورد و «تاریخ شمسی» تاریخ را شمسی می‌نویسد.";
    public const string RenameHelpExt = "«پسوند با حروف کوچک» JPG را jpg می‌کند. «پسوند تازه» پسوند همه‌ی فایل‌ها را عوض می‌کند، بدون نقطه (مثلاً png). فقط نام را عوض می‌کند و نوع فایل را تبدیل نمی‌کند.";
    public const string RenameHelpOrder = "ترتیب فایل‌ها فقط روی شماره‌گذاری اثر دارد: فایل اول در این ترتیب اولین شماره را می‌گیرد. برای عکس‌هایی که با دوربین گرفته‌اید، «قدیمی‌ترین اول» معمولاً همان ترتیب عکس‌برداری است.";
    public const string RenameSlow ="این عبارت منظم برای این نام‌ها بیش از حد کند است.";

    // each program's volume (Volume/VolumeForm)
    public const string Mixer = "صدای برنامه‌ها";
    public const string MixerDefault = "پیش‌فرض";
    public const string MixerDevice = "صدای کل این خروجی";
    public const string MixerWindows = "صداهای ویندوز";
    public const string MixerNone = "هیچ برنامه‌ای روی این خروجی صدا ندارد.";
    public const string MixerNoDevice = "هیچ خروجی صدایی پیدا نشد.";

    // the screen colour picker (Colors/ColorForm)
    public const string Color = "انتخاب رنگ از صفحه";
    public const string ColorAgain = "انتخاب دوباره";
    public const string ColorCopy = "کپی";
    public const string ColorCopied = "کپی شد";
    public const string ColorRecent = "رنگ‌های اخیر (کلیک: کپی HEX)";
    public static string ColorMenu(string? key) => key is null ? Color : $"{Color}  ({key})";

    // where the disk space went (Space/SpaceForm)
    public const string Space = "فضای دیسک کجا رفته";
    public const string SpaceFind = "جستجوی نام فایل یا پوشه…";
    public const string SpaceTree = "درخت پوشه‌ها";
    public const string SpaceBiggest = "بزرگ‌ترین فایل‌ها";
    public const string SpaceAgain = "خواندن دوباره";
    public const string SpaceFolder = "پوشه‌ای دیگر…";
    public const string SpaceShare = "سهم";
    public const string SpaceFiles = "فایل‌ها";
    public const string SpaceUnread = "خوانده نشد";
    public static string SpaceFree(string free, string total) => $"{free} آزاد از {total}";
    public static string SpaceReading(string root, int? percent) => percent is { } p ? $"در حال خواندن {root} … {p}%" : $"در حال خواندن {root} …";
    public static string SpaceWalking(string root, long files) => string.Create(CultureInfo.InvariantCulture, $"در حال خواندن {root} … {files:N0} فایل");
    public static string SpaceDone(string size, int files, double seconds, bool table) => string.Create(CultureInfo.InvariantCulture, $"{size} در {files:N0} فایل · خوانده‌شده در {seconds:0.0} ثانیه{(table ? " از جدول فایل‌های درایو" : "")}");
    public static string SpaceFound(int total, int shown) => total > shown ? string.Create(CultureInfo.InvariantCulture, $"{total:N0} مورد پیدا شد؛ {shown:N0} بزرگ‌ترین نشان داده شده‌اند") : string.Create(CultureInfo.InvariantCulture, $"{total:N0} مورد");
    public static string SpaceFailed(string why) => $"خوانده نشد: {why}";

    // the quick file search (FileSearch/SearchForm)
    public const string Search = "جستجوی فایل‌ها";
    public const string SearchTitle = "مازستا — جستجوی فایل";
    public const string SearchHint = "نام فایل یا پوشه… (مثل report یا *.mp4)";
    public const string SearchName = "نام", SearchFolder = "پوشه", SearchSize = "اندازه", SearchDate = "تاریخ تغییر";
    public const string SearchAll = "همه", SearchFiles = "فقط فایل‌ها", SearchFolders = "فقط پوشه‌ها", SearchAnyKind = "هر نوع", SearchDocs = "سند", SearchImages = "تصویر", SearchVideo = "ویدیو", SearchAudio = "صدا", SearchArchives = "فشرده", SearchPrograms = "برنامه", SearchCode = "کد";
    public const string SearchOpen = "باز کردن", SearchOpenFolder = "باز کردن پوشه‌ی آن", SearchCopyPath = "کپی مسیر";
    public static string SearchMenu(string? key) => key is null ? Search : $"{Search}  ({key})";
    public static string SearchReading(int ready, int all) => string.Create(CultureInfo.InvariantCulture, $"در حال خواندن درایوها: {ready} از {all}");
    public static string SearchIndexed(long names, int drives) => string.Create(CultureInfo.InvariantCulture, $"{names:N0} نام در {drives} درایو");
    public static string SearchFound(int total, int shown, long ms) => total > shown
        ? string.Create(CultureInfo.InvariantCulture, $"{total:N0} یافته، {shown} تای اول نشان داده شده · {ms} میلی‌ثانیه")
        : string.Create(CultureInfo.InvariantCulture, $"{total:N0} یافته · {ms} میلی‌ثانیه");

    public static string Alert(HealthAlert a) => a.Kind switch
    {
        HealthAlertKind.CpuOverheat => $"دمای پردازنده زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.GpuOverheat => $"دمای (میانگین) کارت گرافیک زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.GpuHotSpotOverheat => $"دمای نقطه‌ی داغ کارت گرافیک زیر بار کامل به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. تهویه و خنک‌کننده را بررسی کنید.",
        HealthAlertKind.SsdOverheat => $"دمای SSD به {Num(a.Value, "0.#")}°C رسیده و مدتی همان‌جا مانده است. جریان هوای اطراف آن (و هیت‌سینک) را بررسی کنید.",
        HealthAlertKind.CpuThrottle => $"فرکانس پردازنده در بار کامل به {Num(a.Value, "0")} مگاهرتز افت کرده است. دما و محدودیت توان را بررسی کنید.",
        _ => a.Kind.ToString()
    };

    public static string MemoryFull(double percent) => $"حافظه‌ی رم {Num(percent, "0")}٪ پر شده است و چند دقیقه است همین‌طور مانده. سیستم کند می‌شود و ممکن است برنامه‌هایتان از کار بیفتند؛ برنامه‌های اضافه را ببندید.";

    public const string HotSpot = "نقطه‌ی داغ";

    public const string Overlay = "نمایش نمودارها روی صفحه";
    public const string GpuProfiles = "پروفایل کارت گرافیک";
    public const string Fans = "پروفایل فن";
    public const string FanCustom = "سفارشی (تنظیم دستی در برنامه)";
    public static string FanProfile(string name) => name switch { "auto" => "خودکار (کنترل خود برد)", "silent" => "بی‌صدا", "standard" => "استاندارد", "performance" => "عملکرد", "full" => "تمام سرعت", _ => name };
    public static string FanRequested(string name) => $"پروفایل فن «{name}» درخواست شد.";
    public const string Lights = "نورپردازی RGB";
    public const string LightsOff = "خاموش کردن همه‌ی نورها";
    public const string LightsApply = "اعمال تنظیمات ذخیره‌شده";
    public const string LightsOn = "نورها با تنظیمات ذخیره‌شده روشن شدند.";
    public const string LightsNoProgram = "OpenRGB کنار مازستا پیدا نشد.";
    public const string LightsNoServer = "سرور نورپردازی بالا نیامد.";
    public const string Protection = "محافظت ویندوز";
    public const string AvOff = "خاموش کردن آنتی‌ویروس (تنظیمات امنیت ویندوز)";
    public const string ScreenOnIgpuLink = "https://www.instagram.com/reel/DJor4kyt6Hi/";
    public static string ScreenOnIgpu(string card) => $"مانیتور به گرافیک داخلی پردازنده وصل است و {card} بیکار مانده. کابل مانیتور را از پشت کیس، از مادربرد درآورید و به خروجی‌های کارت گرافیک (پایین‌تر) بزنید. برای دیدن ویدئوی راهنما روی همین پیام بزنید.";
    public const string FirewallOff = "خاموش کردن فایروال";
    public const string FirewallOn = "روشن کردن فایروال";
    public const string PowerTimer = "خواب یا خاموشی زمان‌دار";
    public const string PowerCancel = "لغو";
    public const string PowerCustom = "زمان دلخواه…";
    public const string PowerCustomHelp = "چه مدت دیگر؟ دقیقه (مثل 90) یا ساعت:دقیقه (مثل 1:30) بنویسید؛ از یک دقیقه تا ۴۸ ساعت.";
    public const string PowerSet = "تنظیم";
    public const string PowerNo = "انصراف";
    public const string PowerBadWait = "مدت درست نیست. دقیقه (مثل 90) یا ساعت:دقیقه (مثل 1:30) بنویسید، از یک دقیقه تا ۴۸ ساعت.";
    public const string PowerCancelled = "زمان‌بند خواب یا خاموشی لغو شد.";
    private static string Name(Mazesta.Core.Tray.PowerAction a) => a == Mazesta.Core.Tray.PowerAction.Sleep ? "خواب" : "خاموشی";
    public static string PowerAfter(Mazesta.Core.Tray.PowerAction a) => a == Mazesta.Core.Tray.PowerAction.Sleep ? "رفتن به حالت خواب بعد از…" : "خاموش شدن بعد از…";
    public static string PowerWait(int minutes) => minutes < 60 ? $"{minutes} دقیقه" : minutes % 60 == 0 ? $"{minutes / 60} ساعت" : $"{minutes / 60}:{minutes % 60:00} ساعت";
    public static string PowerPlanned(Mazesta.Core.Tray.PowerAction a, string left) => $"{Name(a)} در {left} دیگر";
    public static string PowerSetNote(Mazesta.Core.Tray.PowerAction a, string wait) => $"{Name(a)} بعد از {wait} انجام می‌شود. از منوی کنار ساعت می‌توانید لغوش کنید.";
    public static string PowerSoon(Mazesta.Core.Tray.PowerAction a) => $"{Name(a)} تا یک دقیقهٔ دیگر انجام می‌شود. کار خود را ذخیره کنید یا از منوی کنار ساعت لغو کنید.";
    public static string PowerFailed(string why) => $"انجام نشد: {why}";
    public const string Reading = "در حال خواندن…";
    public const string Stock = "حالت کارخانه (بدون تغییر)";
    public const string NoNvidia = "کارت گرافیک NVIDIA پیدا نشد";
    public const string NoProfiles = "هنوز پروفایلی ذخیره نشده (صفحهٔ «تنظیم کارت گرافیک»)";
    public const string JournalFound = "یک تنظیم خودکار کارت گرافیک در برنامه نیمه‌کاره مانده است؛ پروفایل هنگام ورود اعمال نشد. برنامه‌ی مازستا را باز کنید.";
    public static string Kind(GpuProfileKind k) => k switch { GpuProfileKind.Undervolt => "آندرولت", GpuProfileKind.Overclock => "اورکلاک", GpuProfileKind.OverclockPlus => "اورکلاک پلاس", _ => "دستی" };
    public static string ProfileApplied(string name, string gpu) => $"«{name}» روی {gpu} اعمال شد و هنگام ورود به ویندوز هم اعمال می‌شود.";
    public static string RuleApplied(string name, string gpu) => $"پروفایل «{name}» روی {gpu} اعمال شد.";
    public static string ProfileFailed(string name, string why) => $"«{name}» اعمال نشد: {why}";
    public static string ProfileNotKept(string why) => $"پروفایل اعمال شد ولی برای ورود بعدی ذخیره نشد: {why}";
    public static string ProfileMissing(string name) => $"پروفایل «{name}» دیگر وجود ندارد؛ کارت در حالت کارخانه ماند.";

    public static string DriveProblem(TrayDrive d) => DriveAttention.HealthPercent(d.WearPercent) is { } p && p < DriveAttention.WatchBelow
        ? $"دیسک {DriveName(d)}: سلامت آن به {p}٪ رسیده است ({(p < DriveAttention.FailBelow ? "خراب یا نزدیک به پایان عمر" : "رو به کاهش")}). از اطلاعات آن نسخه‌ی پشتیبان بگیرید."
        : d.Status is "Warning" or "Unhealthy"
        ? $"دیسک {DriveName(d)}: ویندوز از روی SMART آن را «{Status(d.Status)}» گزارش کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید."
        : $"دیسک {DriveName(d)}: خطای اصلاح‌نشده‌ی خواندن یا نوشتن ثبت کرده است. از اطلاعات آن نسخه‌ی پشتیبان بگیرید.";

    public static string DriveName(TrayDrive d) => d.Letters is null ? d.Name : $"{d.Name} ({d.Letters})";

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
