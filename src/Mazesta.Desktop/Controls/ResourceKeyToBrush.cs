using System.Globalization; using System.Windows; using System.Windows.Data; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;

/// <summary>A brush from the theme by its key (a view model names the colour, the view resolves it).</summary>
public sealed class ResourceKeyToBrush : IValueConverter
{
    public static readonly ResourceKeyToBrush Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key && Application.Current?.TryFindResource(key) is Brush b ? b : Brushes.Gray;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
