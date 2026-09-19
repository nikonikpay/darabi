using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;

/// <summary>
/// Turns LibreHardwareMonitor's generic Super I/O names ("Fan #2") into the label of the header the
/// sensor is actually wired to, but only where that wiring has been <b>verified</b> - spec §5 forbids
/// attaching a specific meaning to an ambiguous sensor name without a valid mapping. Each verified entry
/// names the board, the Super I/O chip, the channel and the evidence it was checked against.
/// </summary>
internal static class SensorNameCatalog
{
    private readonly record struct Channel(string Board, string Chip, SensorType Type, int Index);

    /// <summary>ASUS PRIME B550M-A, Nuvoton NCT6798D. Evidence: fan RPM of channel #2 and #7 tracked
    /// HWiNFO's "CPU" and "CPU_OPT" rows (≈900 vs ≈830 RPM and ≈1940 vs ≈1860 RPM, sampled together,
    /// spread explained by the fans' own fluctuation); recorded in docs/HARDWARE-MATRIX.md.</summary>
    private static readonly Dictionary<Channel, string> Verified = new()
    {
        [new("ASUS PRIME B550M-A", "Nuvoton NCT6798D", SensorType.Fan, 1)] = "CPU Fan",
        [new("ASUS PRIME B550M-A", "Nuvoton NCT6798D", SensorType.Fan, 6)] = "CPU_OPT Fan",
    };

    public static string Resolve(IHardware hardware, ISensor sensor)
    {
        if (hardware.HardwareType != HardwareType.SuperIO) return sensor.Name;
        string board = hardware.Parent?.Name ?? "";
        string? fan = Verified.GetValueOrDefault(new Channel(board, hardware.Name, SensorType.Fan, sensor.Index));
        return sensor.SensorType switch
        {
            SensorType.Fan when fan is not null => fan,
            // The duty-cycle sensor of a channel is literally named after the RPM sensor ("Fan #2"): two
            // different readings under one name. Suffix it so the two can be told apart in every list.
            SensorType.Control => (fan ?? sensor.Name) + " Control",
            _ => sensor.Name
        };
    }
}
