using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Desktop.Tests;
public class ShellViewModelTests
{
    private sealed class NoServices : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    private static ShellViewModel Build()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider();
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance));
        return new ShellViewModel(e, new NoServices());
    }

    [Fact] public void Navigation_is_disabled_until_the_provider_settles()
    {
        var shell = Build();
        Assert.False(shell.IsNavigationEnabled);
        shell.ApplyProviderStatus(ProviderStatus.Starting);
        Assert.False(shell.IsNavigationEnabled);
        shell.ApplyProviderStatus(ProviderStatus.Ready(12));
        Assert.True(shell.IsNavigationEnabled);
    }

    [Fact] public void Failed_provider_also_enables_navigation_so_the_app_is_not_stuck()
    {
        var shell = Build();
        shell.ApplyProviderStatus(ProviderStatus.Failed("Provider.OpenFailed", "boom"));
        Assert.True(shell.IsNavigationEnabled);
    }

    [Fact] public void Degraded_provider_raises_a_banner_and_keeps_the_status_text()
    {
        var shell = Build();
        shell.ApplyProviderStatus(ProviderStatus.Degraded("Provider.NotElevated", "detail", 3));
        Assert.NotNull(shell.Banner);
        Assert.Contains(Loc.Get("Provider.NotElevated"), shell.Banner);
        Assert.Contains(Loc.Get("Provider.NotElevated"), shell.ProviderStatusText);
        Assert.Null(shell.BannerLinkUrl);                       // only PawnIO offers a link
    }

    [Fact] public void Failed_provider_raises_a_banner()
    {
        var shell = Build();
        shell.ApplyProviderStatus(ProviderStatus.Failed("Provider.OpenFailed", "boom"));
        Assert.NotNull(shell.Banner);
        Assert.Contains(Loc.Get("Provider.OpenFailed"), shell.Banner);
    }

    [Fact] public void Missing_pawnio_offers_the_official_download_link()
    {
        var shell = Build();
        shell.ApplyProviderStatus(ProviderStatus.Degraded("Provider.PawnIoMissing", "not installed", 200));
        Assert.Equal("https://pawnio.eu/", shell.BannerLinkUrl);
        Assert.Equal(Loc.Get("Banner_InstallPawnIo"), shell.BannerLinkText);
    }

    [Fact] public void Banner_clears_when_the_provider_becomes_ready()
    {
        var shell = Build();
        shell.ApplyProviderStatus(ProviderStatus.Degraded("Provider.PawnIoMissing", "not installed", 200));
        shell.ApplyProviderStatus(ProviderStatus.Ready(609));
        Assert.Null(shell.Banner); Assert.Null(shell.BannerLinkUrl); Assert.Null(shell.BannerLinkText);
    }

    [Fact] public void A_ready_provider_does_not_clear_a_banner_the_app_raised()
    {
        // The config-corrupt banner is put up at startup and must survive the sensors coming up.
        var shell = Build();
        shell.ShowBanner(Loc.Get("Config_Corrupt"));
        shell.ApplyProviderStatus(ProviderStatus.Ready(609));
        Assert.Equal(Loc.Get("Config_Corrupt"), shell.Banner);
    }

    [Fact] public void Sidebar_glyphs_are_distinct()
    {
        var shell = Build();
        var glyphs = shell.Items.Select(i => i.Glyph).ToList();
        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }
}
