using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Hardware.Details;
namespace Mazesta.Hardware.Wmi;

/// <summary>Drive health from Windows Storage Management: MSFT_PhysicalDisk (the health verdict, which Windows derives from the drive's own SMART
/// and NVMe critical warnings) and its associated MSFT_StorageReliabilityCounter (wear, temperature, uncorrected errors, power-on hours - the same
/// numbers as Get-StorageReliabilityCounter; they need administrator rights, which the app has). Drives on USB (flash sticks, external disks) are left out: they
/// are the customer's or the technician's, a failing one can stall these queries for a minute, and the app's start must not wait on them.</summary>
public sealed class WmiDriveHealthProvider(IWmiQuery query) : IDriveHealthProvider
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    public IReadOnlyList<DriveHealth> Read()
    {
        var rows = query.QueryWithRelated(Storage, "SELECT * FROM MSFT_PhysicalDisk WHERE BusType <> 7 OR BusType IS NULL", "MSFT_StorageReliabilityCounter");
        var drives = WmiDriveHealthParser.Parse(rows);
        var smart = SmartBlobs();
        var taken = new HashSet<int>(); var letters = DriveModels.Letters(query);
        return [.. drives.Select((d, i) =>
        {
            // The drive's own figures beat Windows' counter (which reads 0 on many NVMe drives): the NVMe log's "percentage used", and the bad-sector attributes.
            int? used = d.WearPercent;
            if (d.BusType == "NVMe" && rows[i].Row.GetValueOrDefault("DeviceId") is { } id && int.TryParse(Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture), out int number)
                && NvmeHealthReader.ReadLog(number) is { } log) used = log.PercentageUsed;
            int? sectors = null;
            for (int k = 0; k < smart.Count && sectors is null; k++)
                if (!taken.Contains(k) && WmiDriveHealthParser.Same(smart[k].Prod, d.Name)) { taken.Add(k); sectors = SmartHealth.FromAttributes(smart[k].Raw); }
            return d with { WearPercent = SmartHealth.Wear(used, sectors), Letters = rows[i].Row.GetValueOrDefault("DeviceId") is { } did && letters.TryGetValue(Convert.ToString(did, System.Globalization.CultureInfo.InvariantCulture) ?? "", out var l) ? l : null };
        })];
    }

    /// <summary>Each ATA drive's SMART attributes (administrator only; a drive Windows does not expose them for - NVMe, USB - is simply absent).</summary>
    private List<(string Prod, IReadOnlyDictionary<int, ulong> Raw)> SmartBlobs()
    {
        var list = new List<(string, IReadOnlyDictionary<int, ulong>)>();
        try
        {
            foreach (var r in query.Query(@"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData"))
                if (r.GetValueOrDefault("InstanceName") is string name && r.GetValueOrDefault("VendorSpecific") is byte[] blob) list.Add((name, SmartHealth.Parse(blob)));
        }
        catch (Exception) { }   // no SMART access: the drive keeps what Windows reports
        return list;
    }
}

public static class WmiDriveHealthParser
{
    public static IReadOnlyList<DriveHealth> Parse(IReadOnlyList<(IReadOnlyDictionary<string, object?> Row, IReadOnlyDictionary<string, object?>? Related)> rows) => [.. rows.Select(x =>
    {
        var (d, c) = x; bool solid = N(d, "MediaType") == 4 || N(d, "BusType") == 17;
        return new DriveHealth(S(d, "FriendlyName") ?? "?", S(d, "SerialNumber"),
            N(d, "HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", null => null, _ => "Unknown" },
            // Only a drive known to be flash has a wear counter: an HDD - or one Windows lists as "unspecified" behind RAID or VMD - reports 0, which would read as 100 % life.
            solid ? (int?)N(c, "Wear") : null,
            Celsius(c, "Temperature"), Celsius(c, "TemperatureMax"),  // 0 °C means "not reported", never a running drive's temperature; 255 (0xFF, a Samsung 990 Pro's answer) is the same "unknown", not 255 °C
            N(c, "ReadErrorsUncorrected"), N(c, "WriteErrorsUncorrected"), N(c, "PowerOnHours"),
            N(d, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 3 => "ATA", null => null, var b => $"Bus {b}" });
    })];

    /// <summary>Whether a SMART instance name ("SCSI\Disk&amp;Ven_WDC&amp;Prod_WD10EZRX-00A3KB0\...") is the drive of that friendly name ("WDC WD10EZRX-00A3KB0").</summary>
    public static bool Same(string instance, string friendly)
    {
        static string Key(string x) => new([.. x.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
        int at = instance.IndexOf("Prod_", StringComparison.OrdinalIgnoreCase); if (at < 0) return false;
        string prod = instance[(at + 5)..]; int end = prod.IndexOfAny(['\\', '&']); if (end >= 0) prod = prod[..end];
        return Key(prod).Length > 3 && Key(friendly).EndsWith(Key(prod));
    }

    private static string? S(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    private static long? N(IReadOnlyDictionary<string, object?>? r, string k) => r is not null && r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : null;
    private static double? Celsius(IReadOnlyDictionary<string, object?>? r, string k) => N(r, k) is long t && t > 0 && t < Mazesta.Core.Hardware.ReadingValidator.MaxDriveCelsius ? t : null;
}
