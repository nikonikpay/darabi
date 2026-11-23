namespace Mazesta.Core.Hardware;

public static class ReadingValidator
{
    /// <summary>A drive reporting this or more is not reporting a temperature (a Samsung 990 Pro answers 255 for "unknown").</summary>
    public const double MaxDriveCelsius = 150;

    /// <summary>A board or chipset sensor reading more than this is an unwired Super I/O input (they float to 110-127 °C), not a temperature: real boards throttle or shut down long before.</summary>
    public const double MaxBoardCelsius = 100;

    public static DataQuality Validate(SensorKind kind, double? value, SensorRole role)
        => role is SensorRole.BoardTemp or SensorRole.ChipsetTemp && value > MaxBoardCelsius ? DataQuality.Invalid : Validate(kind, value);

    public static DataQuality Validate(SensorKind kind, double? value)
    {
        if (value is null) return DataQuality.Missing;
        double v = value.Value;
        if (double.IsNaN(v) || double.IsInfinity(v)) return DataQuality.Invalid;
        bool invalid = kind switch
        {
            SensorKind.Temperature => v <= 0 || v > 150,
            SensorKind.Clock => v <= 0,
            SensorKind.Load or SensorKind.Level or SensorKind.Control => v < 0 || v > 100,
            SensorKind.Voltage => v < 0 || v > 20,
            // LHM returns 0f (not null) when RAPL/MSR energy counters are unreadable, so a zero
            // power/current/energy reading is an unreadable counter, never a real measurement.
            SensorKind.Power or SensorKind.Current or SensorKind.Energy => v <= 0,
            SensorKind.Fan or SensorKind.Data or SensorKind.SmallData or SensorKind.Throughput or SensorKind.Count => v < 0,
            _ => false
        };
        return invalid ? DataQuality.Invalid : DataQuality.Ok;
    }
}
