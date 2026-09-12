// tests/Mazesta.Core.Tests/IdentifierTests.cs
using Mazesta.Core.Hardware;
using Xunit;
namespace Mazesta.Core.Tests;
public class IdentifierTests
{
    [Fact] public void ProviderPath_is_normalised_to_kind_slash_token()
        => Assert.Equal("gpu/nvidiagpu-0", HardwareId.FromProviderPath(HardwareKind.Gpu, "/nvidiagpu/0").Value);
    [Fact] public void SubHardware_path_keeps_all_segments()
        => Assert.Equal("motherboard/lpc-nct6687d-0", HardwareId.FromProviderPath(HardwareKind.Motherboard, "/lpc/nct6687d/0").Value);
    [Fact] public void Storage_id_uses_trimmed_serial_with_spaces_replaced()
        => Assert.Equal("storage/S6Z2NJ0T_123", HardwareId.ForStorage("  S6Z2NJ0T 123 ").Value);
    [Fact] public void SensorId_composes_and_exposes_hardware()
    {
        var hw = HardwareId.FromProviderPath(HardwareKind.Gpu, "/nvidiagpu/0");
        var s = SensorId.Create(hw, "temperature/2");
        Assert.Equal("gpu/nvidiagpu-0#temperature/2", s.Value);
        Assert.Equal(hw, s.Hardware);
    }
}
