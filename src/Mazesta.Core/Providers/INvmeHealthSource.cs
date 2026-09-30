using System.Buffers.Binary;
namespace Mazesta.Core.Providers;

/// <summary>
/// An NVMe drive's own SMART / Health Information log (log page 02h, NVMe base specification 5.16.1.3), as the drive reports it - more than
/// Windows' reliability counters carry: the critical-warning bits, the spare capacity against its threshold and the drive's own count of
/// media (unrecovered data integrity) errors and error-log entries. The 128-bit counters are read as their low 64 bits.
/// </summary>
public sealed record NvmeHealthLog(byte CriticalWarning, double? TemperatureC, int AvailableSparePercent, int SpareThresholdPercent, int PercentageUsed,
    ulong DataUnitsRead, ulong DataUnitsWritten, ulong PowerCycles, ulong PowerOnHours, ulong UnsafeShutdowns, ulong MediaErrors, ulong ErrorLogEntries)
{
    public const int Size = 512;

    public static NvmeHealthLog Parse(ReadOnlySpan<byte> page)
    {
        if (page.Length < Size) throw new ArgumentException($"An NVMe health log is {Size} bytes; got {page.Length}.", nameof(page));
        static ulong U64(ReadOnlySpan<byte> p, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(p[offset..]);
        int kelvin = BinaryPrimitives.ReadUInt16LittleEndian(page[1..]);
        return new(page[0], kelvin == 0 ? null : kelvin - 273.15, page[3], page[4], page[5],
            U64(page, 32), U64(page, 48), U64(page, 112), U64(page, 128), U64(page, 144), U64(page, 160), U64(page, 176));
    }

    /// <summary>Data units are thousands of 512-byte blocks.</summary>
    public double BytesWritten => DataUnitsWritten * 512_000.0;
    public double BytesRead => DataUnitsRead * 512_000.0;

    /// <summary>The critical-warning bits that are set, by name (NVMe: spare, temperature, reliability, read-only, volatile backup, PMR).</summary>
    public IReadOnlyList<string> Warnings => [.. Names.Where((_, bit) => (CriticalWarning & (1 << bit)) != 0)];
    private static readonly string[] Names = ["available spare below threshold", "temperature beyond a threshold", "reliability degraded by media or internal errors",
        "media placed in read-only mode", "volatile memory backup failed", "persistent memory region read-only"];
}

/// <summary>One NVMe drive's health log, with the Windows disk number it was read through.</summary>
public sealed record NvmeDriveHealth(int DiskNumber, string? Model, string? Serial, NvmeHealthLog Log);

/// <summary>Reads every NVMe drive's health log; a drive that does not answer is left out, never reported as healthy.</summary>
public interface INvmeHealthSource { IReadOnlyList<NvmeDriveHealth> Read(); }
