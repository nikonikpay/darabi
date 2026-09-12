using System.ComponentModel;
using System.Windows;
using Mazesta.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Mazesta.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var config = App.Services.GetRequiredService<AppConfig>();
        var store = App.Services.GetRequiredService<JsonStore<AppConfig>>();
        var charts = (Mazesta.Desktop.Services.ChartWindowService)App.Services.GetRequiredService<Mazesta.Desktop.ViewModels.IChartWindowService>();
        charts.PersistOpenWindows();
        bool maximized = WindowState == WindowState.Maximized;
        var bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        config.MainWindow = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized);
        store.Save(config);
    }
}
