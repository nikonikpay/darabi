using Mazesta.Core.Inventory;
namespace Mazesta.Hardware.Wmi;

/// <summary>
/// Reads the batteries from the ACPI battery classes in root\wmi (BatteryStaticData, BatteryFullChargedCapacity, BatteryCycleCount, BatteryStatus),
/// joined by their instance name. A desktop has none: the list is empty. A class the firmware does not fill leaves its figures null; a cycle
/// count of 0 is what a controller that does not count reports, so it is left out as well.
/// </summary>
public static class BatteryReader
{
    private const string Scope = @"root\wmi";

    public static IReadOnlyList<BatteryInfo> Read(IWmiQuery wmi)
    {
        var statics = Rows(wmi, "SELECT InstanceName,DesignedCapacity,DeviceName,ManufactureName FROM BatteryStaticData");
        var full = Rows(wmi, "SELECT InstanceName,FullChargedCapacity FROM BatteryFullChargedCapacity");
        var cycles = Rows(wmi, "SELECT InstanceName,CycleCount FROM BatteryCycleCount");
        var status = Rows(wmi, "SELECT InstanceName,RemainingCapacity,ChargeRate,DischargeRate,Voltage,PowerOnline,Charging,Discharging FROM BatteryStatus");
        var names = statics.Keys.Concat(full.Keys).Concat(status.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return [.. names.Select(n =>
        {
            var s = statics.GetValueOrDefault(n); var f = full.GetValueOrDefault(n); var c = cycles.GetValueOrDefault(n); var st = status.GetValueOrDefault(n);
            bool? charging = Flag(st, "Charging"), discharging = Flag(st, "Discharging");
            long? rate = discharging == true ? Positive(st, "DischargeRate") : charging == true ? Positive(st, "ChargeRate") : null;
            return new BatteryInfo(Text(s, "DeviceName"), Text(s, "ManufactureName"), Positive(s, "DesignedCapacity"), Positive(f, "FullChargedCapacity"), Number(st, "RemainingCapacity"),
                Positive(c, "CycleCount") is { } n0 ? (int)n0 : null, charging, discharging, Flag(st, "PowerOnline"), rate, Positive(st, "Voltage"));
        })];
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> Rows(IWmiQuery wmi, string wql)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var r in wmi.Query(Scope, wql)) if (r.TryGetValue("InstanceName", out var n) && n is string name) map[name] = r; }
        catch (Exception e) when (e is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }   // the class is not there: its figures stay null
        return map;
    }

    private static long? Number(IReadOnlyDictionary<string, object?>? row, string name)
        => row is not null && row.TryGetValue(name, out var v) && v is not null && v is IConvertible ? Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : null;
    private static long? Positive(IReadOnlyDictionary<string, object?>? row, string name) => Number(row, name) is > 0 and var n ? n : null;
    private static bool? Flag(IReadOnlyDictionary<string, object?>? row, string name) => row is not null && row.TryGetValue(name, out var v) && v is bool b ? b : null;
    private static string? Text(IReadOnlyDictionary<string, object?>? row, string name) => row is not null && row.TryGetValue(name, out var v) && v is string s && s.Trim().Length > 0 ? s.Trim() : null;
}
