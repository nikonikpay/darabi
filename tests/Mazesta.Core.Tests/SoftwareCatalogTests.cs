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
    [Fact] public void V_ray_gpu_wants_nvidia_and_its_rtx_mode_an_rtx_card()
    {
        Assert.Contains(SoftwareCatalog.Judge(A("vraygpu"), Pc("AMD Radeon RX 7900 XTX", 24, 64)).Missing, m => m.What == "Nvidia");
        Assert.Equal(SoftTierKind.Minimum, SoftwareCatalog.Judge(A("vraygpu"), Pc("NVIDIA GeForce GTX 1080", 8, 32)).Level);
        Assert.Equal(SoftTierKind.Recommended, SoftwareCatalog.Judge(A("vraygpu"), Pc("NVIDIA GeForce RTX 2070", 8, 32)).Level);
    }
    [Fact] public void Cores_count_only_where_the_publisher_names_them()
        => Assert.Contains(SoftwareCatalog.Judge(A("blender"), Pc("NVIDIA GeForce RTX 3060", 12, 32, cores: 6)).Missing, m => m.What == "Cores");
    [Fact] public void A_program_with_one_set_of_requirements_is_met_not_at_its_minimum()
    {
        Assert.Equal("Meets", SoftwareCatalog.LevelName(A("vantage"), SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce RTX 3090", 24, 64))));
        Assert.Equal("Below", SoftwareCatalog.LevelName(A("vantage"), SoftwareCatalog.Judge(A("vantage"), Pc("NVIDIA GeForce GTX 1080", 8, 64))));
        Assert.Equal("Minimum", SoftwareCatalog.LevelName(A("lumion"), SoftwareCatalog.Judge(A("lumion"), Pc("NVIDIA GeForce RTX 4060", 8, 32))));
    }
}
