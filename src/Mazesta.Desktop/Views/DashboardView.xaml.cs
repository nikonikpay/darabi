using System.Windows.Controls;
using Mazesta.Desktop.ViewModels;

namespace Mazesta.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as DashboardViewModel)?.Dispose();
    }
}
