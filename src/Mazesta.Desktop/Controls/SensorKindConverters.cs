using System.Globalization; using System.Windows; using System.Windows.Data; using Mazesta.Core.Hardware;
namespace Mazesta.Desktop.Controls;

/// <summary>Maps a <see cref="SensorKind"/> to the icon family it shares with related kinds (Energy with
/// Power, SmallData with Data, Frequency with Clock…), so the theme needs one icon and one colour per
/// family instead of one per enum member.</summary>
internal static class SensorKindStyle
{
    public static string Family(SensorKind kind) => kind switch
    {
        SensorKind.Energy => "Power",
        SensorKind.Frequency => "Clock",
        SensorKind.SmallData => "Data",
        SensorKind.Timing => "Timespan",
        SensorKind.Temperature or SensorKind.Voltage or SensorKind.Current or SensorKind.Power or SensorKind.Clock or SensorKind.Load
            or SensorKind.Fan or SensorKind.Control or SensorKind.Data or SensorKind.Throughput or SensorKind.Timespan => kind.ToString(),
        _ => "Default"
    };
}

public sealed class SensorKindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Application.Current.TryFindResource("Icon." + SensorKindStyle.Family(value is SensorKind k ? k : SensorKind.Unknown));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class SensorKindToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Application.Current.TryFindResource("Brush.Kind." + SensorKindStyle.Family(value is SensorKind k ? k : SensorKind.Unknown));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
