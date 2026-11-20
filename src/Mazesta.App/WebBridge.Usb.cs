using Mazesta.Core.Inventory; using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Win32;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The USB devices plugged in now, each with the port and hub Windows places it on, for the hands-on port check: the page compares two
    /// readings, and a port where a new device appeared is one that works. Read only while the check runs.
    /// </summary>
    private void RegisterUsb()
    {
        var wmi = _sp.GetRequiredService<IWmiQuery>();
        MethodAsync("usb.read", async _ => await Task.Run(() => wmi.Query(@"root\cimv2", "SELECT DeviceID,Name,PNPClass,Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%'")
            .Select(r => (id: r["DeviceID"] as string, name: r["Name"] as string, cls: r["PNPClass"] as string, ok: r["Status"] as string == "OK"))
            .Where(d => UsbPort.IsPlugged(d.id))
            .Select(d =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + d.id);
                var at = UsbPort.Parse(key?.GetValue("LocationInformation") as string);
                return new { d.id, d.name, d.cls, d.ok, port = at?.Port, hub = at?.Hub };
            }).ToList()).ConfigureAwait(true));
    }
}
