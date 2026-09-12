using System.Windows;

namespace Mazesta.Desktop.Views;

public partial class ChartWindow : Window
{
    public ChartWindow()
    {
        InitializeComponent();
        RootGrid.FlowDirection = Localization.Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
