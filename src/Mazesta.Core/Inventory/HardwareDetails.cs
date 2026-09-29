namespace Mazesta.Core.Inventory;

/// <summary>One cache level as Windows reports it: <see cref="Count"/> caches of <see cref="SizeBytes"/> each (16 × 32 KB, 4 × 16 MB).</summary>
public sealed record CacheInfo(int Level, string Kind, long SizeBytes, int Count, int Ways, int LineBytes);

/// <summary>What Windows and the CPU itself report beyond the basic inventory: the caches, the family/model/stepping, the microcode Windows
/// loaded, and the instruction sets the runtime found usable. Nothing is looked up by model name.</summary>
public sealed record CpuDetails(string? Identifier, uint? Microcode, int? BaseClockMhz, int? BusClockMhz, IReadOnlyList<CacheInfo> Caches,
    IReadOnlyList<string> InstructionSets, bool? VirtualizationEnabled, bool? Slat);

/// <summary>A PCI Express link as the device reports it: generation (1 = 2.5 GT/s … 5 = 32 GT/s) and lanes, now and at most. "Now" is the
/// moment of reading: a card at idle may drop to a lower generation to save power.</summary>
public sealed record PciLinkInfo(int? CurrentGen, int? CurrentWidth, int? MaxGen, int? MaxWidth)
{
    public static double? GtPerSecond(int? gen) => gen switch { 1 => 2.5, 2 => 5, 3 => 8, 4 => 16, 5 => 32, 6 => 64, _ => null };
}

/// <summary>A graphics card's details: the board maker from the PCI subsystem vendor, the link from Windows, and (NVIDIA) what NVML reports.</summary>
public sealed record GpuDetails(string? PnpDeviceId, string? BoardVendor, string? SubsystemId, PciLinkInfo? Link, string? Vbios, int? BusWidthBits, int? Cores,
    string? Architecture, string? ComputeCapability, long? Bar1Bytes, int? MaxCoreClockMhz, int? MaxMemoryClockMhz, int? PowerDefaultW, int? PowerMaxW, string? PciBusId);

/// <summary>Boot and security state as Windows reports it; null where it could not be read.</summary>
public sealed record PlatformSecurity(bool? UefiBoot, bool? SecureBoot, string? TpmVersion, bool? TpmEnabled, bool? VbsRunning, bool? HvciRunning);

/// <summary>A drive's link (NVMe drives: their controller's PCIe link) and the counters its SMART log keeps.</summary>
public sealed record DriveDetails(string? Serial, string? Name, PciLinkInfo? Link, long? PowerOnHours, double? TemperatureC, double? TemperatureMaxC,
    long? ReadErrorsUncorrected, long? WriteErrorsUncorrected, int? WearPercent);

/// <summary>A network adapter's PCIe link and driver, where it has them.</summary>
public sealed record NicDetails(string? Name, PciLinkInfo? Link, string? Driver);

/// <summary>Everything the specification pages show beyond <see cref="HardwareInventory"/>. Read once the sensor driver is up (the SPD chips are on
/// the SMBus it opens). A part that could not be read is empty, with the reason in <see cref="Errors"/>.</summary>
public sealed record HardwareDetails(CpuDetails? Cpu, IReadOnlyList<GpuDetails> Gpus, IReadOnlyList<SpdModule> Spd, PlatformSecurity? Security,
    IReadOnlyList<DriveDetails> Drives, IReadOnlyList<NicDetails> Nics, IReadOnlyList<string> Errors)
{
    public static readonly HardwareDetails Empty = new(null, [], [], null, [], [], []);
}

/// <summary>Board makers by PCI subsystem vendor ID (PCI-SIG assignments), for naming who built a graphics card. An ID not listed is shown as
/// its number, never guessed.</summary>
public static class PciVendors
{
    private static readonly Dictionary<ushort, string> Names = new()
    {
        [0x10DE] = "NVIDIA", [0x1002] = "AMD", [0x8086] = "Intel", [0x1043] = "ASUS", [0x1458] = "Gigabyte", [0x1462] = "MSI", [0x3842] = "EVGA",
        [0x196E] = "PNY", [0x19DA] = "Zotac", [0x1569] = "Palit", [0x1682] = "XFX", [0x1DA2] = "Sapphire", [0x148C] = "PowerColor", [0x1849] = "ASRock",
        [0x1028] = "Dell", [0x103C] = "HP", [0x17AA] = "Lenovo",
    };

    public static string? Name(ushort id) => Names.GetValueOrDefault(id);

    /// <summary>The subsystem (board) vendor and device from a PnP id such as PCI\VEN_10DE&amp;DEV_2204&amp;SUBSYS_136A196E&amp;REV_A1\…: SUBSYS is device then vendor.</summary>
    public static (ushort Vendor, ushort Device)? Subsystem(string? pnpDeviceId)
    {
        int at = pnpDeviceId?.IndexOf("SUBSYS_", StringComparison.OrdinalIgnoreCase) ?? -1;
        if (at < 0 || pnpDeviceId!.Length < at + 15) return null;
        string hex = pnpDeviceId.Substring(at + 7, 8);
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? ((ushort)(v & 0xFFFF), (ushort)(v >> 16)) : null;
    }
}
