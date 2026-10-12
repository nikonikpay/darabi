namespace Mazesta.Core.Health;

public enum ThermalSensor { CpuPackage, CpuHottestCore, GpuCore, GpuHotSpot, GpuVram, Drive }

/// <summary>What the guard looks at in one poll; a member is null when the machine has no such sensor (a rule never runs on a guess).</summary>
public sealed record ThermalSample(double? CpuPackageC, double? CpuHottestCoreC, double? GpuCoreC, double? GpuHotSpotC, double? GpuVramC, double? DriveC);

/// <param name="Hold">How long a part may stay at or above its limit before the test is stopped.</param>
public sealed record ThermalLimits(double CpuC, double GpuC, double GpuHotSpotC, double GpuVramC, double DriveC, TimeSpan Hold);

/// <param name="Sustained">False: the reading passed the part's absolute ceiling at once. True: it stayed at or above the limit for the whole hold.</param>
public sealed record ThermalTrip(ThermalSensor Sensor, double ValueC, double LimitC, bool Sustained, DateTimeOffset At);

/// <summary>
/// The tests' "stop when a part overheats" rule. A part is stopped at once when it reaches its ceiling (<see cref="Ceiling"/>: 105 °C, the highest limit the settings allow;
/// GDDR6X slows itself near 110 °C; a drive throttles from about 85 °C) and after <see cref="ThermalLimits.Hold"/> (10 minutes by default) of staying at or above its own
/// limit (the tray's temperature limits). Unlike <see cref="HealthAlerts"/> no load condition applies: the test is the load.
/// </summary>
public sealed class ThermalGuard(ThermalLimits limits)
{
    public const double CpuCeilingC = 105, GpuCeilingC = 105, VramCeilingC = 110, DriveCeilingC = 85;
    private readonly Dictionary<ThermalSensor, DateTimeOffset> _since = [];
    private readonly Dictionary<ThermalSensor, DateTimeOffset> _last = [];

    public static double Ceiling(ThermalSensor s) => s switch { ThermalSensor.GpuVram => VramCeilingC, ThermalSensor.Drive => DriveCeilingC, ThermalSensor.GpuCore or ThermalSensor.GpuHotSpot => GpuCeilingC, _ => CpuCeilingC };
    public double Limit(ThermalSensor s) => s switch
    {
        ThermalSensor.GpuCore => limits.GpuC, ThermalSensor.GpuHotSpot => limits.GpuHotSpotC, ThermalSensor.GpuVram => limits.GpuVramC, ThermalSensor.Drive => limits.DriveC, _ => limits.CpuC,
    };

    /// <summary>The first sensor that tripped in this poll, or null.</summary>
    public ThermalTrip? Evaluate(ThermalSample s, DateTimeOffset now)
    {
        foreach (var (sensor, value) in new[] { (ThermalSensor.CpuPackage, s.CpuPackageC), (ThermalSensor.CpuHottestCore, s.CpuHottestCoreC), (ThermalSensor.GpuCore, s.GpuCoreC),
                     (ThermalSensor.GpuHotSpot, s.GpuHotSpotC), (ThermalSensor.GpuVram, s.GpuVramC), (ThermalSensor.Drive, s.DriveC) })
        {
            if (value is not { } v) continue;   // no reading: the count neither advances nor resets
            double limit = Limit(sensor);
            if (v >= Ceiling(sensor)) return new(sensor, v, Ceiling(sensor), false, now);
            if (v < limit) { _since.Remove(sensor); continue; }
            // A gap longer than the hold (the PC slept, the sensor went away) starts the count over.
            if (!_since.TryGetValue(sensor, out var since) || now - _last[sensor] > limits.Hold) _since[sensor] = since = now;
            _last[sensor] = now;
            if (now - since >= limits.Hold) return new(sensor, v, limit, true, now);
        }
        return null;
    }
}
