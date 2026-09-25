using System.Collections.Specialized; using System.Windows; using System.Windows.Controls;
namespace Mazesta.Desktop.Views;

public partial class WindowsToolsView : UserControl
{
    public WindowsToolsView() { InitializeComponent(); }
}

/// <summary>Keeps a log list scrolled to its newest line as lines arrive, like a console.</summary>
public static class AutoScroll
{
    public static readonly DependencyProperty ToEndProperty = DependencyProperty.RegisterAttached("ToEnd", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnChanged));
    public static bool GetToEnd(DependencyObject d) => (bool)d.GetValue(ToEndProperty);
    public static void SetToEnd(DependencyObject d, bool value) => d.SetValue(ToEndProperty, value);
    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list || e.NewValue is not true) return;
        ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, a) => { if (a.Action == NotifyCollectionChangedAction.Add && list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]); };
    }
}
