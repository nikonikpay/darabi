using System.Globalization; using System.Windows.Data; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.Controls;

public sealed class RepeatModeToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode m ? Loc.Get($"Test_Repeat_{m}") : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
