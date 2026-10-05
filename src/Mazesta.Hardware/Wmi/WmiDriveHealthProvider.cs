using Mazesta.Core.Providers;
namespace Mazesta.Hardware.Wmi;

/// <summary>Drive health from Windows Storage Management: MSFT_PhysicalDisk (the health verdict, which Windows derives from the drive's own SMART
/// and NVMe critical warnings) and its associated MSFT_StorageReliabilityCounter (wear, temperature, uncorrected errors, power-on hours - the same
/// numbers as Get-StorageReliabilityCounter; they need administrator rights, which the app has). Drives on USB (flash sticks, external disks) are left out: they
/// are the customer's or the technician's, a failing one can stall these queries for a minute, and the app's start must not wait on them.</summary>
public sealed class WmiDriveHealthProvider(IWmiQuery query) : IDriveHealthProvider
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    public IReadOnlyList<DriveHealth> Read()
        => WmiDriveHealthParser.Parse(query.QueryWithRelated(Storage, "SELECT * FROM MSFT_PhysicalDisk WHERE BusType <> 7 OR BusType IS NULL", "MSFT_StorageReliabilityCounter"));
}

public static class WmiDriveHealthParser
{
    public static IReadOnlyList<DriveHealth> Parse(IReadOnlyList<(IReadOnlyDictionary<string, object?> Row, IReadOnlyDictionary<string, object?>? Related)> rows) => [.. rows.Select(x =>
    {
        var (d, c) = x; bool hdd = N(d, "MediaType") == 3;
        return new DriveHealth(S(d, "FriendlyName") ?? "?", S(d, "SerialNumber"),
            N(d, "HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", null => null, _ => "Unknown" },
            hdd ? null : (int?)N(c, "Wear"),                            // an HDD has no wear counter; Windows reports 0 for it
            Positive(c, "Temperature"), Positive(c, "TemperatureMax"),  // 0 °C means "not reported", never a running drive's temperature
            N(c, "ReadErrorsUncorrected"), N(c, "WriteErrorsUncorrected"), N(c, "PowerOnHours"),
            N(d, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 3 => "ATA", null => null, var b => $"Bus {b}" });
    })];

    private static string? S(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    private static long? N(IReadOnlyDictionary<string, object?>? r, string k) => r is not null && r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : null;
    private static double? Positive(IReadOnlyDictionary<string, object?>? r, string k) => N(r, k) is long t && t > 0 ? t : null;
}
