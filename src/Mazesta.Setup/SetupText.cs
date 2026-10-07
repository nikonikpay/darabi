namespace Mazesta.Setup;

/// <summary>The installer's words in Persian and English. Persian is the default; the setup's language button switches the whole window.</summary>
internal static class SetupText
{
    public static bool English;
    private static string T(string fa, string en) => English ? en : fa;
    public static string Title => T("نصب مازستا تست", "Install Mazesta Test");
    public static string Language => T("English", "فارسی");
    public static string StepLicense => T("قرارداد", "Agreement");
    public static string StepOptions => T("نصب", "Install");
    public static string StepFinish => T("پایان", "Finish");
    public static string LicenseHead => T("قرارداد استفاده از برنامه", "License agreement");
    public static string LicenseLede => T("لطفاً قرارداد را بخوانید. برای ادامه باید آن را بپذیرید.", "Please read the agreement. You must accept it to continue.");
    public static string Accept => T("قرارداد را خوانده‌ام و می‌پذیرم", "I have read the agreement and accept it");
    public static string OptionsHead => T("محل نصب", "Where to install");
    public static string Lede => T("مازستا تست دما، بار و سلامت قطعات سیستم را زنده نشان می‌دهد و آزمون و بنچمارک دارد. برنامه برای همه‌ی کاربران این کامپیوتر در پوشه‌ی زیر نصب می‌شود؛ تنظیمات و گزارش‌های هر کاربر در پروفایل خودش می‌ماند.",
        "Mazesta Test shows your PC's temperatures, load and health live and has tests and benchmarks. It is installed for every user of this computer in the folder below; each user's settings and reports stay in their own profile.");
    public static string Folder => T("پوشه‌ی نصب", "Install folder");
    public static string Browse => T("انتخاب…", "Browse…");
    public static string Desktop => T("میان‌بر روی دسکتاپ بساز", "Create a desktop shortcut");
    public static string Launch => T("بعد از نصب برنامه را باز کن", "Open the program when done");
    public static string Install => T("نصب", "Install");
    public static string Update => T("به‌روزرسانی", "Update");
    public static string Next => T("بعدی", "Next");
    public static string Back => T("قبلی", "Back");
    public static string Close => T("بستن", "Close");
    public static string Finish => T("پایان", "Finish");
    public static string Cancel => T("انصراف", "Cancel");
    public static string UpdateNote => T("نسخه‌ای از قبل نصب شده است؛ فایل‌های برنامه جایگزین می‌شود و اطلاعات شما می‌ماند.",
        "A copy is already installed: its files are replaced and your data is kept.");
    public static string MovedNote => T("نسخه‌ی قبلیِ نصب‌شده برای یک کاربر بود. اطلاعاتش به پروفایل شما منتقل و نسخه‌ی قبلی حذف می‌شود.",
        "The earlier copy was installed for one user. Its data is moved to your profile and that copy is removed.");
    public static string BadFolder => T("پوشه‌ی نصب درست نیست (مسیر کامل یک پوشه را بدهید، نه ریشه‌ی یک درایو).", "That is not a usable folder (give the full path of a folder, not a drive's root).");
    public static string NotEmptyAsk => T("این پوشه خالی نیست و برنامه‌ی مازستا در آن نصب نشده.\n\nاگر ادامه بدهید فایل‌های برنامه داخل آن کپی می‌شود و فایل‌های هم‌نام جایگزین می‌شوند؛ بقیه‌ی فایل‌های پوشه دست‌نخورده می‌ماند.\n\nدر همین پوشه نصب شود؟",
        "This folder is not empty and Mazesta is not installed in it.\n\nIf you continue, the program's files are copied into it and files with the same name are replaced; everything else in the folder is left as it is.\n\nInstall into this folder?");
    public static string Running => T("مازستا تست در حال اجراست. آن را ببندید (نوار کنار ساعت هم) و دوباره نصب را بزنید.", "Mazesta Test is running. Close it (also in the tray) and install again.");
    public static string NoPayload => T("این فایل نصب ناقص است (برنامه داخلش نیست).", "This installer is incomplete (the program is not inside it).");
    public static string Preparing => T("آماده‌سازی…", "Preparing…");
    public static string Copying => T("در حال کپی فایل‌ها…", "Copying files…");
    public static string Moving => T("انتقال اطلاعات نسخه‌ی قبلی…", "Moving the earlier copy's data…");
    public static string Shortcuts => T("ساخت میان‌بر…", "Creating shortcuts…");
    public static string Registering => T("ثبت در برنامه‌های نصب‌شده‌ی ویندوز…", "Registering in Installed apps…");
    public static string Done => T("نصب انجام شد.", "Installation is complete.");
    public static string Failed(string why) => T("نصب انجام نشد: ", "Installation failed: ") + why;
    public static string Admin => T("برنامه هنگام باز شدن اجازه‌ی مدیر ویندوز می‌خواهد (برای خواندن سنسورها)؛ این طبیعی است.", "The program asks for administrator permission when it opens (to read the sensors); that is normal.");
    public static string Wait => T("نصب در حال انجام است؛ لطفاً صبر کنید.", "Installing; please wait.");

