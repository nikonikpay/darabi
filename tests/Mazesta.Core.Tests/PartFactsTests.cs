using Mazesta.Core.Inventory; using Xunit;
namespace Mazesta.Core.Tests;

public class PartFactsTests
{
    [Fact] public void Nvidia_units_follow_from_the_core_count_and_compute_capability()
    {
        var u = GpuFacts.For("NVIDIA GeForce RTX 3090", "8.6", 10496)!;   // 82 SMs
        Assert.Equal((82, 328, 82, 328, 112, "Samsung 8 nm"), (u.ComputeUnits, u.Tmus, u.RtCores, u.AiCores, u.Rops, u.Process));
        var b5 = GpuFacts.For("NVIDIA GeForce RTX 5070", "12.0", 6144)!;
        Assert.Equal((48, 192, 48, 64), (b5.ComputeUnits, b5.AiCores, b5.RtCores, b5.Rops));
    }
    [Fact] public void A_gtx_16_card_has_no_rt_or_tensor_cores_and_an_odd_core_count_gets_nothing()
    {
        var g = GpuFacts.For("NVIDIA GeForce GTX 1660 SUPER", "7.5", 1408)!;
        Assert.Null(g.RtCores); Assert.Null(g.AiCores);
        Assert.Null(GpuFacts.For("NVIDIA GeForce RTX 3090", "8.6", 10000));
    }
    [Fact] public void Amd_and_intel_come_from_the_table_by_exact_name_and_a_laptop_part_never_matches()
    {
        Assert.Equal((6144, 384, 192, 96), GpuFacts.For("AMD Radeon RX 7900 XTX", null, null) is { } x ? (x.Shaders!.Value, x.Tmus!.Value, x.Rops!.Value, x.RtCores!.Value) : default);
        Assert.Equal(4096, GpuFacts.For("Intel(R) Arc(TM) A770 Graphics", null, null)!.Shaders);
        Assert.Null(GpuFacts.For("AMD Radeon RX 7600M XT", null, null));
        Assert.Null(GpuFacts.For("NVIDIA GeForce RTX 4070 Laptop GPU", null, null));
    }
    [Fact] public void Cpu_platforms_match_listed_desktop_models_only()
    {
        Assert.Equal("PCIe 4.0, 24 lanes", CpuFacts.For("AMD Ryzen 9 3950X 16-Core Processor")!.Pcie);
        Assert.Equal("Raptor Lake", CpuFacts.For("13th Gen Intel(R) Core(TM) i9-13900K")!.Microarchitecture);
        Assert.Equal("Arrow Lake-S", CpuFacts.For("Intel(R) Core(TM) Ultra 7 265K")!.Microarchitecture);
        Assert.Null(CpuFacts.For("AMD Ryzen 7 5700G with Radeon Graphics"));
        Assert.Null(CpuFacts.For("12th Gen Intel(R) Core(TM) i7-12700H"));
        Assert.Null(CpuFacts.For("Intel(R) Core(TM) i7-1185G7"));
    }
}
