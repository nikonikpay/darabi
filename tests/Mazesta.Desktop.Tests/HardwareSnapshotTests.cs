using System.Text.Json; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Desktop.Composition; using Xunit;
namespace Mazesta.Desktop.Tests;

public class HardwareSnapshotTests
{
    // A stored read that loses a field on the way back would show "not available" for something the machine has: every value must survive.
    [Fact] public void A_stored_read_comes_back_whole()
    {
        var inv = HardwareInventory.Empty with
        {
            Cpu = new("AMD Ryzen 7 5800X", HardwareVendor.Amd, 8, 16, 3800, "AM4"), Gpus = [new("RTX 3080", "32.0.15.6094", 10L << 30, @"PCI\VEN_10DE&DEV_2206&SUBSYS_38971462")],
            MemoryModules = [new("DIMM A1", 32L << 30, "G Skill Intl", "F4-4000C18-32GTZR", 4000, 2133, "BANK 1", 2, 1400, 26, 64, 64)], TotalPhysicalMemoryBytes = 64L << 30,
            Storage = [new("Samsung 980 PRO", "S5GX", "SSD", "NVMe", 1_000_204_886_016, "5B2QGXA7", "Healthy", 3)], NetworkAdapters = [new("Intel I225-V", "00-11-22-33-44-55", ["192.168.1.5"], 2_500_000_000, true)],
            Errors = ["bios: access denied"],
        };
        var details = HardwareDetails.Empty with
        {
            Cpu = new("AMD64 Family 25 Model 33 Stepping 0", 0xA201205, 3800, 100, [new(1, "Data", 32768, 8, 8, 64)], ["AVX2"], true, true),
            Spd = [new(2, "DDR4", "F4-4000C18-32GTZR", "G Skill Intl", "SK Hynix", 32, 2, 8, 16, 4, false, "2021-W07", [new("JEDEC 2133", 2133, 15, 15, 15, 35, 49, 1.2)], [new("XMP 1", 4000, 18, 22, 22, 42, 64, 1.4)], "2.0")],
            Drives = [new("S5GX", "Samsung 980 PRO", new(4, 4, 4, 4), 1234, 41.5, 70, 0, 0, 3, new NvmeHealthLog(0, 41.85, 100, 10, 3, 1, 2, 3, 1234, 5, 0, 7))],
            Security = new(true, true, "2.0", true, false, null), Errors = ["spd: no bus"],
        };
        string json = JsonSerializer.Serialize(new HardwareSnapshot.Stored("key", DateTimeOffset.UnixEpoch, inv, details), HardwareSnapshot.Json);
        var back = JsonSerializer.Deserialize<HardwareSnapshot.Stored>(json, HardwareSnapshot.Json)!;
        Assert.Equal("key", back.Key);
        Assert.Equal(json, JsonSerializer.Serialize(back, HardwareSnapshot.Json));
    }
}
