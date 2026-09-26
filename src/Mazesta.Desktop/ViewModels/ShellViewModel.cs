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
    /// <summary>Notices shown in the corner (newest last). Each goes away by itself after <see cref="ToastSeconds"/> or when clicked; nothing
    /// runs while there is none.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];
    public const int ToastSeconds = 8;

    /// <summary>The service job being worked on (spec 7.1), shown under the sidebar and printed on every report while set. Persian digits become
    /// Latin so the number reads the same in every report; saved with the settings when the app closes.</summary>
    public string ServiceNumber
    {
        get => _sp.GetService<Mazesta.Persistence.AppConfig>()?.ServiceNumber ?? "";
        set { if (_sp.GetService<Mazesta.Persistence.AppConfig>() is { } config) { config.ServiceNumber = Mazesta.Core.Text.PersianDigits.Normalize(value ?? "").Trim(); OnPropertyChanged(); } }
    }

    public ShellViewModel(PollingEngine engine, IServiceProvider sp)
    {
        ComponentViewModel Component(HardwareKind kind) => sp.GetRequiredService<Func<HardwareKind, ComponentViewModel>>()(kind);
        _engine = engine;
        _sp = sp;
        Items = new(
        [
            new("Nav_Dashboard", "", () => sp.GetRequiredService<Func<DashboardViewModel>>()()),
            new("Nav_Monitoring", "", () => sp.GetRequiredService<Func<MonitoringViewModel>>()()),
            new("Nav_Tests", "", () => sp.GetRequiredService<Func<TestCenterViewModel>>()()),
            new("Nav_SystemInfo", "", () => sp.GetRequiredService<Func<SystemInfoViewModel>>()()),
            new("Nav_Benchmarks", "", () => sp.GetRequiredService<Func<BenchmarksViewModel>>()()),
            new("Nav_Gpu", "", () => Component(HardwareKind.Gpu)),
            new("Nav_Cpu", "", () => Component(HardwareKind.Cpu)),
            new("Nav_Network", "", () => Component(HardwareKind.Network)),
            new("Nav_Storage", "", () => Component(HardwareKind.Storage)),
            new("Nav_Gaming", "", () => sp.GetRequiredService<Func<GamingViewModel>>()()),
            new("Nav_Tuning", "", () => sp.GetRequiredService<TuningViewModel>()),
            new("Nav_WindowsTools", "", () => sp.GetRequiredService<WindowsToolsViewModel>()),
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
        if (sp.GetService<Services.ReportService>() is { } reports) reports.ReportCreated += r => Ui(() => Notify(ReportToast(r)));
        if (sp.GetService<Services.OverlayService>() is { } overlay) overlay.VisibilityChanged += v => IsOverlayVisible = v;
        if (sp.GetService<Mazesta.Diagnostics.Benchmarks.BenchmarkRunner>() is { } runner)
            runner.Finished += run => { if (run.Result.Status != Mazesta.Diagnostics.Benchmarks.BenchmarkStatus.Completed) Ui(() => Notify(new(Loc.Format("Toast_BenchmarkNotCompleted", Loc.Get(run.Definition.NameKey), Loc.Get("Bench_Status_" + run.Result.Status)), ToastKind.Warning))); };
    }

    private static void Ui(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);

    /// <summary>A completed benchmark shows through its report; a finished test session through its report's verdict.</summary>
    internal static ToastViewModel ReportToast(Mazesta.Reporting.StoredReport r) => r.Verdict switch
    {
        null => new(Loc.Format("Toast_BenchmarkReport", string.Join(" · ", r.Benchmarks)), ToastKind.Info),
        Mazesta.Reporting.ReportVerdict.Passed => new(Loc.Get("Toast_TestsPassed"), ToastKind.Success),
        Mazesta.Reporting.ReportVerdict.Failed => new(Loc.Get("Toast_TestsFailed"), ToastKind.Danger),
        _ => new(Loc.Get("Toast_TestsIncomplete"), ToastKind.Warning)
    };

    public void Notify(ToastViewModel toast)
    {
        Toasts.Add(toast);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(ToastSeconds) };
        timer.Tick += (_, _) => { timer.Stop(); Toasts.Remove(toast); };
        timer.Start();
    }

    [RelayCommand] private void DismissToast(ToastViewModel toast) => Toasts.Remove(toast);

    /// <summary>Ctrl+1 ... Ctrl+9 and Ctrl+0 open the first ten pages (spec 9.4: keyboard shortcuts for the main operations).</summary>
    [RelayCommand]
    private void Navigate(string index) { if (IsNavigationEnabled && int.TryParse(index, out int i) && i >= 0 && i < Items.Count) Selected = Items[i]; }

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

    /// <summary>The on-screen overlay is up (the status bar's toggle shows it).</summary>
    [ObservableProperty] private bool _isOverlayVisible;
    [RelayCommand] private void ToggleOverlay() => _sp.GetService<Services.OverlayService>()?.Toggle();

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
