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
}
