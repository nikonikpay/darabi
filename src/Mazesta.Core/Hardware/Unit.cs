namespace Mazesta.Core.Hardware;

public enum Unit { Celsius, MegaHertz, Percent, Volt, Ampere, Watt, WattHour, Rpm, Gigabyte, Megabyte, BytesPerSecond, Seconds, Hertz, Nanoseconds, Decibel, Ratio, LitersPerHour, MicroSiemens, None }

public static class Units
{
    public static Unit ForKind(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => Unit.Celsius, SensorKind.Clock => Unit.MegaHertz, SensorKind.Load or SensorKind.Control or SensorKind.Level or SensorKind.Humidity => Unit.Percent,
        SensorKind.Voltage => Unit.Volt, SensorKind.Current => Unit.Ampere, SensorKind.Power => Unit.Watt, SensorKind.Energy => Unit.WattHour, SensorKind.Fan => Unit.Rpm,
        SensorKind.Data => Unit.Gigabyte, SensorKind.SmallData => Unit.Megabyte, SensorKind.Throughput => Unit.BytesPerSecond, SensorKind.Timespan => Unit.Seconds,
        SensorKind.Frequency => Unit.Hertz, SensorKind.Timing => Unit.Nanoseconds, SensorKind.Noise => Unit.Decibel, SensorKind.Factor => Unit.Ratio,
        SensorKind.Flow => Unit.LitersPerHour, SensorKind.Conductivity => Unit.MicroSiemens, _ => Unit.None
    };
    public static string Symbol(Unit unit) => unit switch
    {
        Unit.Celsius => "°C", Unit.MegaHertz => "MHz", Unit.Percent => "%", Unit.Volt => "V", Unit.Ampere => "A", Unit.Watt => "W", Unit.WattHour => "Wh", Unit.Rpm => "RPM",
        Unit.Gigabyte => "GB", Unit.Megabyte => "MB", Unit.BytesPerSecond => "B/s", Unit.Seconds => "s", Unit.Hertz => "Hz", Unit.Nanoseconds => "ns", Unit.Decibel => "dB",
        Unit.Ratio => "", Unit.LitersPerHour => "L/h", Unit.MicroSiemens => "µS", _ => ""
    };
    public static int Decimals(Unit unit) => unit switch
    {
        Unit.Volt => 3, Unit.Celsius or Unit.Watt or Unit.Ampere or Unit.Gigabyte or Unit.Ratio or Unit.Nanoseconds => 1,
        _ => 0
    };
    public static string Format(double value, Unit unit) => value.ToString("F" + Decimals(unit), System.Globalization.CultureInfo.InvariantCulture);
    public static string FormatWithSymbol(double value, Unit unit)
    {
        if (unit == Unit.BytesPerSecond)
        {
            string[] prefixes = ["B/s", "KB/s", "MB/s", "GB/s"]; int i = 0; double v = value;
            while (v >= 1000 && i < prefixes.Length - 1) { v /= 1000; i++; }
            return $"{v.ToString(i == 0 ? "F0" : "F1", System.Globalization.CultureInfo.InvariantCulture)} {prefixes[i]}";
        }
        if (unit == Unit.MegaHertz && value >= 1000) return $"{(value / 1000).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} GHz";
        var s = Symbol(unit);
        return s.Length == 0 ? Format(value, unit) : $"{Format(value, unit)} {s}";
    }
}
