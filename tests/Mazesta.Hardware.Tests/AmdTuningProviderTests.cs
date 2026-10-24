using Mazesta.Core.Tuning; using Mazesta.Hardware.Amd; using Mazesta.Hardware.Wmi; using Xunit;
namespace Mazesta.Hardware.Tests;

public sealed class AmdTuningProviderTests
{
    private sealed class Fake(params (string Name, string Maker, string Pnp)[] cards) : IWmiQuery
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql) =>
            [.. cards.Select(c => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Name"] = c.Name, ["AdapterCompatibility"] = c.Maker, ["DriverVersion"] = "32.0.1", ["PNPDeviceID"] = c.Pnp })];
    }
    private sealed class Nvidia(string? reason) : IGpuTuningProvider
    {
        public IReadOnlyList<IGpuTuningDevice> Devices => [];
        public string? UnavailableReasonKey => reason; public string? UnavailableDetail => null;
    }
    private static readonly (string, string, string) Radeon = ("AMD Radeon RX 7800 XT", "Advanced Micro Devices, Inc.", @"PCI\VEN_1002&DEV_747E");

    [Fact] public void A_radeon_with_adrenalin_is_named() { var p = new AmdTuningProvider(new Fake(Radeon), () => true); Assert.Equal("Tuning_Unavailable_Amd", p.UnavailableReasonKey); Assert.Contains("RX 7800 XT", p.UnavailableDetail); }
    [Fact] public void A_radeon_without_adrenalin_says_to_install_it() => Assert.Equal("Tuning_Unavailable_AmdNoDriver", new AmdTuningProvider(new Fake(Radeon), () => false).UnavailableReasonKey);
    [Fact] public void An_nvidia_only_pc_is_not_an_amd_one() { var p = new AmdTuningProvider(new Fake(("RTX 3090", "NVIDIA", @"PCI\VEN_10DE&DEV_2204")), () => true); Assert.False(p.HasAmd); Assert.Null(p.UnavailableReasonKey); }
    [Fact] public void The_page_gets_the_amd_reason_when_nvidia_has_no_card() => Assert.Equal("Tuning_Unavailable_Amd", new CombinedTuningProvider(new Nvidia("Tuning_Unavailable_NoNvidia"), new AmdTuningProvider(new Fake(Radeon), () => true)).UnavailableReasonKey);
    [Fact] public void With_no_amd_card_the_nvidia_reason_stays() => Assert.Equal("Tuning_Unavailable_NoNvidia", new CombinedTuningProvider(new Nvidia("Tuning_Unavailable_NoNvidia"), new AmdTuningProvider(new Fake(), () => true)).UnavailableReasonKey);
}
