using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

public class AiAssistantTests
{
    private const long G = AiFitter.Gib;
    private static AiAssistantChoice Pick(long? vram, long ramFree = 40 * G) => AiAssistantPolicy.Decide(new("GPU", vram, null, 64 * G, ramFree));

    [Fact] public void No_gpu_or_under_4_gb_is_not_offered_the_assistant()
    {
        Assert.Equal(AiAssistantStatus.NoGpu, Pick(null).Status); Assert.Equal(AiAssistantStatus.NoGpu, Pick(0).Status);
        Assert.Equal(AiAssistantStatus.LittleVram, Pick(2 * G).Status); Assert.Equal(AiAssistantStatus.LittleVram, Pick(3 * G).Status);
        Assert.Null(Pick(2 * G).Model);
    }
    [Fact] public void A_4_gb_card_gets_the_4b_model_even_when_it_reports_a_little_under_4_gib()
    {
        var c = Pick(4095L * 1048576); Assert.Equal(AiAssistantStatus.Available, c.Status); Assert.Equal(AiAssistantPolicy.BaseModelId, c.Model!.Id);
    }
    [Fact] public void Every_card_is_offered_the_same_small_model()
    {
        // One small model for every card: a big card is not given a 9 GB download by default (a larger model can be picked).
        Assert.Equal(AiAssistantPolicy.BaseModelId, Pick(24 * G).Model!.Id); Assert.Equal(AiAssistantPolicy.BaseModelId, Pick(8 * G).Model!.Id);
    }
    [Fact] public void Without_room_in_memory_nothing_is_offered() => Assert.Equal(AiAssistantStatus.NoRoom, Pick(4 * G, ramFree: 1 * G).Status);
    [Fact] public void Both_models_are_in_the_catalog_and_the_prompt_says_a_test_that_did_not_pass_is_not_a_pass()
    {
        Assert.NotNull(AiCatalog.Find(AiAssistantPolicy.BaseModelId)); Assert.NotNull(AiCatalog.Find(AiAssistantPolicy.LargeModelId));
        Assert.Contains("did not pass", AiAssistantPolicy.SystemPrompt); Assert.Contains("run_tests", AiAssistantPolicy.SystemPrompt); Assert.Contains("run_benchmark", AiAssistantPolicy.SystemPrompt);
    }
    [Fact] public void History_is_trimmed_from_the_oldest_and_keeps_at_least_the_last_message()
    {
        var h = new[] { new string('a', 3000), new string('b', 1000), new string('c', 1000) };
        Assert.Equal(["b", "c"], AiAssistantPolicy.Trim(h, x => x.Length).Select(x => x[..1]));
        Assert.Single(AiAssistantPolicy.Trim(new[] { new string('z', 9000) }, x => x.Length));
    }
    [Fact] public void A_request_to_test_open_or_switch_is_an_action_and_a_question_is_not()
    {
        foreach (var x in new[] { "رم سیستم رو چک کن", "گرافیک رو تست کن", "cpu رو چک کن", "قسمت اورلی رو نشون بده", "اورلی رو فعال کن", "صفحه‌ی گزارش‌ها رو باز کن", "Run a benchmark", "open the reports" })
            Assert.True(AiAssistantPolicy.AsksToAct(x), x);
        foreach (var x in new[] { "دمای پردازنده چنده؟", "این سیستم چه کارت گرافیکی داره؟", "how hot is the cpu?" })
            Assert.False(AiAssistantPolicy.AsksToAct(x), x);
    }
    [Fact] public void The_prompt_names_the_real_site_and_forbids_prices() { Assert.Contains("dfmrendering.com", AiAssistantPolicy.SystemPrompt); Assert.Contains("never give a price", AiAssistantPolicy.SystemPrompt); Assert.Contains("company_info", AiAssistantPolicy.SystemPrompt); }
    [Fact] public void Company_facts_carry_the_real_phones_and_no_price() { var json = System.Text.Json.JsonSerializer.Serialize(MazestaCompany.Info); Assert.Contains("09197588700", json); Assert.Contains("021-41139", json); Assert.Single(MazestaCompany.Systems, s => s.Name.StartsWith("AM9")); Assert.Contains("no price", System.Text.Json.JsonSerializer.Serialize(MazestaCompany.SystemsAnswer(null))); }
    [Fact] public void The_prompt_says_every_request_is_a_new_run() { Assert.Contains("Each request is a new run", AiAssistantPolicy.SystemPrompt); Assert.Contains("open_page", AiAssistantPolicy.SystemPrompt); }
}
