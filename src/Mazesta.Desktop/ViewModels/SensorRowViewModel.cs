using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SensorRowViewModel : ObservableObject
{
    public SensorDefinition Definition { get; }
    public string SubGroup { get; }
    public string Name => Definition.Name;
    public string Unit => Units.Symbol(Definition.Unit);
    [ObservableProperty] private string _current = Loc.Get("Value_NotAvailable");
    [ObservableProperty] private string _min = "";
    [ObservableProperty] private string _max = "";
    [ObservableProperty] private string _avg = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private DataQuality _quality = DataQuality.Missing;
    [ObservableProperty] private bool _isVisible = true;

    public SensorRowViewModel(SensorDefinition definition, string subGroup)
    {
        Definition = definition;
        SubGroup = subGroup;
    }

    public void Apply(SensorReading r, SensorStats s)
    {
        Quality = r.Quality;
        Current = r.Quality == DataQuality.Ok && r.Value is { } v ? Units.Format(v, Definition.Unit)
                : r.Quality == DataQuality.Stale && r.Value is { } sv ? Units.Format(sv, Definition.Unit) : Loc.Get("Value_NotAvailable");
        State = r.Quality switch { DataQuality.Ok => "", DataQuality.Stale => Loc.Get("Value_Stale"), DataQuality.Invalid => Loc.Get("Value_Invalid"), _ => Loc.Get("Value_NotAvailable") };
        Min = s.Min is { } mn ? Units.Format(mn, Definition.Unit) : ""; Max = s.Max is { } mx ? Units.Format(mx, Definition.Unit) : ""; Avg = s.Average is { } av ? Units.Format(av, Definition.Unit) : "";
    }
}
