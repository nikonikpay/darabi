using System.Globalization;
using System.Windows.Data;

namespace Mazesta.Desktop.Controls;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && !b;
}
