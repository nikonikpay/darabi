using Xunit; using Mazesta.Core.Ai;
namespace Mazesta.Core.Tests;

public class AiFitTests
{
    private const long G = AiFitter.Gib;
    private static AiModel M(string id) => AiCatalog.Find(id)!;
    /// <summary>The owner's machine: RTX 3090 (24 GB, 384-bit, NVML 9751 MHz), 64 GB of RAM with 40 GB free.</summary>
    private static readonly AiMachine Rtx3090 = new("RTX 3090", 24 * G, AiFitter.GpuBandwidth(384, 9751), 64 * G, 40 * G);

    [Fact] public void The_catalog_is_pinned_and_consistent()
    {
        Assert.Equal(AiCatalog.Models.Count, AiCatalog.Models.Select(m => m.Id).Distinct().Count());
        Assert.All(AiCatalog.Models, m =>
        {
            Assert.Matches("^[0-9a-f]{64}$", m.Sha256); Assert.StartsWith("https://huggingface.co/ggml-org/", m.Url); Assert.EndsWith("/" + m.File, m.Url);
            Assert.InRange(m.ActiveBytes, 1, m.Bytes); Assert.True(m.MixtureOfExperts == m.ActiveBytes < m.Bytes * 0.5);
        });
        Assert.Matches("^[0-9a-f]{64}$", AiCatalog.Runtime.Sha256); Assert.Contains(AiCatalog.Runtime.Build, AiCatalog.Runtime.Url);
    }

    [Fact] public void Bandwidth_comes_from_bus_width_and_twice_the_nvml_memory_clock()
    {
        Assert.Equal(936.1, AiFitter.GpuBandwidth(384, 9751)!.Value / 1e9, 1);
        Assert.Null(AiFitter.GpuBandwidth(null, 9751)); Assert.Null(AiFitter.GpuBandwidth(384, 0));
    }

    [Fact] public void A_model_that_fits_the_card_runs_there_with_a_bandwidth_ceiling_and_a_context_limit()
    {
        var f = AiFitter.Fit(M("qwen3-4b"), Rtx3090);
        Assert.Equal(AiFitMode.Gpu, f.Mode); Assert.False(f.Tight); Assert.Equal(1, f.GpuShare);
        Assert.Equal(376, f.CeilingTokensPerSecond!.Value, 0);   // 936 GB/s over 2.49 GB of weights; the RTX 3090 measured 195 tok/s
        Assert.Equal(40960, f.MaxContext);   // the model's own limit, well inside the card
    }

    [Fact] public void Need_is_weights_plus_the_cache_for_4096_tokens_plus_the_overhead()
        => Assert.Equal(2497280640 + 147456L * 4096 + G / 2, AiFitter.NeedBytes(M("qwen3-4b")));

    [Fact] public void Too_big_for_the_card_splits_with_the_ram_and_has_no_ceiling()
    {
        var f = AiFitter.Fit(M("qwen3-14b"), Rtx3090 with { VramBytes = 8 * G });
        Assert.Equal(AiFitMode.Split, f.Mode); Assert.InRange(f.GpuShare, 0.7, 0.8); Assert.Null(f.CeilingTokensPerSecond); Assert.Null(f.MaxContext);
    }

    [Fact] public void Without_a_card_it_runs_on_the_cpu_if_the_free_ram_holds_it()
    {
        var noGpu = new AiMachine(null, null, null, 16 * G, 10 * G);
        Assert.Equal(AiFitMode.Cpu, AiFitter.Fit(M("qwen3-4b"), noGpu).Mode);
        Assert.Equal(AiFitMode.TooBig, AiFitter.Fit(M("gpt-oss-20b"), noGpu).Mode);
        Assert.Equal(AiFitMode.TooBig, AiFitter.Fit(M("qwen3.6-35b-a3b"), Rtx3090 with { VramBytes = 8 * G, RamAvailableBytes = 8 * G }).Mode);
    }

    [Fact] public void Little_room_left_is_flagged_tight()
        => Assert.True(AiFitter.Fit(M("qwen3.6-35b-a3b"), Rtx3090).Tight);   // 20.4 GB of weights on a 24 GB card

    [Fact] public void The_suggestion_is_the_largest_model_that_fits_the_card_with_room()
    {
        Assert.Equal("qwen3.8-27b", AiFitter.Recommend(AiCatalog.Models, Rtx3090)!.Id);
        Assert.Equal("qwen3-4b", AiFitter.Recommend(AiCatalog.Models, Rtx3090 with { VramBytes = 6 * G })!.Id);
        Assert.Equal("qwen3-4b", AiFitter.Recommend(AiCatalog.Models, new AiMachine(null, null, null, 16 * G, 10 * G))!.Id);
        Assert.Null(AiFitter.Recommend(AiCatalog.Models, new AiMachine(null, null, null, 2 * G, 1 * G)));
    }
    [Fact] public void The_assistant_is_judged_at_the_context_its_server_starts_with()
    {
        var m = M(AiAssistantPolicy.LargeModelId);
        Assert.Equal(m.KvBytesPerToken * (AiAssistantPolicy.ServerContext - AiFitter.Context), AiFitter.Fit(m, Rtx3090, AiAssistantPolicy.ServerContext).NeedBytes - AiFitter.Fit(m, Rtx3090).NeedBytes);
    }
}
