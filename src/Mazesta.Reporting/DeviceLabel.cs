using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

/// <summary>The device's maker and model as a report names it, for the places outside the report (the site's list).</summary>
public static class DeviceLabel
{
    public static string? Of(HardwareInventory machine) => ReportFormat.DeviceName(machine);
}
