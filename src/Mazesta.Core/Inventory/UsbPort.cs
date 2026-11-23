using System.Text.RegularExpressions;
namespace Mazesta.Core.Inventory;

/// <summary>
/// Where a USB device sits, from the LocationInformation Windows keeps for it ("Port_#0003.Hub_#0001"): the port of the hub it is plugged into.
/// Only devices on a physical port carry it; an interface of a composite device ("&amp;MI_01") or a root hub is not a device in a port.
/// </summary>
public static partial class UsbPort
{
    [GeneratedRegex(@"Port_#(\d+)\.Hub_#(\d+)", RegexOptions.IgnoreCase)] private static partial Regex Location();

    public static (int Port, int Hub)? Parse(string? location)
    {
        var m = Location().Match(location ?? "");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    /// <summary>A device plugged into a port: a USB\VID_ id, not an interface of one, not a root hub.</summary>
    public static bool IsPlugged(string? pnpId) =>
        pnpId is not null && pnpId.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) && !pnpId.Contains("&MI_", StringComparison.OrdinalIgnoreCase);
}
