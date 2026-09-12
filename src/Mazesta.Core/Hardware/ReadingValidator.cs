namespace Mazesta.Core.Hardware;

public static class ReadingValidator
{
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
            SensorKind.Fan or SensorKind.Data or SensorKind.SmallData or SensorKind.Throughput => v < 0,
            _ => false
        };
        return invalid ? DataQuality.Invalid : DataQuality.Ok;
    }
}
