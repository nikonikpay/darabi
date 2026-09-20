using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mazesta.Core.Hardware;
using Mazesta.Desktop.Localization;
using Mazesta.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace Mazesta.Desktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PollingEngine _engine;
    private readonly IServiceProvider _sp;

    [ObservableProperty] private NavItem? _selected;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _providerStatusText = Loc.Get("Status_Provider_Starting");
    [ObservableProperty] private string _intervalText = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string? _banner;
    [ObservableProperty] private string? _bannerLinkText;
    [ObservableProperty] private string? _bannerLinkUrl;
    /// <summary>False until the provider has settled (Ready, Degraded or Failed). The sidebar is
    /// disabled meanwhile: a click during the hardware scan would build a page against an empty
    /// hardware list and that page would never pick up the real sensors.</summary>
    [ObservableProperty] private bool _isNavigationEnabled;

    /// <summary>The official PawnIO page, offered in the banner when the driver is missing.</summary>
    public const string PawnIoUrl = "https://pawnio.eu/";
    /// <summary>Mirrors LibreHardwareMonitorProvider.ReasonPawnIoMissing; the Desktop layer knows
    /// the key, not the provider type.</summary>
    private const string LibreHardwareMonitorReasonPawnIoMissing = "Provider.PawnIoMissing";
    private bool _providerOwnsBanner;

    public ObservableCollection<NavItem> Items { get; }

    public ShellViewModel(PollingEngine engine, IServiceProvider sp)
    {
        _engine = engine;
        _sp = sp;
        Items = new(
        [
            new("Nav_Dashboard", "", () => sp.GetRequiredService<Func<DashboardViewModel>>()()),
            new("Nav_Monitoring", "", () => sp.GetRequiredService<Func<MonitoringViewModel>>()()),
            new("Nav_Tests", "", () => sp.GetRequiredService<Func<TestCenterViewModel>>()()),
            new("Nav_Benchmarks", "", () => new PlaceholderViewModel("Nav_Benchmarks")),
            new("Nav_Gpu", "", () => new PlaceholderViewModel("Nav_Gpu")),
            new("Nav_Cpu", "", () => new PlaceholderViewModel("Nav_Cpu")),
            new("Nav_Network", "", () => new PlaceholderViewModel("Nav_Network")),
            new("Nav_Storage", "", () => new PlaceholderViewModel("Nav_Storage")),
            new("Nav_Gaming", "", () => new PlaceholderViewModel("Nav_Gaming")),
            new("Nav_WindowsTools", "", () => new PlaceholderViewModel("Nav_WindowsTools")),
            new("Nav_Reports", "", () => sp.GetRequiredService<Func<ReportsViewModel>>()()),
            new("Nav_Settings", "", () => sp.GetRequiredService<Func<SettingsViewModel>>()()),
        ]);
        engine.Provider.StatusChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ApplyProviderStatus(s));
        engine.StateChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsPaused = s == EngineState.Paused;
            if (s == EngineState.Failed) ShowBanner(Loc.Get("Engine_Failed"));
        });
        IntervalText = Loc.Format("Status_Interval", engine.FastInterval.TotalSeconds);
    }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        try { CurrentPage = value.PageFactory(); }
        catch (Exception e) { _sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Navigation").LogError(e, "Opening page {Page} failed", value.Key); }
    }

    // The page view models subscribe to the polling engine; the outgoing page is disposed here so
    // the subscription goes with it. Nothing else holds them - they are built by a Func<T> factory,
    // not by the container (see Bootstrapper.AddViewModelFactory) - so this is their only owner.
    partial void OnCurrentPageChanged(object? oldValue, object? newValue) { if (!ReferenceEquals(oldValue, newValue)) (oldValue as IDisposable)?.Dispose(); }

    [RelayCommand]
    private void TogglePause() { if (_engine.State == EngineState.Paused) _engine.Resume(); else _engine.Pause(); }

    /// <summary>
    /// Spec §5.4/§9: a degraded or failed provider is a banner, not just a status-bar line - the
    /// customer must see that some readings are missing without reading the footer. The status-bar
    /// text is kept as well. Ready clears the banner.
    /// </summary>
    internal void ApplyProviderStatus(ProviderStatus s)
    {
        ProviderStatusText = Describe(s);
        if (s.State == ProviderState.Ready)
        {
            // Only a banner this method put up is cleared: a config-corrupt banner raised at
            // startup is the app's, and must not vanish because the sensors came up fine.
            if (_providerOwnsBanner) { ClearBanner(); _providerOwnsBanner = false; }
        }
        else if (s.State is ProviderState.Degraded or ProviderState.Failed)
        {
            bool pawnIo = s.ReasonKey == LibreHardwareMonitorReasonPawnIoMissing;
            string reason = Loc.Get(s.ReasonKey ?? "");
            ShowBanner(Loc.Format(s.State == ProviderState.Degraded ? "Banner_ProviderDegraded" : "Banner_ProviderFailed", reason),
                       pawnIo ? Loc.Get("Banner_InstallPawnIo") : null,
                       pawnIo ? PawnIoUrl : null);
            _providerOwnsBanner = true;
        }
        if (s.State is ProviderState.Ready or ProviderState.Degraded or ProviderState.Failed) IsNavigationEnabled = true;
    }

    public void ShowBanner(string text, string? linkText = null, string? linkUrl = null)
    { Banner = text; BannerLinkText = linkText; BannerLinkUrl = linkUrl; }

    public void ClearBanner() { Banner = null; BannerLinkText = null; BannerLinkUrl = null; }

    [RelayCommand]
    private void OpenBannerLink()
    {
        if (BannerLinkUrl is { } url) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void RefreshInterval() => IntervalText = Loc.Format("Status_Interval", _engine.FastInterval.TotalSeconds);

    public static string Describe(ProviderStatus s) => s.State switch
    {
        ProviderState.Ready => Loc.Format("Status_Provider_Ready", s.SensorCount),
        ProviderState.Degraded => Loc.Format("Status_Provider_Degraded", Loc.Get(s.ReasonKey ?? "")),
        ProviderState.Failed => Loc.Format("Status_Provider_Failed", Loc.Get(s.ReasonKey ?? "") + (s.Detail is null ? "" : $" ({s.Detail})")),
        _ => Loc.Get("Status_Provider_Starting")
    };
}
