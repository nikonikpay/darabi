using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Composition; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;

/// <summary>
/// A component page from the spec's sidebar (§9.2: CPU, GPU, Network, Storage): that part's inventory, its live sensors and its
/// benchmarks on one page. It only assembles pieces that exist elsewhere - the System Information sections for the part, the
/// Monitoring tree limited to it, and its rows of the Benchmarks page (runs are shared with that page through BenchmarkRunner).
/// </summary>
public sealed partial class ComponentViewModel : ObservableObject, IDisposable
{
    public HardwareKind Kind { get; }
    public string Title { get; }
    public string Note { get; }
    public ObservableCollection<InfoSection> Inventory { get; } = [];
    public MonitoringViewModel Sensors { get; }
    public BenchmarksViewModel Benchmarks { get; }
    public bool HasSensors => Sensors.Groups.Count > 0;
    public bool HasBenchmarks => Benchmarks.Rows.Count > 0;
    public Task Loaded { get; }

    public ComponentViewModel(HardwareKind kind, InventoryCache inventory, MonitoringViewModel sensors, BenchmarksViewModel benchmarks, Func<Action, object> dispatch)
    {
        Kind = kind; Sensors = sensors; Benchmarks = benchmarks;
        Title = Loc.Get("Nav_" + kind); Note = Loc.Get("Component_Note_" + kind);
        Loaded = LoadAsync(inventory, dispatch);
    }

    private async Task LoadAsync(InventoryCache inventory, Func<Action, object> dispatch)
    {
        var inv = await inventory.GetAsync().ConfigureAwait(false);
        dispatch(() => { foreach (var s in SystemInfoViewModel.Component(inv, Kind)) Inventory.Add(s); });
    }

    public void Dispose() { Sensors.Dispose(); Benchmarks.Dispose(); }
}
