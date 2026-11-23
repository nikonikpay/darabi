using Mazesta.Core.Rgb; using Xunit;
namespace Mazesta.Core.Tests;

public class RgbScanTests
{
    [Fact] public void A_full_find_misses_nothing() => Assert.Empty(RgbScan.Missing(["Motherboard", "Memory", "Graphics"], ["NVIDIA GeForce RTX 4070"]));
    [Fact] public void A_card_of_its_own_with_no_lights_found_is_listed() => Assert.Equal(["Graphics"], RgbScan.Missing(["Motherboard", "Memory"], ["NVIDIA GeForce RTX 4070"]));
    [Fact] public void Built_in_graphics_alone_is_not_expected_among_the_lights() => Assert.Empty(RgbScan.Missing(["Motherboard", "Memory"], ["AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics 770"]));
    [Fact] public void Nothing_found_lists_the_board_and_the_memory() => Assert.Equal(["Motherboard", "Memory"], RgbScan.Missing([], []));
}
