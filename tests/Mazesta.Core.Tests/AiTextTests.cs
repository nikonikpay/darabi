using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

public class AiTextTests
{
    private const string Block = "**انتخاب کنید:**\n- میکروفن\n- صفحه نمایش\n- کیبورد\n- صدا\n- سایر بخش‌ها\n\nلطفاً مشخص کنید کدام بخش را تست می‌خواهید.\n\n---\n\n";

    [Fact] public void A_block_written_three_times_is_a_loop_and_twice_is_not()
    {
        Assert.False(AiText.Looping("سلام. " + Block + Block));
        Assert.True(AiText.Looping("سلام. " + Block + Block + Block));
        Assert.False(AiText.Looping(new string('a', 100)));
    }
    [Fact] public void The_copies_after_the_first_are_cut()
    {
        string kept = AiText.Unloop("برای تست میکروفن صفحهٔ بررسی‌ها را باز کردم.\n\n" + Block + Block + Block + "**انتخاب کنید:**\n- میکروفن");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(kept, "لطفاً مشخص کنید"));
        Assert.StartsWith("برای تست میکروفن", kept);
    }
    [Fact] public void A_reply_without_repeats_is_kept_as_it_is() => Assert.Equal("یک.\n\nدو.", AiText.Unloop("یک.\n\nدو."));
    [Fact] public void A_reply_ends_at_a_marker_the_model_wrote_out()
    {
        Assert.Equal("پایشگر فعال شد.", AiText.CutAtMarker("پایشگر فعال شد. </s> <tray><cpu>75.3 °C</cpu></tray>"));
        Assert.Equal("ok", AiText.CutAtMarker("ok"));
        Assert.Equal(-1, AiText.MarkerAt("a < b and c > d"));
    }
}
