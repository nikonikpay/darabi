using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

/// <summary>Everyday questions a user types to the assistant (written as a service-shop customer would), and what the app must do with each.
/// The words themselves are in <c>Ai/ai-lexicon.json</c>.</summary>
public class AppGuideLexiconTests
{
    private static AiRoute R(string t) => AppGuide.Route(t);
    [Theory]
    [InlineData("راهنما", "help"), InlineData("آموزش برنامه", "help"), InlineData("پیام ها", "messages"), InlineData("سرعت دانلود", "benchmarks"), InlineData("پیام جدید دارم؟", "messages"),
     InlineData("زبان برنامه رو انگلیسی کن", "settings"), InlineData("ولتاژ گرافیک رو کم کن", "tuning"), InlineData("پلن پرفورمنس رو فعال کن", "play"),
     InlineData("پاکسازی ویندوز", "tools"), InlineData("اینترنت وصل نیست", "tools"), InlineData("یه برنامه رو بلاک کن", "shield"), InlineData("تله متری ویندوز رو خاموش کن", "tweaks"),
     InlineData("ویندوز آپدیت رو خاموش کن", "updates"), InlineData("برنامه رو آپدیت کن", "appupdate"), InlineData("رنگ رم ها رو قرمز کن", "lights"), InlineData("نور سیستم رو خاموش کن", "lights"),
     InlineData("فن رو روی سایلنت بذار", "fans"), InlineData("حالت بازی رو روشن کن", "play"), InlineData("ایکس ام پی رو فعال کنم", "tuning"), InlineData("تاریخچه دما", "monitoring"),
     InlineData("خروجی pdf از مشخصات بگیر", "system"), InlineData("مشخصات سیستم رو pdf کن", "system")]
    public void An_everyday_request_shows_the_place_where_it_is_done(string text, string page) { var r = R(text); Assert.True(r.Intent == AiIntent.Navigate && r.Place?.Page == page, $"{text}: {r.Intent} {r.Place?.Page}"); }

    [Theory, InlineData("ویندوزم کنده چیکار کنم"), InlineData("کامپیوترم کند شده"), InlineData("سیستمم هنگ میکنه"), InlineData("چرا کامپیوترم خودش ری استارت میشه"), InlineData("چرا بازی ها لگ دارن"), InlineData("فن لپ تاپم خیلی سر و صدا میکنه")]
    public void A_complaint_is_answered_by_the_app_never_as_a_spec_or_a_page(string text) { var r = R(text); Assert.True(r.Intent == AiIntent.Complaint, $"{text}: {r.Intent} {r.Place?.Page}/{r.Part}"); }

    [Theory, InlineData("cpu چند درجه است", "cpu"), InlineData("گرم میشه سیستمم", null), InlineData("لپ تاپم خیلی داغ میکنه", null), InlineData("چقدر برق مصرف میکنه", null)]
    public void A_question_of_heat_or_power_reads_the_sensors(string text, string? part) { var r = R(text); Assert.True(r.Intent == AiIntent.Sensors && r.Part == part, $"{text}: {r.Intent} {r.Part}"); }

    [Theory, InlineData("کیس گیمینگ پیشنهاد بده"), InlineData("یه کامپیوتر بخرم برای رندرینگ"), InlineData("قیمت چنده"), InlineData("شماره تماس مازستا"), InlineData("سلام"), InlineData("مرسی"), InlineData("تو کی هستی؟")]
    public void The_shop_and_small_talk_are_the_models(string text) => Assert.Equal(AiIntent.None, R(text).Intent);

    [Fact] public void A_state_question_is_not_a_how_to_and_the_highest_temperature_ever_is_the_reports()
    {
        Assert.True(R("سلامت هاردم چطوره").Intent == AiIntent.Tests, "سلامت هاردم چطوره: " + R("سلامت هاردم چطوره").Intent);
        Assert.True(R("چطوری سلامت رممو بررسی کنم؟").Intent == AiIntent.HowTo, "چطوری سلامت رممو بررسی کنم؟: " + R("چطوری سلامت رممو بررسی کنم؟").Intent);
        Assert.True(R("بالاترین دما چقدر بود").Intent == AiIntent.Report, "بالاترین دما چقدر بود: " + R("بالاترین دما چقدر بود").Intent);
        Assert.True(R("دمای cpu الان چنده").Intent == AiIntent.Sensors, "دمای cpu الان چنده: " + R("دمای cpu الان چنده").Intent);
        Assert.True(R("بنچمارک گرافیک").Intent == AiIntent.Benchmarks, "بنچمارک گرافیک: " + R("بنچمارک گرافیک").Intent);
        Assert.True(R("تست پایداری سیستم").Intent == AiIntent.Checkup, "تست پایداری سیستم: " + R("تست پایداری سیستم").Intent);
    }
    [Fact] public void A_battery_question_shows_the_battery_check_and_a_shop_question_makes_the_model_call_its_tools()
    {
        var r = R("باتری چند درصده"); Assert.True(r.Intent == AiIntent.Navigate && r.Place?.Target == "battery", r.Intent.ToString());
        Assert.All(new[] { "مازستا چیه", "شماره تماس مازستا", "یه کامپیوتر بخرم برای رندرینگ", "قیمت چنده" }, t => Assert.True(AiAssistantPolicy.AsksToAct(t), t));
        Assert.False(AiAssistantPolicy.AsksToAct("سلام"));
        Assert.True(R("آی پی من چنده").Place?.Page == "network" && R("آی پی من چنده").Intent == AiIntent.Navigate);
    }
    [Fact] public void A_drive_test_asked_for_by_name_runs_alone()
    {
        var r = R("تست نوشتن طولانی هارد رو انجام بده");
        Assert.Equal(AiIntent.Tests, r.Intent); Assert.Equal(["storage.sustained"], r.TestIds); Assert.DoesNotContain("storage", r.Areas ?? []);
        Assert.Null(R("تست هارد رو انجام بده").TestIds);
    }
    [Fact] public void Every_place_has_words_in_the_lexicon_and_no_word_is_blank()
    {
        Assert.All(AppGuide.Places, p => Assert.All(p.Words, w => Assert.False(string.IsNullOrWhiteSpace(w))));
    }
}
