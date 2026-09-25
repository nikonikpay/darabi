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

    [Fact] public void Ctrl_number_opens_that_page_only_once_navigation_is_enabled()
    {
        var shell = Build();
        shell.NavigateCommand.Execute("3"); Assert.Null(shell.Selected);
        shell.ApplyProviderStatus(ProviderStatus.Ready(1));
        shell.NavigateCommand.Execute("3"); Assert.Same(shell.Items[3], shell.Selected);
        shell.NavigateCommand.Execute("42"); Assert.Same(shell.Items[3], shell.Selected);
    }
    [Fact] public void A_saved_report_becomes_a_notice_coloured_by_its_verdict_and_a_click_dismisses_it()
    {
        Mazesta.Reporting.StoredReport Stored(Mazesta.Reporting.ReportVerdict? v) => new("f", "id", T0, v is null ? Mazesta.Reporting.ReportKind.Benchmark : Mazesta.Reporting.ReportKind.TestSession, v, new(1, 1, 0, 0, 0, 0, 0), "x", ["Storage"]);
        Assert.Equal(ToastKind.Success, ShellViewModel.ReportToast(Stored(Mazesta.Reporting.ReportVerdict.Passed)).Kind);
        Assert.Equal(ToastKind.Danger, ShellViewModel.ReportToast(Stored(Mazesta.Reporting.ReportVerdict.Failed)).Kind);
        Assert.Equal(ToastKind.Warning, ShellViewModel.ReportToast(Stored(Mazesta.Reporting.ReportVerdict.Incomplete)).Kind);
        var benchmark = ShellViewModel.ReportToast(Stored(null)); Assert.Equal(ToastKind.Info, benchmark.Kind); Assert.Contains("Storage", benchmark.Text);
        var shell = Build(); shell.Notify(benchmark); Assert.Single(shell.Toasts);
        shell.DismissToastCommand.Execute(benchmark); Assert.Empty(shell.Toasts);
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
