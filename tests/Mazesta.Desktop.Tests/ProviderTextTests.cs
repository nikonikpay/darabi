using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Xunit;
namespace Mazesta.Desktop.Tests;

public class ProviderTextTests
{
    [Fact] public void A_ready_provider_says_how_many_sensors_it_reads() => Assert.Contains("412", ProviderText.Describe(new(ProviderState.Ready, 412, null, null)));

    [Fact] public void A_failed_provider_gives_its_reason_and_the_detail()
    {
        string text = ProviderText.Describe(new(ProviderState.Failed, 0, ProviderText.PawnIoMissing, "0x5"));
        Assert.Contains(Loc.Get(ProviderText.PawnIoMissing), text); Assert.Contains("(0x5)", text);
    }
}
