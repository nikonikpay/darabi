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
    [Fact] public void A_big_card_gets_the_14b_and_a_middle_one_the_4b()
    {
        Assert.Equal(AiAssistantPolicy.LargeModelId, Pick(24 * G).Model!.Id); Assert.Equal(AiAssistantPolicy.BaseModelId, Pick(8 * G).Model!.Id);
    }
    [Fact] public void Without_room_in_memory_nothing_is_offered() => Assert.Equal(AiAssistantStatus.NoRoom, Pick(4 * G, ramFree: 1 * G).Status);
    [Fact] public void Both_models_are_in_the_catalog_and_the_prompt_admits_it_can_not_run_tests_yet()
    {
        Assert.NotNull(AiCatalog.Find(AiAssistantPolicy.BaseModelId)); Assert.NotNull(AiCatalog.Find(AiAssistantPolicy.LargeModelId));
        Assert.Contains("not run them yet", AiAssistantPolicy.SystemPrompt);
    }
    [Fact] public void History_is_trimmed_from_the_oldest_and_keeps_at_least_the_last_message()
    {
        var h = new[] { new string('a', 3000), new string('b', 1000), new string('c', 1000) };
        Assert.Equal(["b", "c"], AiAssistantPolicy.Trim(h, x => x.Length).Select(x => x[..1]));
        Assert.Single(AiAssistantPolicy.Trim(new[] { new string('z', 9000) }, x => x.Length));
    }
}
