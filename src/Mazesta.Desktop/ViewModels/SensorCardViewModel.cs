using System.Collections.ObjectModel; using System.Windows.Media; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class CardLine(string label, SensorId? id, Unit unit) : ObservableObject
{ public string Label { get; } = label; public SensorId? Id { get; } = id; public Unit Unit { get; } = unit; [ObservableProperty] private string _value = Loc.Get("Value_NotAvailable"); }
public sealed partial class SensorCardViewModel(string titleKey, HardwareKind kind, string? subtitle = null) : ObservableObject
{
    public string Title => Loc.Get(titleKey) + (subtitle is null ? "" : $" · {subtitle}"); public string HelpKey => titleKey; public HardwareKind Kind => kind;
    public Brush Accent => SeriesBrushes.For(kind);
    public ObservableCollection<CardLine> Lines { get; } = []; public bool HasAnySensor => Lines.Any(l => l.Id is not null);
}
