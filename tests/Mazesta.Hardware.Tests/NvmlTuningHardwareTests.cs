using Mazesta.Hardware.Nvidia; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Hardware.Tests;

/// <summary>Read-only: opens the NVIDIA card through NVML and reads what the tuning page shows. Nothing is applied - writing needs
/// administrator rights and changes the card, so it is verified by hand (docs/VERIFICATION-slice9-gpu-tuning.md).</summary>
[Trait("Category", "Hardware")]
public class NvmlTuningHardwareTests
{
    [Fact] public void The_nvidia_card_reports_offset_ranges_limits_and_live_readings()
    {
        var provider = new NvmlTuningProvider(NullLogger.Instance);
        var card = Assert.Single(provider.Devices, d => d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("GPU-", card.Id);
        Assert.True(card.Limits.HasCoreOffset); Assert.True(card.Limits.HasMemoryOffset); Assert.True(card.Limits.HasPowerLimit);
        var t = card.ReadTelemetry();
        Assert.True(t.CoreClockMHz > 0); Assert.True(t.TemperatureC > 0); Assert.True(t.PowerW > 0);
        Assert.NotNull(card.ReadCurrent());
    }
}
