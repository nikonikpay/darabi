using Xunit; using Mazesta.Core.Software;
namespace Mazesta.Core.Tests;

public class SoftwareCatalogTests
{
    private const long G = 1L << 30;
    private static SoftMachine Pc(string? gpu, double? vram, double ram, int cores = 8) => new("CPU", cores, cores * 2, (long)(ram * G), gpu, vram is { } v ? (long)(v * G) : null);
    private static SoftApp A(string id) => SoftwareCatalog.Find(id)!;

    [Fact] public void Every_program_has_tiers_in_order_a_source_and_unique_names()
    {
        Assert.All(SoftwareCatalog.Apps, a =>
        {
            Assert.NotEmpty(a.Tiers); Assert.False(string.IsNullOrWhiteSpace(a.Source), a.Id);
            Assert.Equal(a.Tiers.Select(t => t.Kind).Order(), a.Tiers.Select(t => t.Kind));
        });
        Assert.Equal(SoftwareCatalog.Apps.Count, SoftwareCatalog.Apps.Select(a => a.Id).Distinct().Count());
        foreach (var c in Enum.GetValues<SoftCategory>()) Assert.Contains(SoftwareCatalog.Apps, a => a.Category == c);
    }
    [Fact] public void Ray_tracing_is_read_from_the_card_family()
    {
        Assert.True(SoftwareCatalog.RayTracing("NVIDIA GeForce RTX 3090")); Assert.True(SoftwareCatalog.RayTracing("AMD Radeon RX 6700 XT"));
        Assert.True(SoftwareCatalog.RayTracing("Intel(R) Arc(TM) A770 Graphics")); Assert.True(SoftwareCatalog.RayTracing("NVIDIA RTX A4000"));
        Assert.False(SoftwareCatalog.RayTracing("NVIDIA GeForce GTX 1080 Ti")); Assert.False(SoftwareCatalog.RayTracing("AMD Radeon RX 580"));
        Assert.False(SoftwareCatalog.RayTracing("AMD Radeon RX 5700 XT")); Assert.False(SoftwareCatalog.RayTracing("Intel(R) UHD Graphics 770"));
    }
    [Fact] public void Vantage_needs_a_card_that_traces_rays()
    {
        Assert.Null(SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce GTX 1080 Ti", 11, 32)).Level);
        Assert.Contains(SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce GTX 1080 Ti", 11, 32)).Missing, m => m.What == "RayTracing");
        Assert.Equal(SoftTierKind.Minimum, SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce RTX 3060", 12, 16)).Level);
    }
    [Fact] public void The_level_is_the_highest_tier_met_in_order_and_the_next_says_what_is_missing()
    {
        // A 3090 with 64 GB ("63.9" as Windows reads it) is Lumion's high-end tier.
        Assert.Equal(SoftTierKind.HighEnd, SoftwareCatalog.Judge(A("lumion"), Pc("NVIDIA GeForce RTX 3090", 23.9, 63.9)).Level);
        // An 8 GB card with 32 GB of RAM: minimum, and the recommended tier wants 10 GB of graphics memory.
        var v = SoftwareCatalog.Judge(A("lumion"), Pc("NVIDIA GeForce RTX 4060", 8, 32));
        Assert.Equal(SoftTierKind.Minimum, v.Level); Assert.Equal(SoftTierKind.Recommended, v.Next);
        Assert.Equal([new SoftShort("Vram", 10, 8)], v.Missing);
    }
    [Fact] public void A_card_without_its_own_memory_does_not_run_a_program_that_needs_one()
    {
        var v = SoftwareCatalog.Judge(A("enscape"), Pc("Intel(R) UHD Graphics 770", null, 32));
        Assert.Null(v.Level); Assert.Contains(v.Missing, m => m.What == "Dedicated");
        Assert.Equal(SoftTierKind.Recommended, SoftwareCatalog.Judge(A("corona"), Pc("Intel(R) UHD Graphics 770", null, 32)).Level);
    }
    [Fact] public void V_ray_gpu_takes_nvidia_and_amd_cards_and_ties_ram_to_the_card()
    {
        Assert.Contains(SoftwareCatalog.Judge(A("vraygpu"), Pc("Intel(R) UHD Graphics 770", null, 64)).Missing, m => m.What == "Dedicated");
        Assert.Equal(SoftTierKind.Recommended, SoftwareCatalog.Judge(A("vraygpu"), Pc("AMD Radeon RX 7900 XTX", 24, 64)).Level);
        var v = SoftwareCatalog.Judge(A("vraygpu"), Pc("NVIDIA GeForce RTX 3090", 24, 32));
        Assert.Equal(SoftTierKind.Minimum, v.Level); Assert.Equal([new SoftShort("RamVsVram", 48, 32)], v.Missing);
    }
    [Fact] public void Cores_count_only_where_the_publisher_names_them()
        => Assert.Contains(SoftwareCatalog.Judge(A("blender"), Pc("NVIDIA GeForce RTX 3060", 12, 32, cores: 6)).Missing, m => m.What == "Cores");
    [Fact] public void What_the_card_and_the_processor_say_of_themselves_decides_and_what_could_not_be_read_is_said()
    {
        // Plenty of RAM is no help to a processor without AVX2; the card's own DXR answer outranks its name.
        Assert.Null(SoftwareCatalog.Judge(A("vray"), Pc(null, null, 64) with { Avx2 = false }).Level);
        Assert.Equal(SoftTierKind.Minimum, SoftwareCatalog.Judge(A("vray"), Pc(null, null, 64) with { Avx2 = true }).Level);
        Assert.Null(SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce RTX 3090", 24, 64) with { Dxr = false }).Level);
        // Cores not read, an example card to compare with: the level stands for what was checked, and these are named beside it.
        var v = SoftwareCatalog.Judge(A("unreal"), Pc("NVIDIA GeForce RTX 3090", 24, 64) with { Cores = null, Dx12 = true });
        Assert.Equal(SoftTierKind.Recommended, v.Level); Assert.Contains("Cores", v.Unchecked!); Assert.Contains("CpuSpeed", v.Unchecked!); Assert.DoesNotContain("Api", v.Unchecked!);
        Assert.Contains("GpuSpeed", SoftwareCatalog.Judge(A("lumion"), Pc("NVIDIA GeForce RTX 3090", 24, 64)).Unchecked!);
        // Below the minimum, what could not be checked is still said: the shortfall does not make the unread parts read.
        Assert.Contains("GpuSpeed", SoftwareCatalog.Judge(A("lumion"), Pc("Intel(R) UHD Graphics 770", null, 64)).Unchecked!);
    }
    [Fact] public void A_program_with_one_set_of_requirements_is_met_and_one_without_a_minimum_is_below_its_recommendation()
    {
        Assert.Equal("Meets", SoftwareCatalog.LevelName(A("rhino"), SoftwareCatalog.Judge(A("rhino"), Pc("NVIDIA GeForce RTX 3090", 24, 64))));
        Assert.Equal("BelowRec", SoftwareCatalog.LevelName(A("rhino"), SoftwareCatalog.Judge(A("rhino"), Pc("NVIDIA GeForce GTX 1050", 2, 16))));
        Assert.Equal("Below", SoftwareCatalog.LevelName(A("vantage"), SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce GTX 1080", 8, 64))));
        Assert.Equal("Minimum", SoftwareCatalog.LevelName(A("lumion"), SoftwareCatalog.Judge(A("lumion"), Pc("NVIDIA GeForce RTX 4060", 8, 32))));
    }
    [Fact] public void The_publishers_hard_conditions_decide_whatever_the_memory()
    {
        Assert.Null(SoftwareCatalog.Judge(A("corona"), Pc(null, null, 128) with { Sse42 = false }).Level);
        Assert.Null(SoftwareCatalog.Judge(A("aftereffects"), Pc("NVIDIA GeForce RTX 3090", 24, 64) with { Avx2 = false }).Level);
        Assert.Contains("Api", SoftwareCatalog.Judge(A("photoshop"), Pc("NVIDIA GeForce RTX 3090", 24, 64) with { Dx12 = true, Avx2 = true, Sse42 = true }).Unchecked!);
        // Chaos: system RAM at least the card's memory.
        Assert.Contains(SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce RTX 3090", 24, 16)).Missing, m => m.What == "RamVsVram");
        // D5 names the GTX 1650 as unsupported, whatever its memory; a GTX 1060 gets DXR from its driver, so only Direct3D's answer counts.
        Assert.Contains(SoftwareCatalog.Judge(A("d5"), Pc("NVIDIA GeForce GTX 1650", 4, 32)).Missing, m => m.What == "Unsupported");
        Assert.Equal(SoftTierKind.Minimum, SoftwareCatalog.Judge(A("d5"), Pc("NVIDIA GeForce GTX 1060 6GB", 6, 16) with { Dxr = true }).Level);
        Assert.Contains("Api", SoftwareCatalog.Judge(A("d5"), Pc("NVIDIA GeForce GTX 1060 6GB", 6, 16)).Unchecked!);
        // A 1 GiB card does not pass a 1.5 GB requirement.
        Assert.Contains(SoftwareCatalog.Judge(A("photoshop"), Pc("AMD Radeon HD 7750", 1, 16)).Missing, m => m.What == "Vram");
    }
    [Fact] public void Archicads_minimum_is_its_own_and_its_recommendations_are_by_building_size()
    {
        var v = SoftwareCatalog.Judge(A("archicad"), Pc("NVIDIA GeForce GTX 1050", 2, 8));
        Assert.Equal(SoftTierKind.Minimum, v.Level); Assert.Equal("Soft_Tier_Archicad_Entry", v.NextTier!.LabelKey);
        var mid = SoftwareCatalog.Judge(A("archicad"), Pc("NVIDIA GeForce RTX 3060", 12, 32));
        Assert.Equal(("Soft_Tier_Archicad_Mid", "Soft_Tier_Archicad_High"), (mid.Reached!.LabelKey, mid.NextTier!.LabelKey));
        Assert.Equal(SoftTierKind.HighEnd, SoftwareCatalog.Find("twinmotion")!.Tiers[1].Kind);
    }
}
