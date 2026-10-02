namespace Mazesta.Core.Hardware;

/// <summary>One measured part of the system's power: the device, the sensor it was read from, and its watts.</summary>
public sealed record PowerPart(string Device, HardwareKind Kind, string Sensor, double Watts);

/// <summary>What the computer draws, as far as its sensors say. <see cref="Measured"/> is the sum of <see cref="Parts"/>; <see cref="FromPsu"/> means
/// it is the power supply's own output reading (the whole system's DC side). <see cref="Unmeasured"/> names what has no power sensor, so the sum is
/// never passed off as the whole computer.</summary>
public sealed record PowerTotal(double? Measured, bool FromPsu, IReadOnlyList<PowerPart> Parts, IReadOnlyList<HardwareKind> Unmeasured);

/// <summary>
/// Adds up the system's power from its sensors without counting anything twice. The CPU's package power already holds its cores' (and the
/// cores' own readings are parts of it), so a CPU counts by its package alone; a graphics card by its one whole-card reading. RAM and drives count
/// where they report their own power (some DDR5 modules, a few SSDs). A power supply that reports its output power measures all of it, and then it
/// alone is the total. The motherboard, the fans and anything else without a sensor are listed as not measured, never guessed.
/// </summary>
public static class PowerTotals
{
    public static PowerTotal Sum(IEnumerable<(string Device, HardwareKind Kind, IReadOnlyList<(SensorDefinition Sensor, double Value)> Readings)> devices)
    {
        var list = devices.ToList(); var parts = new List<PowerPart>();
        static bool Watts(SensorDefinition s) => s.Kind == SensorKind.Power;

        // A power supply's own output reading is the whole computer's draw (on its DC side).
        foreach (var (device, kind, readings) in list.Where(d => d.Kind == HardwareKind.Psu))
            if (readings.Where(r => Watts(r.Sensor) && r.Value > 0).OrderByDescending(r => r.Sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)).ThenByDescending(r => r.Value).FirstOrDefault() is { Sensor: not null } psu)
                return new(psu.Value, true, [new(device, kind, psu.Sensor.Name, psu.Value)], []);

        foreach (var (device, kind, readings) in list)
        {
            var power = readings.Where(r => Watts(r.Sensor) && r.Value >= 0).ToList();
            switch (kind)
            {
                case HardwareKind.Cpu:
                    if (power.FirstOrDefault(r => r.Sensor.Role == SensorRole.CpuPackagePower) is { Sensor: not null } pkg) parts.Add(new(device, kind, pkg.Sensor.Name, pkg.Value));
                    break;
                case HardwareKind.Gpu:
                    if (power.FirstOrDefault(r => r.Sensor.Role == SensorRole.GpuPower) is { Sensor: not null } card) parts.Add(new(device, kind, card.Sensor.Name, card.Value));
                    break;
                case HardwareKind.Memory or HardwareKind.Storage:
                    parts.AddRange(power.Select(r => new PowerPart(device, kind, r.Sensor.Name, r.Value)));
                    break;
            }
        }
        var measured = parts.Select(p => p.Kind).ToHashSet();
        // The board (its chipset, VRM losses, USB), the fans and the cooler have no power reading on a PC; RAM and drives only where they gave one.
        var unmeasured = new[] { HardwareKind.Motherboard, HardwareKind.Memory, HardwareKind.Storage, HardwareKind.Cooler }
            .Where(k => !measured.Contains(k) && (k is HardwareKind.Motherboard or HardwareKind.Cooler || list.Any(d => d.Kind == k))).ToList();
        foreach (var k in new[] { HardwareKind.Cpu, HardwareKind.Gpu }) if (!measured.Contains(k) && list.Any(d => d.Kind == k)) unmeasured.Insert(0, k);
        return new(parts.Count > 0 ? parts.Sum(p => p.Watts) : null, false, parts, unmeasured);
    }
}
