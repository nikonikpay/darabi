using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

/// <summary>The messages here are the ones the assistant got wrong in the owner's chats (2026-09-30), and the questions it has to answer.</summary>
public class AppGuideTests
{
    private static AiRoute R(string text) => AppGuide.Route(text);
    private static void Goes(string text, string page, string? target = null)
    {
        var r = R(text);
        Assert.True(r.Intent == AiIntent.Navigate, $"{text}: {r.Intent}");
        Assert.Equal(page, r.Place!.Page); Assert.Equal(target, r.Place.Target);
    }

    [Fact] public void Diagnose_the_system_runs_the_smart_diagnosis_itself_while_a_bare_name_only_opens_its_page()
    {
        foreach (var t in new[] { "سیستم رو عیب یابی کن", "عیب‌یابی هوشمند انجام بده", "چکاپ بگیر", "سیستمم رو چک کن", "کامپیوتر رو بررسی کن", "عیب یابی هوشمند رو اجرا کن", "diagnose my system", "run the smart diagnosis" })
            Assert.True(R(t).Intent == AiIntent.Checkup, $"{t}: {R(t).Intent}");
        Goes("عیب یابی هوشمند", "checkup"); Goes("برو به صفحه عیب یابی", "checkup"); Goes("صفحه عیب یابی رو باز کن", "checkup");
        foreach (var t in new[] { "چطوری سیستم رو عیب یابی کنم", "سیستم رو عیب یابی نکن", "اینترنت رو عیب یابی کن", "رم رو تست کن", "مشخصات سیستم رو نشون بده" })
            Assert.NotEqual(AiIntent.Checkup, R(t).Intent);
    }
    [Fact] public void Overclock_and_undervolt_are_the_tuning_page_not_the_overlay()
    {
        Goes("برو به بخش اورکلاک گرافیک", "tuning"); Goes("برو بخش اورکلاک", "tuning"); Goes("برو به بخش آندرولت", "tuning");
        Goes("صفحه آندرولت باز بشه", "tuning"); Goes("برو صفحه اندرولت", "tuning"); Goes("صفحه اورکلاک باز کن", "tuning"); Goes("گرافیکو اندرولت کن", "tuning");
        Goes("برو به بخش اورلی", "overlay");
    }
    [Fact] public void A_control_is_pointed_at_on_its_page()
    {
        Goes("اندرولت خودکار گرافیک رو انجام بده", "tuning", "autoundervolt"); Goes("دکمه اندرولت خودکار گرافیک رو بزن", "tuning", "autoundervolt");
        Goes("اورکلاک خودکار رو انجام بده", "tuning", "autooverclock"); Goes("منحنی ولتاژ رو نشون بده", "tuning", "curve");
        Goes("برو بخش dns", "tools", "dns");
    }
    [Fact] public void A_hands_on_check_is_the_checks_page_not_a_hardware_test()
    {
        Goes("تست میکروفن بگیر", "checks", "mic"); Goes("برو بخش تست کیبورد", "checks", "keys"); Goes("تست ماوس", "checks", "mouse");
    }
    [Fact] public void Pages_are_found_by_the_names_people_use()
    {
        Goes("برو بخش بنچمارک", "benchmarks"); Goes("برو به صفحه سنسورها", "monitoring"); Goes("صفحه تنظیمات ویندوز رو باز کن", "tweaks");
        Goes("صفحه هوش مصنوعی رو باز کن", "ai"); Goes("برو تنظیمات", "settings"); Goes("صفحه گزارش ها رو باز کن", "reports");
    }
    [Fact] public void Reports_are_summarised_or_made_into_a_file()
    {
        Assert.Equal(AiIntent.Report, R("گزارش هارو خلاصه کن برام").Intent);
        Assert.Equal(AiIntent.Report, R("تو گزارش ها ببین بالاترین دمای گرافیکم چقدر بوده؟").Intent);
        Assert.Equal(AiIntent.Report, R("نتیجه تست سیستم رو بهم بگو").Intent);
        Assert.Equal(("pdf", AiIntent.ReportFile), (R("گزارش رو pdf بده").Format, R("گزارش رو pdf بده").Intent));
        Assert.Equal("summary", R("خلاصه گزارش رو PDF کن").Format);
        Assert.Equal("html", R("گزارش آخر رو html بده").Format);
    }
    [Fact] public void A_question_about_this_computer_reads_the_part_it_names()
    {
        Assert.Equal(("ram", AiIntent.Specs), (R("رم سیستم چقدره؟").Part, R("رم سیستم چقدره؟").Intent));
        Assert.Equal("cpu", R("مدل سی پی یو چیه").Part);
        Assert.Equal("vram", R("رم گرافیکم چقدره؟").Part);
        Assert.Equal("gpu", R("کارت گرافیکم چیه؟").Part);
    }
    [Fact] public void A_program_is_checked_against_this_computer()
    {
        var r = R("روی سیستم من برنامه vantage اجرا میشه؟");
        Assert.Equal(AiIntent.Software, r.Intent); Assert.Equal("vantage", r.App!.Id);
        Assert.Equal("lumion", R("لومیون رو میکشه؟").App!.Id);
        Assert.Equal("3dsmax", R("تری دی مکس روی این لپ تاپ اجرا میشه").App!.Id);
        var list = R("کدوم برنامه های رندرینگ رو میتونم اجرا کنم؟");
        Assert.Equal(AiIntent.SoftwareList, list.Intent); Assert.Equal(Software.SoftCategory.Visualization, list.Category);
    }
    [Fact] public void Tests_of_parts_are_runs_and_how_questions_are_answered_not_acted_on()
    {
        Assert.Equal(["cpu"], R("تست cpu بگیر").Areas!);
        Assert.Equal(["memory"], R("رم رو تست کن").Areas!);
        Assert.Equal(AiIntent.HowTo, R("چطوری سلامت رممو بررسی کنم؟").Intent);
        Assert.Equal(AiIntent.None, R("سلام، خوبی؟").Intent);
    }
    [Fact] public void Spelling_variants_fold_into_one()
    {
        Assert.Equal(AppGuide.Normalize("آندرولت"), AppGuide.Normalize("اندرولت"));
        Assert.Equal(AppGuide.Normalize("كيبورد"), AppGuide.Normalize("کیبورد"));
        Assert.Equal(" گزارش ها ", AppGuide.Normalize("گزارش‌ها"));
        Assert.Equal(" 3 ", AppGuide.Normalize("۳"));
    }
    [Fact] public void Every_place_has_a_name_and_the_page_list_names_every_page()
    {
        Assert.All(AppGuide.Places, p => Assert.NotEmpty(p.Words));
        string list = AppGuide.PageList(k => k);
        foreach (var page in AppGuide.Places.Select(p => p.Page).Distinct()) Assert.Contains(page + " = ", list);
    }
    [Fact] public void The_overlay_is_switched_and_a_reading_now_is_the_sensors()
    {
        Assert.Equal((AiIntent.Overlay, true), (R("میخوام نمایش دما ها بالای صفحه بیاد").Intent, R("میخوام نمایش دما ها بالای صفحه بیاد").On));
        Assert.Equal((AiIntent.Overlay, false), (R("اورلی رو خاموش کن").Intent, R("اورلی رو خاموش کن").On));
        Goes("صفحه اورلی رو نشون بده", "overlay");
        Assert.Equal(("Temperature", AiIntent.Sensors), (R("دمای cpu الان چنده؟").Kind, R("دمای cpu الان چنده؟").Intent));
        Assert.Equal("Fan", R("دور فن ها چقدره").Kind);
        Assert.Equal("cpu", R("دمای cpu الان چنده؟").Part);
        Assert.NotEqual(AiIntent.Specs, R("کامپیوترم کند شده چیکار کنم؟").Intent);
        Assert.Equal("all", R("مشخصات سیستمم رو بگو").Part);
        Assert.Equal(AiIntent.Dns, R("بهترین dns رو برام پیدا کن").Intent);
        Assert.Equal(AiIntent.Games, R("سیستم من برای گیم مناسبه؟").Intent);
        Goes("برو بخش بازی", "tools", "gamemode");
    }
    [Fact] public void A_command_said_not_to_be_done_starts_nothing()
    {
        Assert.Equal((AiIntent.TestsInfo, "cpu"), (R("تست CPU را اجرا نکن، فقط توضیح بده").Intent, R("تست CPU را اجرا نکن، فقط توضیح بده").Areas![0]));
        Assert.NotEqual(AiIntent.Tests, R("تست رم نمیخوام").Intent);
        Assert.False(AiAssistantPolicy.AsksToAct("تست cpu رو اجرا نکن"));
        Assert.True(AiAssistantPolicy.AsksToAct("تست cpu بگیر"));
    }
    [Fact] public void A_test_request_says_whether_all_of_them_together_and_for_how_long()
    {
        var all = R("تست گرافیک رو همشو انجام بده");
        Assert.Equal((AiIntent.Tests, true, false, null), (all.Intent, all.All, all.Together, all.Minutes));
        Assert.Equal(["gpu"], all.Areas);
        var both = R("تست cpu و گرافیک رو همزمان ۱۰ دقیقه بگیر");
        Assert.Equal((AiIntent.Tests, false, true, 10), (both.Intent, both.All, both.Together, both.Minutes));
        Assert.False(R("تست رم بگیر").All);
    }
    [Fact] public void Not_active_turns_the_overlay_off()
    {
        Assert.Equal((AiIntent.Overlay, false), (R("اورلی رو غیر فعال کن").Intent, R("اورلی رو غیر فعال کن").On));
        Assert.Equal((AiIntent.Overlay, false), (R("اورلی رو غیرفعال کن").Intent, R("اورلی رو غیرفعال کن").On));
        Assert.Equal((AiIntent.Overlay, true), (R("اورلی رو فعال کن").Intent, R("اورلی رو فعال کن").On));
    }
    [Fact] public void A_reports_temperature_is_of_the_part_named_and_the_earlier_report_is_found()
    {
        var r = R("تو گزارش ها ببین بالاترین دمای گرافیکم چقدر بوده؟");
        Assert.Equal(("Temperature", "gpu"), (r.Kind, r.Part));
        Assert.Null(R("بالاترین دما توی گزارش چند بود").Part);
        Assert.Equal(1, R("گزارش قبلی رو خلاصه کن").Index); Assert.Equal(0, R("گزارش هارو خلاصه کن").Index);
        Assert.Equal((AiIntent.ReportFile, 1), (R("گزارش قبلی رو pdf بده").Intent, R("گزارش قبلی رو pdf بده").Index));
    }
    // From the owner's chats: each of these went wrong (the overlay for the tray, a made-up "Unlock-Item", the programs list for "what can you do").
    [Fact] public void The_tray_is_not_the_overlay()
    {
        Assert.Equal((AiIntent.Tray, true), (R("پایشگر tray رو فعال کن").Intent, R("پایشگر tray رو فعال کن").On));
        Assert.Equal((AiIntent.Tray, true), (R("قابلیت ترای رو فعال کن").Intent, R("قابلیت ترای رو فعال کن").On));
        Assert.Equal((AiIntent.Tray, true), (R("پایشگر رو فعال کن").Intent, R("پایشگر رو فعال کن").On));
        Assert.Equal((AiIntent.Tray, false), (R("tray رو غیر فعال کن").Intent, R("tray رو غیر فعال کن").On));
        Assert.Equal(AiIntent.Overlay, R("اورلی رو روشن کن").Intent);
        Assert.NotEqual(AiIntent.Tray, R("سریع ترین dns رو پیدا کن").Intent);
    }
    [Fact] public void A_warning_at_a_temperature_is_the_trays()
    {
        var r = R("میتونی وقتی دمای cpu بالای 80 درجه شد بهم گزارش بدی؟");
        Assert.Equal((AiIntent.Alert, "cpu", 80), (r.Intent, r.Part, r.Value));
        Assert.Equal((AiIntent.Alert, "gpu", 85), (R("اگه دمای گرافیک از ۸۵ گذشت خبرم کن").Intent, R("اگه دمای گرافیک از ۸۵ گذشت خبرم کن").Part, R("اگه دمای گرافیک از ۸۵ گذشت خبرم کن").Value));
        Assert.Null(R("هر وقت دما بالا رفت هشدار بده").Value);
    }
    [Fact] public void What_the_assistant_can_do_is_the_apps_answer()
    {
        Assert.Equal(AiIntent.Help, R("تو چه کارهایی میتونی انجام بدی؟").Intent);
        Assert.Equal(AiIntent.Help, R("چه کارهای دیگه ای میتونی انجام بدی به غیر از کارهایی که برای همین برنامه هست. ؟ کلا چه سوالاتی میتونم ازت بپرسم؟").Intent);
        Assert.Equal(AiIntent.SoftwareList, R("چه برنامه هایی روی سیستمم اجرا میشه؟").Intent);
    }
    [Fact] public void Windows_windows_open_and_commands_come_from_the_checked_list()
    {
        Assert.Equal((AiIntent.WinOpen, "thispc"), (R("میتونی mycomputer رو باز کنی؟").Intent, R("میتونی mycomputer رو باز کنی؟").Ids?[0]));
        Assert.Equal("devmgr", R("دیوایس منیجر رو باز کن").Ids?[0]);
        Assert.Equal("diskmgmt", R("مدیریت دیسک").Ids?[0]);
        Assert.Equal(AiIntent.Navigate, R("صفحه تنظیمات رو باز کن").Intent);
        var lockQ = R("درایو های من عکس قفل دارن یه دستور بده که اون درایو ها رو بردارم.");
        Assert.Equal(AiIntent.WinCommand, lockQ.Intent); Assert.Equal(["bitlockerstatus", "bitlockeroff"], lockQ.Ids);
        Assert.Equal(["flushdns"], R("دستور پاک کردن dns cache چیه").Ids);
        Assert.Equal(AiIntent.WinCommandUnknown, R("یه دستور بده که ویندوز رو سریعتر کنه").Intent);
    }
    [Fact] public void A_driver_is_not_a_drive()
    {
        Assert.Equal(AiIntent.Drivers, R("درایورهای سیستمم آپدیت لازم دارن؟").Intent);
        Assert.Equal(AiIntent.Drivers, R("درایور کارت گرافیکم جدیده؟").Intent);
        Assert.Equal("drivers", R("صفحه درایورها رو باز کن").Place?.Page);
        Assert.Equal(AiIntent.Specs, R("درایوهام چند گیگ هستن؟").Intent);
    }
    [Fact] public void A_command_the_model_made_up_is_found_and_a_listed_one_is_not()
    {
        Assert.Equal(["manage-bde -on D:"], WindowsActions.UncheckedIn("برای رمزگذاری از `manage-bde -on D:` استفاده کنید."));
        Assert.Empty(WindowsActions.UncheckedIn("اجرا کنید: `manage-bde -off E:` و بعد `manage-bde -status`"));
        Assert.Equal(["Unlock-Item -Path D:"], WindowsActions.UncheckedIn("```\nUnlock-Item -Path D:\n```"));
        Assert.Empty(WindowsActions.UncheckedIn("کارت `RTX` و فایل `hiberfil.sys`"));
    }
    [Fact] public void A_benchmarks_number_is_not_the_parts_specification()
    {
        Assert.NotEqual(AiIntent.Specs, R("آخرین بنچمارک پردازنده چند بود و نسبت به قبل کندتر شده؟").Intent);
        Assert.Equal(AiIntent.Specs, R("پردازنده من چند هسته داره؟").Intent);
    }
    [Theory, InlineData("خطاهای pcie کارت گرافیکم چنده"), InlineData("are there pcie errors on my gpu")]
    public void Pcie_errors_are_read_from_the_driver(string text) => Assert.Equal(AiIntent.PcieErrors, R(text).Intent);
}
