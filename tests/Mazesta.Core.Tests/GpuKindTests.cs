using Mazesta.Core.Hardware;
using Xunit;
namespace Mazesta.Core.Tests;

public class GpuKindTests
{
    [Theory]
    [InlineData("AMD Radeon(TM) Graphics", true)] [InlineData("AMD Radeon 780M Graphics", true)] [InlineData("AMD Radeon(TM) Vega 8 Graphics", true)] [InlineData("AMD Radeon 8060S Graphics", true)]
    [InlineData("Intel(R) UHD Graphics 770", true)] [InlineData("Intel(R) Iris(R) Xe Graphics", true)] [InlineData("Intel(R) Arc(TM) Graphics", true)] [InlineData("Intel(R) Arc(TM) 140V GPU (16GB)", true)]
    [InlineData("AMD Radeon RX 7800 XT", false)] [InlineData("AMD Radeon PRO W7600", false)] [InlineData("Intel(R) Arc(TM) A770 Graphics", false)] [InlineData("Intel(R) Arc(TM) B580 Graphics", false)]
    [InlineData("NVIDIA GeForce RTX 4050 Laptop GPU", false)]
    public void Integrated_graphics_are_told_from_cards_by_name(string name, bool integrated) => Assert.Equal(integrated, GpuKind.Integrated(name, false));

    [Fact] public void Unified_memory_is_integrated_whatever_the_name() => Assert.True(GpuKind.Integrated("Some GPU", true));
}
