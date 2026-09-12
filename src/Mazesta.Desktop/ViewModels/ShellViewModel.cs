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

    public ObservableCollection<NavItem> Items { get; }

    public ShellViewModel(PollingEngine engine, IServiceProvider sp)
    {
        _engine = engine;
        _sp = sp;
        Items = new(
        [
            new("Nav_Dashboard", "", () => sp.GetRequiredService<DashboardViewModel>()),
            new("Nav_Monitoring", "", () => sp.GetRequiredService<MonitoringViewModel>()),
            new("Nav_Tests", "", () => new PlaceholderViewModel("Nav_Tests")),
            new("Nav_Benchmarks", "", () => new PlaceholderViewModel("Nav_Benchmarks")),
            new("Nav_Gpu", "", () => new PlaceholderViewModel("Nav_Gpu")),
            new("Nav_Cpu", "", () => new PlaceholderViewModel("Nav_Cpu")),
            new("Nav_Network", "", () => new PlaceholderViewModel("Nav_Network")),
            new("Nav_Storage", "", () => new PlaceholderViewModel("Nav_Storage")),
            new("Nav_Gaming", "", () => new PlaceholderViewModel("Nav_Gaming")),
            new("Nav_WindowsTools", "", () => new PlaceholderViewModel("Nav_WindowsTools")),
            new("Nav_Reports", "", () => new PlaceholderViewModel("Nav_Reports")),
            new("Nav_Settings", "", () => sp.GetRequiredService<SettingsViewModel>()),
        ]);
        engine.Provider.StatusChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ProviderStatusText = Describe(s));
        engine.StateChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => IsPaused = s == EngineState.Paused);
        IntervalText = Loc.Format("Status_Interval", engine.FastInterval.TotalSeconds);
    }

    partial void OnSelectedChanged(NavItem? value) { if (value is not null) CurrentPage = value.PageFactory(); }

    [RelayCommand]
    private void TogglePause() { if (_engine.State == EngineState.Paused) _engine.Resume(); else _engine.Pause(); }

    public void ShowBanner(string text) => Banner = text;

    public void RefreshInterval() => IntervalText = Loc.Format("Status_Interval", _engine.FastInterval.TotalSeconds);

    public static string Describe(ProviderStatus s) => s.State switch
    {
        ProviderState.Ready => Loc.Format("Status_Provider_Ready", s.SensorCount),
        ProviderState.Degraded => Loc.Format("Status_Provider_Degraded", Loc.Get(s.ReasonKey ?? "")),
        ProviderState.Failed => Loc.Format("Status_Provider_Failed", Loc.Get(s.ReasonKey ?? "") + (s.Detail is null ? "" : $" ({s.Detail})")),
        _ => Loc.Get("Status_Provider_Starting")
    };
}
