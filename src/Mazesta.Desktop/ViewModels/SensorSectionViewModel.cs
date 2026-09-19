using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;

/// <summary>A collapsible group of related sensors inside one hardware node ("Core Clocks"). A flat
/// section has no title and no header: its sensors just sit at the top of the node, as HWiNFO lists a
/// sensor that has nothing to be grouped with. Expansion is the technician's and lives only as long as
/// the page.</summary>
public sealed partial class SensorSectionViewModel(SensorSection? section) : ObservableObject
{
    public bool IsFlat { get; } = section is null;
    public string Title { get; } = section is { } s ? TitleOf(s) : "";
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Family + the kind's plural ("Core" + Clock → "Core Clocks"); the bare plural for the generic bucket.
    /// The order of the two parts is the language's (Persian puts the kind first).</summary>
    internal static string TitleOf(SensorSection s)
    {
        string kind = Loc.Get("SensorKind_" + s.Kind);
        return s.Family.Length == 0 ? kind : Loc.Format("SensorGroup_Format", s.Family, kind);
    }

    /// <summary>Flat sensors first, then the generic bucket and named families by kind, so the order is the same every run.</summary>
    internal static int OrderOf(SensorSection? s) => s is null ? -1 : KindOrder(s.Value.Kind);
    private static int KindOrder(SensorKind k) => k switch
    {
        SensorKind.Temperature => 0, SensorKind.Voltage => 1, SensorKind.Current => 2, SensorKind.Power => 3, SensorKind.Energy => 4, SensorKind.Clock => 5,
        SensorKind.Load => 6, SensorKind.Fan => 7, SensorKind.Control => 8, SensorKind.Throughput => 9, SensorKind.Data => 10, SensorKind.SmallData => 11, _ => 20
    };
}
