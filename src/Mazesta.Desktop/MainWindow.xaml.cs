using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Mazesta.Core.Hardware;
using Mazesta.Desktop.ViewModels;
using Mazesta.Monitoring;
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
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // Developer shortcut (acceptance §15-7): Ctrl+Shift+F jumps to Monitoring and focuses CPU+GPU.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        if (DataContext is not ShellViewModel shell) return;
        var monitoring = shell.Items.FirstOrDefault(i => i.Key == "Nav_Monitoring");
        if (monitoring is not null) shell.Selected = monitoring;
        App.Services.GetRequiredService<MonitoringFocus>().RequestFocus(new HashSet<HardwareKind> { HardwareKind.Cpu, HardwareKind.Gpu }, "dev-shortcut");
        e.Handled = true;
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
        // Save never throws (it returns false and logs). Tell the user in the status banner rather
        // than closing silently on a data folder that has become unwritable.
        if (!store.Save(config) && DataContext is ShellViewModel shell) shell.ShowBanner(Localization.Loc.Get("Settings_SaveFailed"));
    }
}
