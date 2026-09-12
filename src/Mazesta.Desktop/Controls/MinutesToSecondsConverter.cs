using System.Globalization;
using System.Windows.Data;

namespace Mazesta.Desktop.Controls;

public sealed class MinutesToSecondsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int minutes ? minutes * 60 : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
