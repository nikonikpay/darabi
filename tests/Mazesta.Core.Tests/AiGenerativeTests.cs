using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

public class AiGenerativeTests
{
    private const long G = AiFitter.Gib;
    private static AiMachine Pc(string? gpu, long? vram) => new(gpu, vram, null, 64 * G, 40 * G);
    private static AiGenModel M(string id) => AiGenCatalog.Models.Single(m => m.Id == id);

    [Fact] public void Every_kind_has_models_and_every_model_needs_less_for_the_reduced_path()
    {
        foreach (var k in Enum.GetValues<AiMedia>()) Assert.Contains(AiGenCatalog.Models, m => m.Media == k);
        Assert.All(AiGenCatalog.Models, m => { Assert.True(m.MinVram <= m.FullVram, m.Id); Assert.False(string.IsNullOrWhiteSpace(m.Source), m.Id); });
        Assert.Equal(AiGenCatalog.Models.Count, AiGenCatalog.Models.Select(m => m.Id).Distinct().Count());
    }
    [Fact] public void Without_a_card_nothing_runs() => Assert.All(AiGenCatalog.Models, m => Assert.Equal(new AiGenVerdict(AiGenFit.No, AiGenBlock.NoGpu), AiGenCatalog.Judge(m, Pc(null, null))));
    [Fact] public void A_cuda_only_model_does_not_run_on_amd_whatever_its_memory()
    {
        Assert.Equal(AiGenBlock.NeedsNvidia, AiGenCatalog.Judge(M("trellis.2-4b"), Pc("AMD Radeon RX 7900 XTX", 24 * G)).Block);
        Assert.Equal(AiGenFit.Full, AiGenCatalog.Judge(M("trellis.2-4b"), Pc("NVIDIA GeForce RTX 4090", 24 * G)).Fit);
    }
    [Fact] public void A_card_that_reports_a_little_under_its_size_gets_its_full_path()
        => Assert.Equal(AiGenFit.Full, AiGenCatalog.Judge(M("trellis.2-4b"), Pc("NVIDIA GeForce RTX 3090", 24 * G - 200L * 1048576)).Fit);
    [Fact] public void Between_the_least_and_the_full_figure_it_runs_reduced_and_below_it_not()
    {
        Assert.Equal(AiGenFit.Reduced, AiGenCatalog.Judge(M("qwen-image-2.1"), Pc("NVIDIA GeForce RTX 4060", 8 * G)).Fit);
        Assert.Equal(new AiGenVerdict(AiGenFit.No, AiGenBlock.LittleVram), AiGenCatalog.Judge(M("ltx-2.5"), Pc("NVIDIA GeForce RTX 4060", 8 * G)));
    }
    [Fact] public void The_suggestion_is_the_first_at_full_quality_else_the_first_that_runs_else_none()
    {
        Assert.Equal("minimax-h3", AiGenCatalog.Suggest(AiMedia.Video, Pc("NVIDIA GeForce RTX 4090", 24 * G))!.Id);
        Assert.Equal("flux.2-klein-4b", AiGenCatalog.Suggest(AiMedia.Image, Pc("NVIDIA GeForce RTX 4070 Ti SUPER", 16 * G))!.Id);
        Assert.Equal("qwen-image-2.1", AiGenCatalog.Suggest(AiMedia.Image, Pc("NVIDIA GeForce RTX 3050", 6 * G))!.Id);
        Assert.Null(AiGenCatalog.Suggest(AiMedia.Mesh, Pc("AMD Radeon RX 7800 XT", 16 * G)));
    }
}
