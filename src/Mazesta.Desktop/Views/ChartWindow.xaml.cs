using System.Windows;

namespace Mazesta.Desktop.Views;

public partial class ChartWindow : Window
{
    public ChartWindow()
    {
        InitializeComponent();
        Localization.Rtl.Apply(RootGrid);
    }
}
