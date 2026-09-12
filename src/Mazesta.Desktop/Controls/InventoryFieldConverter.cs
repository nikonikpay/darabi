using System.Globalization;
using System.Windows.Data;

namespace Mazesta.Desktop.Controls;

/// <summary>
/// Extracts a field from a <c>(string Label, string Value)</c> tuple for XAML binding.
/// WPF's binding engine only resolves CLR *properties* by path (e.g. "Item1"), not the public
/// *fields* that <see cref="System.ValueTuple{T1, T2}"/> actually exposes, so
/// <c>{Binding Item1}</c>/<c>{Binding Item2}</c> against a tuple silently binds to nothing. This
/// converter reads the tuple directly in code, where field access works normally.
/// </summary>
public sealed class InventoryFieldConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is (string label, string val) ? (Equals(parameter, "Value") ? val : label) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