    public static string License => English ? LicenseEn : LicenseFa;

    private const string LicenseFa =
@"قرارداد استفاده از نرم‌افزار «مازستا تست» (Mazesta Test)
نسخه‌ی قرارداد: مهر ۱۴۰۵

این قرارداد بین شما (کاربر) و مازستا (ارائه‌دهنده‌ی نرم‌افزار) است. با نصب یا استفاده از برنامه، شما این قرارداد را می‌پذیرید. اگر نمی‌پذیرید، برنامه را نصب نکنید.

۱. مجوز استفاده
مازستا به شما یک مجوز غیرانحصاری، غیرقابل واگذاری و قابل فسخ می‌دهد تا برنامه را روی کامپیوترهایی که مالک آنها هستید یا مجاز به سرویس‌دادن به آنها هستید نصب و استفاده کنید. مالکیت برنامه به شما منتقل نمی‌شود و فقط حق استفاده می‌گیرید.

۲. هزینه‌ی استفاده
در حال حاضر استفاده از برنامه رایگان است. اما ممکن است در هر زمان، استفاده از کل برنامه یا بخش‌هایی از آن مشمول هزینه شود. در این صورت این موضوع، همراه با شرایط آن، پیش از اعمال در خود برنامه مشخص خواهد شد و تا زمانی که شما آن را در برنامه نپذیرفته‌اید از شما هزینه‌ای دریافت نمی‌شود. رایگان بودن فعلی، تعهدی برای رایگان ماندن در آینده نیست.

۳. محدودیت‌ها
شما حق ندارید: (الف) برنامه را مهندسی معکوس، کدشکنی یا دست‌کاری کنید، مگر تا اندازه‌ای که قانون صراحتاً اجازه می‌دهد؛ (ب) اعلام مالکیت و نام برنامه را حذف یا تغییر دهید؛ (پ) نسخه‌ی تغییریافته‌ی برنامه را پخش کنید یا نسخه‌ی اصلی را بدون اجازه‌ی مکتوب مازستا به نام خود یا برای فروش پخش کنید؛ (ت) محدودیت‌های مجوز یا هزینه‌ی برنامه را دور بزنید؛ (ث) از برنامه برای کار غیرقانونی یا آسیب‌زدن به سیستم دیگران استفاده کنید.

۴. دسترسی مدیر و درایور
برنامه برای خواندن سنسورها به دسترسی مدیر ویندوز نیاز دارد و درایور PawnIO را نصب می‌کند. برنامه‌ی OpenRGB هم برای کنترل نور همراه آن است. این دو مجموعه‌ی شخص ثالث هستند و تابع مجوز خودشان‌اند (فهرست در فایل THIRD-PARTY-NOTICES برنامه). با حذف مازستا تست، این دو هم حذف می‌شوند.

۵. آزمون‌های سنگین و تنظیمات سخت‌افزار
آزمون‌ها و بنچمارک‌ها پردازنده، کارت گرافیک، رم و دیسک را زیر بار شدید می‌گذارند و بخش‌های کنترل فن، نور و اورکلاک می‌توانند تنظیمات سخت‌افزار را تغییر دهند. این کارها اگر سیستم خنک‌کاری ضعیف یا سخت‌افزار معیوب داشته باشد، می‌تواند باعث داغ‌شدن، ناپایداری، قطع‌شدن سیستم، از دست رفتن اطلاعات یا آسیب سخت‌افزار و از بین رفتن گارانتی شود. مسئولیت استفاده از این بخش‌ها با شماست. پیش از آزمون سنگین، کارهای مهم خود را ذخیره و از اطلاعاتتان نسخه‌ی پشتیبان تهیه کنید.

۶. اطلاعات و حریم خصوصی
گزارش‌ها، تنظیمات و تاریخچه روی کامپیوتر خودتان (در پروفایل کاربر) ذخیره می‌شود. بخش‌هایی که به اینترنت وصل می‌شوند (بررسی و دریافت نسخه‌ی تازه، فهرست مقایسه‌ی بنچمارک، ارسال گزارش به سایت سرویس‌دهنده و دستیار هوشمند) فقط همان‌طور که در خود برنامه توضیح داده شده کار می‌کنند و گزارش را فقط وقتی می‌فرستند که شما یا مرکز سرویس شما این کار را انجام دهید. اطلاعات شما بدون اجازه‌ی خودتان به شخص دیگری فروخته نمی‌شود.

۷. به‌روزرسانی
برنامه ممکن است نسخه‌ی تازه را بررسی و دریافت کند. نسخه‌های تازه ممکن است ویژگی‌ها را تغییر دهند، اضافه یا حذف کنند.

۸. مالکیت
برنامه، نام، نشان و ظاهر آن متعلق به مازستا است و با قوانین حقوق مالکیت فکری محافظت می‌شود. اجزای متن‌باز و شخص ثالث متعلق به صاحبان خودشان است.

۹. سلب ضمانت
برنامه «همان‌طور که هست» و «به شرط موجود بودن» ارائه می‌شود. مازستا ضمانت نمی‌کند که برنامه بدون خطا کار کند، همه‌ی سخت‌افزارها را درست بخواند یا نتیجه‌ی آزمون‌ها برای هر منظوری کافی باشد. مقدارها و نتیجه‌ها راهنما هستند، نه گواهی رسمی سلامت قطعه.

۱۰. محدودیت مسئولیت
تا آنجا که قانون اجازه می‌دهد، مازستا در برابر خسارت‌های غیرمستقیم، تبعی یا اتفاقی، از جمله از دست رفتن اطلاعات، سود یا کار، و آسیب به سخت‌افزار ناشی از استفاده یا ناتوانی در استفاده از برنامه مسئول نیست. مجموع مسئولیت مازستا در هر حال، از مبلغی که بابت برنامه پرداخته‌اید بیشتر نیست (و چون اکنون رایگان است، صفر است).

۱۱. فسخ
اگر این قرارداد را نقض کنید، مجوز شما خودبه‌خود پایان می‌یابد. هر زمان می‌توانید با حذف برنامه از «Installed apps» ویندوز استفاده را پایان دهید. بندهای مالکیت، سلب ضمانت و محدودیت مسئولیت بعد از پایان قرارداد هم معتبر می‌مانند.

۱۲. تغییر قرارداد
مازستا می‌تواند این قرارداد را تغییر دهد. نسخه‌ی تازه از زمانی که در برنامه یا هنگام نصب به شما نشان داده می‌شود و می‌پذیرید معتبر است.

۱۳. قانون حاکم
این قرارداد تابع قوانین جمهوری اسلامی ایران است و اختلافات در مراجع صالح قضایی همان کشور بررسی می‌شود، مگر اینکه قانون الزامی کشور محل زندگی شما چیز دیگری بگوید.

۱۴. ارتباط
وب‌سایت: www.dfmrendering.com";

