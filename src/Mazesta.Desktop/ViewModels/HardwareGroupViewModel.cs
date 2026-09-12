using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class HardwareGroupViewModel : ObservableObject
{
    public HardwareNode Node { get; }
    public string Title => Node.Name;
    public HardwareKind Kind => Node.Kind;
    public string Id => Node.Id.Value;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];

    public HardwareGroupViewModel(HardwareNode node)
    {
        Node = node;
    }
}
