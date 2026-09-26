using System.Windows; using System.Windows.Controls; using Mazesta.Desktop.ViewModels;
namespace Mazesta.Desktop.Views;

public partial class TuningView : UserControl
{
    public TuningView() { InitializeComponent(); }
    private void OnLoaded(object sender, RoutedEventArgs e) => (DataContext as TuningViewModel)?.SetVisible(true);
    private void OnUnloaded(object sender, RoutedEventArgs e) => (DataContext as TuningViewModel)?.SetVisible(false);
}