    private const string LicenseEn =
@"Mazesta Test - Software License Agreement
Agreement version: October 2026

This agreement is between you (the user) and Mazesta (the provider of the software). By installing or using the program you accept this agreement. If you do not accept it, do not install the program.

1. License
Mazesta grants you a non-exclusive, non-transferable, revocable license to install and use the program on computers that you own or are authorised to service. You do not receive ownership of the program, only the right to use it.

2. Price
At present the program is free to use. However, at any time the use of the whole program or of parts of it may become subject to a fee. If that happens, it will be made clear inside the program, with its terms, before it applies, and you will not be charged until you have accepted it in the program. Being free today is not a promise that it stays free.

3. Restrictions
You may not: (a) reverse engineer, decompile or tamper with the program, except to the extent the law expressly allows; (b) remove or change its ownership notices and name; (c) distribute a modified version of the program, or distribute the original under your own name or for sale, without Mazesta's written permission; (d) circumvent any licence or payment limit of the program; (e) use the program for anything unlawful or to harm other people's systems.

4. Administrator rights and drivers
To read sensors the program needs Windows administrator rights and installs the PawnIO driver. The OpenRGB program, used to control lighting, is shipped with it. Both are third-party components under their own licenses (listed in the program's THIRD-PARTY-NOTICES). When Mazesta Test is uninstalled, both are removed with it.

5. Stress tests and hardware settings
Tests and benchmarks put the processor, graphics card, memory and drives under heavy load, and the fan, lighting and overclocking parts can change hardware settings. With poor cooling or faulty hardware this can cause overheating, instability, shut-downs, loss of data, hardware damage and the loss of a warranty. You use these parts at your own risk. Save your work and back up your data before a heavy test.

6. Data and privacy
Reports, settings and history are stored on your own computer (in the user's profile). The parts that connect to the internet (checking for and downloading updates, benchmark comparison lists, sending a report to the service shop's site, and the smart assistant) work only as described in the program, and send a report only when you or your service shop do so. Your information is not sold to anyone without your permission.

7. Updates
The program may check for and download new versions. New versions may change, add or remove features.

8. Ownership
The program, its name, logo and look belong to Mazesta and are protected by intellectual property law. Open-source and third-party components belong to their owners.

9. Disclaimer of warranty
The program is provided ""as is"" and ""as available"". Mazesta does not warrant that it will be error-free, that it will read every piece of hardware correctly, or that test results are enough for any purpose. Values and results are guidance, not an official certificate of a part's health.

10. Limitation of liability
To the extent the law allows, Mazesta is not liable for indirect, consequential or incidental damages, including loss of data, profit or work, and damage to hardware, arising from the use of or inability to use the program. In any case Mazesta's total liability is limited to the amount you paid for the program (which, as it is free now, is zero).

11. Termination
If you breach this agreement, your license ends automatically. You may stop at any time by uninstalling the program from Windows' Installed apps. The sections on ownership, disclaimer of warranty and limitation of liability continue after the agreement ends.

12. Changes
Mazesta may change this agreement. A new version applies from when it is shown to you in the program or at installation and you accept it.

13. Governing law
This agreement is governed by the laws of the Islamic Republic of Iran, and disputes go to that country's competent courts, unless a mandatory law of your country of residence says otherwise.

14. Contact
Website: www.dfmrendering.com";
}
