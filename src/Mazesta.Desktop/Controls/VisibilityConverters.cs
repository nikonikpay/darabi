using System.Globalization; using System.Windows; using System.Windows.Data;
namespace Mazesta.Desktop.Controls;

/// <summary>Visible for a non-empty string, collapsed for an empty or missing one.</summary>
public sealed class NonEmptyToVisibility : IValueConverter
{
    public static readonly NonEmptyToVisibility Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Collapsed for true, visible for false: an empty-state line shown while a list has nothing in it.</summary>
public sealed class NegatedBoolToVisibility : IValueConverter
{
    public static readonly NegatedBoolToVisibility Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
