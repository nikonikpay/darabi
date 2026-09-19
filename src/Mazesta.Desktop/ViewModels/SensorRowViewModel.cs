using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SensorRowViewModel : ObservableObject
{
    public SensorDefinition Definition { get; }
    /// <summary>The node this row belongs to. The monitoring page is one virtualizing ListView grouped by
    /// node and then by <see cref="Section"/>, so group headers and expansion stay user-owned while only
    /// the visible rows are realized.</summary>
    public HardwareGroupViewModel Group { get; }
    public SensorSectionViewModel Section { get; }
    public string Name => Definition.Name;
    public SensorKind Kind => Definition.Kind;
    /// <summary>Every reading is text with its unit attached ("4.33 GHz", "58.0 °C"); there is no separate unit column.</summary>
    [ObservableProperty] private string _current = Loc.Get("Value_NotAvailable");
    [ObservableProperty] private string _min = "";
    [ObservableProperty] private string _max = "";
    [ObservableProperty] private string _avg = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private DataQuality _quality = DataQuality.Missing;
    [ObservableProperty] private bool _isVisible = true;

    public SensorRowViewModel(SensorDefinition definition, SensorSectionViewModel section, HardwareGroupViewModel group)
    {
        Definition = definition;
        Section = section;
        Group = group;
    }

    public void Apply(SensorReading r, SensorStats s)
    {
        Quality = r.Quality;
        bool hasValue = r.Quality is DataQuality.Ok or DataQuality.Stale && r.Value is not null;
        Current = hasValue ? Show(r.Value!.Value) : Loc.Get(r.Quality == DataQuality.Invalid ? "Value_Invalid" : "Value_NotAvailable");
        State = r.Quality switch { DataQuality.Ok => "", DataQuality.Stale => Loc.Get("Value_Stale"), DataQuality.Invalid => Loc.Get("Value_Invalid"), _ => Loc.Get("Value_NotAvailable") };
        Min = s.Min is { } mn ? Show(mn) : ""; Max = s.Max is { } mx ? Show(mx) : ""; Avg = s.Average is { } av ? Show(av) : "";
    }

    private string Show(double value) => Units.FormatWithSymbol(value, Definition.Unit);
}
