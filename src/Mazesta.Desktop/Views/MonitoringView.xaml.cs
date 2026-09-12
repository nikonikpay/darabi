using System.Windows.Controls;
using System.Windows.Input;
using Mazesta.Desktop.ViewModels;

namespace Mazesta.Desktop.Views;

public partial class MonitoringView : UserControl
{
    public MonitoringView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as MonitoringViewModel)?.Dispose();
    }

    private void Rows_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView { SelectedItem: SensorRowViewModel row } && DataContext is MonitoringViewModel vm)
            vm.OpenChartCommand.Execute(row);
    }

    private void Rows_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ListView { SelectedItem: SensorRowViewModel row } && DataContext is MonitoringViewModel vm)
        {
            vm.OpenChartCommand.Execute(row);
            e.Handled = true;
        }
    }
}
