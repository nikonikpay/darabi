using Mazesta.Core.Inventory; using RAMSPDToolkit.I2CSMBus; using RAMSPDToolkit.SPD; using RAMSPDToolkit.SPD.Interop.Shared;
namespace Mazesta.Hardware.Details;

/// <summary>
/// Reads every memory module's SPD chip over the SMBus that LibreHardwareMonitor opened (through PawnIO, with RAMSPDToolkit, which LHM itself uses
/// for the DIMM temperatures): addresses 0x50-0x57 are the eight slots' SPD. Read-only - nothing is ever written to a module. DDR4 is decoded in
/// full by <see cref="Ddr4Spd"/>; other types give what the toolkit reads (part number, makers, size) and no profiles, rather than a guess.
/// Empty when no SMBus is open (the sensor driver is not installed, or the board's SMBus is not supported).
/// </summary>
internal static class SpdReader
{
    private static readonly object Lock = new();

    public static IReadOnlyList<SpdModule> Read()
    {
        lock (Lock)
        {
            var modules = new List<SpdModule>();
            foreach (var bus in SMBusManager.RegisteredSMBuses)
                for (byte address = 0x50; address <= 0x57; address++)
                {
                    SPDDetector detector;
                    try { detector = new SPDDetector(bus, address); } catch (Exception) { continue; }
                    if (!detector.IsValid || detector.Accessor is not { } spd) continue;
                    int slot = address - 0x50;
                    string? part = Clean(spd.ModulePartNumber()), maker = Clean(spd.GetModuleManufacturerString()), dram = Clean(spd.GetDRAMManufacturerString());
                    if (detector.SPDMemoryType == SPDMemoryType.SPD_DDR4_SDRAM)
                    {
                        var bytes = new byte[512];
                        for (ushort i = 0; i < bytes.Length; i++) bytes[i] = spd.At(i);
                        spd.At(0);   // back to page 0, where every other reader of this bus expects it
                        modules.Add(Ddr4Spd.Decode(slot, bytes, part, maker, dram));
                    }
                    else
                    {
                        float size = spd.GetCapacity();
                        modules.Add(new(slot, Type(detector.SPDMemoryType), part, maker, dram, size > 0 ? (int)size : null, null, null, null, null, null, null, [], [], null));
                    }
                }
            return modules;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Trim('\0');
    private static string Type(SPDMemoryType t) => t switch
    {
        SPDMemoryType.SPD_DDR5_SDRAM => "DDR5", SPDMemoryType.SPD_DDR3_SDRAM => "DDR3", SPDMemoryType.SPD_LPDDR5_SDRAM => "LPDDR5",
        SPDMemoryType.SPD_LPDDR4_SDRAM or SPDMemoryType.SPD_LPDDR4X_SDRAM => "LPDDR4", _ => t.ToString().Replace("SPD_", "").Replace("_SDRAM", ""),
    };
}
