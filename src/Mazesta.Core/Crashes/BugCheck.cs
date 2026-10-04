using System.Buffers.Binary; using System.Globalization; using System.Text.RegularExpressions;
namespace Mazesta.Core.Crashes;

/// <summary>What usually lies behind a stop code, most likely first. These are the usual suspects Microsoft's bug check reference names for the
/// code, not a finding about this computer: the page says "likely", and what is to be checked.</summary>
public enum CrashCause { Driver, GpuDriver, Gpu, Ram, Storage, StorageCable, Cpu, Overclock, Heat, Psu, Bios, SystemFiles, Software, Device, Manual }

/// <summary>A blue screen as Windows recorded it. <see cref="At"/> is the crash itself when the dump's header gave it, else the restart that
/// followed (<see cref="AtIsRestart"/>). The parameters are null when only the code survived.</summary>
public sealed record CrashRecord(DateTimeOffset At, bool AtIsRestart, uint Code, IReadOnlyList<ulong>? Parameters, string? DumpFile, TimeSpan? Uptime);

/// <summary>A restart Windows did not see coming, with no stop code behind it (Kernel-Power 41 with code 0): the power went, the reset button
/// was pressed, or the machine hung so hard that nothing could be written.</summary>
public sealed record PowerLoss(DateTimeOffset At);

public sealed record BugCheckInfo(uint Code, string Name, IReadOnlyList<CrashCause> Causes);

/// <summary>The stop codes a service shop meets, by Microsoft's own names, with their usual causes and what their parameters say.</summary>
public static partial class BugCheckCatalog
{
    private static BugCheckInfo B(uint code, string name, params CrashCause[] causes) => new(code, name, causes);
    private const CrashCause Driver = CrashCause.Driver, GpuDriver = CrashCause.GpuDriver, Gpu = CrashCause.Gpu, Ram = CrashCause.Ram, Storage = CrashCause.Storage,
        Cable = CrashCause.StorageCable, Cpu = CrashCause.Cpu, Oc = CrashCause.Overclock, Heat = CrashCause.Heat, Psu = CrashCause.Psu, Bios = CrashCause.Bios,
        Files = CrashCause.SystemFiles, Soft = CrashCause.Software, Device = CrashCause.Device;

    public static IReadOnlyList<BugCheckInfo> All { get; } =
    [
        B(0x0A, "IRQL_NOT_LESS_OR_EQUAL", Driver, Ram, Oc),
        B(0x18, "REFERENCE_BY_POINTER", Driver),
        B(0x19, "BAD_POOL_HEADER", Driver, Ram),
        B(0x1A, "MEMORY_MANAGEMENT", Ram, Oc, Driver),
        B(0x1E, "KMODE_EXCEPTION_NOT_HANDLED", Driver, Ram, Bios),
        B(0x23, "FAT_FILE_SYSTEM", Storage, Files),
        B(0x24, "NTFS_FILE_SYSTEM", Storage, Files, Cable),
        B(0x3B, "SYSTEM_SERVICE_EXCEPTION", Driver, GpuDriver, Files),
        B(0x3F, "NO_MORE_SYSTEM_PTES", Driver),
        B(0x44, "MULTIPLE_IRP_COMPLETE_REQUESTS", Driver),
        B(0x4A, "IRQL_GT_ZERO_AT_SYSTEM_SERVICE", Driver),
        B(0x4E, "PFN_LIST_CORRUPT", Ram, Driver, Oc),
        B(0x50, "PAGE_FAULT_IN_NONPAGED_AREA", Ram, Driver, Soft, Storage),
        B(0x51, "REGISTRY_ERROR", Storage, Files),
        B(0x74, "BAD_SYSTEM_CONFIG_INFO", Files, Ram, Storage),
        B(0x77, "KERNEL_STACK_INPAGE_ERROR", Storage, Cable, Ram),
        B(0x7A, "KERNEL_DATA_INPAGE_ERROR", Storage, Cable, Ram),
        B(0x7B, "INACCESSIBLE_BOOT_DEVICE", Storage, Bios, Driver, Cable),
        B(0x7C, "BUGCODE_NDIS_DRIVER", Driver),
        B(0x7E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", Driver, GpuDriver, Ram),
        B(0x7F, "UNEXPECTED_KERNEL_MODE_TRAP", Ram, Cpu, Oc, Driver),
        B(0x8E, "KERNEL_MODE_EXCEPTION_NOT_HANDLED", Driver, Ram),
        B(0x9C, "MACHINE_CHECK_EXCEPTION", Cpu, Oc, Heat, Psu),
        B(0x9F, "DRIVER_POWER_STATE_FAILURE", Driver, Device),
        B(0xA0, "INTERNAL_POWER_ERROR", Driver, Storage),
        B(0xA5, "ACPI_BIOS_ERROR", Bios),
        B(0xBE, "ATTEMPTED_WRITE_TO_READONLY_MEMORY", Driver, Ram),
        B(0xC1, "SPECIAL_POOL_DETECTED_MEMORY_CORRUPTION", Driver),
        B(0xC2, "BAD_POOL_CALLER", Driver),
        B(0xC4, "DRIVER_VERIFIER_DETECTED_VIOLATION", Driver),
        B(0xC5, "DRIVER_CORRUPTED_EXPOOL", Driver, Ram),
        B(0xCA, "PNP_DETECTED_FATAL_ERROR", Driver, Device),
        B(0xD1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL", Driver, Ram),
        B(0xE2, "MANUALLY_INITIATED_CRASH", CrashCause.Manual),
        B(0xEF, "CRITICAL_PROCESS_DIED", Files, Storage, Driver, Ram),
        B(0xF4, "CRITICAL_OBJECT_TERMINATION", Storage, Files, Cable),
        B(0xFC, "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", Driver, Ram),
        B(0xFE, "BUGCODE_USB_DRIVER", Device, Driver),
        B(0x101, "CLOCK_WATCHDOG_TIMEOUT", Cpu, Oc, Bios, Heat),
        B(0x109, "CRITICAL_STRUCTURE_CORRUPTION", Ram, Driver),
        B(0x10E, "VIDEO_MEMORY_MANAGEMENT_INTERNAL", GpuDriver, Gpu),
        B(0x113, "VIDEO_DXGKRNL_FATAL_ERROR", GpuDriver, Gpu),
        B(0x116, "VIDEO_TDR_FAILURE", GpuDriver, Gpu, Heat, Psu),
        B(0x119, "VIDEO_SCHEDULER_INTERNAL_ERROR", GpuDriver, Gpu),
        B(0x124, "WHEA_UNCORRECTABLE_ERROR", Cpu, Oc, Heat, Psu, Ram, Bios),
        B(0x12B, "FAULTY_HARDWARE_CORRUPTED_PAGE", Ram, Storage),
        B(0x133, "DPC_WATCHDOG_VIOLATION", Driver, Storage, Bios),
        B(0x139, "KERNEL_SECURITY_CHECK_FAILURE", Driver, Ram),
        B(0x13A, "KERNEL_MODE_HEAP_CORRUPTION", Driver, Ram),
        B(0x141, "VIDEO_ENGINE_TIMEOUT_DETECTED", GpuDriver, Gpu, Heat),
        B(0x144, "BUGCODE_USB3_DRIVER", Device, Driver),
        B(0x154, "UNEXPECTED_STORE_EXCEPTION", Storage, Soft, Ram),
        B(0x1C7, "STORE_DATA_STRUCTURE_CORRUPTION", Ram, Storage),
        B(0x1C8, "MANUALLY_INITIATED_POWER_BUTTON_HOLD", CrashCause.Manual),
        B(0xC000021A, "WINLOGON_FATAL_ERROR", Files, Soft, Storage),
        B(0xC0000221, "STATUS_IMAGE_CHECKSUM_MISMATCH", Files, Storage, Ram),
    ];

    /// <summary>The code's entry. A code written with the 0x1000_0000 mark (0x1000007E: the same stop, reported without its full detail) is its
    /// plain one. A code the list does not hold has none: the page shows the number alone.</summary>
    public static BugCheckInfo? Find(uint code)
        => All.FirstOrDefault(b => b.Code == code) ?? ((code & 0xF0000000) == 0x10000000 ? All.FirstOrDefault(b => b.Code == (code & 0x0FFFFFFF)) : null);

    /// <summary>What the parameters of this crash say, as string keys (Bsod_Note_…): only what the reference states for these values.</summary>
    public static IReadOnlyList<string> Notes(uint code, IReadOnlyList<ulong>? p)
    {
        if (p is not { Count: >= 4 }) return [];
        uint c = Find(code)?.Code ?? code; var notes = new List<string>();
        switch (c)
        {
            case 0x124:
                notes.Add(p[0] switch { 0 => "Bsod_Note_Whea_Mce", 4 => "Bsod_Note_Whea_Pcie", 1 or 2 => "Bsod_Note_Whea_Corrected", 3 => "Bsod_Note_Whea_Nmi", _ => "Bsod_Note_Whea_Other" }); break;
            case 0x7A or 0x77:
                // The second parameter (the first, for the stack's page) is the I/O status that failed the read.
                switch ((uint)(c == 0x7A ? p[1] : p[0]))
                {
                    case 0xC000009C or 0xC000016A: notes.Add("Bsod_Note_Io_BadBlocks"); break;
                    case 0xC000009D or 0xC0000185 or 0xC000000E: notes.Add("Bsod_Note_Io_Cable"); break;
                    case 0xC000009A: notes.Add("Bsod_Note_Io_Resources"); break;
                }
                break;
            case 0x1A:
                if (p[0] is 0x41790 or 0x41792 or 0x41284 or 0x403 or 0x41201) notes.Add("Bsod_Note_Mm_PageTable"); break;
            case 0x133:
                notes.Add(p[0] == 0 ? "Bsod_Note_Dpc_Single" : "Bsod_Note_Dpc_Total"); break;
            case 0x7F:
                if (p[0] == 8) notes.Add("Bsod_Note_Trap_DoubleFault"); else if (p[0] is 0 or 6 or 0xD) notes.Add("Bsod_Note_Trap_Cpu"); break;
            case 0x9F:
                if (p[0] == 3) notes.Add("Bsod_Note_Power_Device"); else if (p[0] == 4) notes.Add("Bsod_Note_Power_Pnp"); break;
            case 0x1E or 0x7E or 0x8E or 0x3B:
                if ((uint)p[0] == 0xC0000005) notes.Add("Bsod_Note_AccessViolation"); else if ((uint)p[0] == 0xC000001D) notes.Add("Bsod_Note_IllegalInstruction"); break;
            case 0x139:
                if (p[0] == 3) notes.Add("Bsod_Note_Sec_List"); else if (p[0] == 2) notes.Add("Bsod_Note_Sec_Stack"); break;
            case 0x101:
                notes.Add("Bsod_Note_Clock_Core"); break;
        }
        return notes;
    }

    /// <summary>The codes whose four parameters the page names (Bsod_Params_XX), so a technician reads them without the reference open.</summary>
    public static string? ParametersKey(uint code)
        => (Find(code)?.Code ?? code) switch
        {
            0x0A or 0xD1 => "Bsod_Params_Irql", 0x50 => "Bsod_Params_PageFault", 0x124 => "Bsod_Params_Whea", 0x116 => "Bsod_Params_Tdr", 0x7A => "Bsod_Params_Inpage",
            0x1E or 0x7E or 0x8E => "Bsod_Params_Exception", 0x3B => "Bsod_Params_Service", 0x9F => "Bsod_Params_Power", 0x101 => "Bsod_Params_Clock", 0x133 => "Bsod_Params_Dpc",
            0xEF => "Bsod_Params_Process", 0x1A => "Bsod_Params_Mm", 0x7F => "Bsod_Params_Trap", 0x139 => "Bsod_Params_Security", _ => null,
        };

    /// <summary>Windows' own line in the System log: "0x0000009f (0x0000000000000003, 0x…, 0x…, 0x…)". The code alone when the rest is missing.</summary>
    public static (uint Code, IReadOnlyList<ulong>? Parameters)? ParseLogText(string? text)
    {
        if (text is null || LogLine().Match(text) is not { Success: true } m) return null;
        if (!uint.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint code)) return null;
        var ps = new List<ulong>();
        foreach (Capture x in m.Groups[2].Captures) if (ulong.TryParse(x.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v)) ps.Add(v);
        return (code, ps.Count == 4 ? ps : null);
    }
    [GeneratedRegex(@"0x([0-9a-fA-F]{1,8})\b(?:\s*\(\s*(?:0x([0-9a-fA-F]{1,16})\s*,?\s*){1,4}\))?")] private static partial Regex LogLine();

    /// <summary>
    /// The head of a kernel dump (a minidump of C:\Windows\Minidump, or MEMORY.DMP): the stop code and its four parameters, and from a 64-bit
    /// dump the time of the crash and how long Windows had been up. Null for anything that is not such a file. The time and the uptime are
    /// given only when they read as sane (a date between 2000 and <paramref name="notAfter"/>, less than a year of uptime).
    /// </summary>
    public static CrashRecord? ReadDump(ReadOnlySpan<byte> head, string? file, DateTimeOffset fileTime, DateTimeOffset notAfter)
    {
        if (head.Length < 0x60 || !head[..4].SequenceEqual("PAGE"u8)) return null;
        if (head.Slice(4, 4).SequenceEqual("DU64"u8))
        {
            uint code = BinaryPrimitives.ReadUInt32LittleEndian(head[0x38..]);
            ulong[] p = new ulong[4];
            for (int i = 0; i < 4; i++) p[i] = BinaryPrimitives.ReadUInt64LittleEndian(head[(0x40 + 8 * i)..]);
            DateTimeOffset? at = null; TimeSpan? up = null;
            if (head.Length >= 0x1038)
            {
                long time = BinaryPrimitives.ReadInt64LittleEndian(head[0xFA8..]), ticks = BinaryPrimitives.ReadInt64LittleEndian(head[0x1030..]);
                if (time > 0 && time < DateTime.MaxValue.Ticks - 504911232000000000)
                {
                    var t = new DateTimeOffset(DateTime.FromFileTimeUtc(time));
                    if (t.Year >= 2000 && t <= notAfter.AddDays(1)) at = t;
                }
                if (ticks > 0 && ticks < TimeSpan.FromDays(366).Ticks) up = TimeSpan.FromTicks(ticks);
            }
            return new(at ?? fileTime, at is null, code, p, file, up);
        }
        if (head.Slice(4, 4).SequenceEqual("DUMP"u8))
        {
            uint code = BinaryPrimitives.ReadUInt32LittleEndian(head[0x20..]);
            ulong[] p = new ulong[4];
            for (int i = 0; i < 4; i++) p[i] = BinaryPrimitives.ReadUInt32LittleEndian(head[(0x24 + 4 * i)..]);
            return new(fileTime, true, code, p, file, null);
        }
        return null;
    }

    /// <summary>The dumps and the log's lines as one list, newest first: a crash that left both a dump and a line is listed once (by its dump,
    /// which holds the time of the crash itself).</summary>
    public static IReadOnlyList<CrashRecord> Merge(IEnumerable<CrashRecord> dumps, IEnumerable<CrashRecord> logged)
    {
        var all = dumps.ToList();
        foreach (var l in logged)
        {
            bool have = all.Any(d => d.Code == l.Code && (l.DumpFile is not null && d.DumpFile is not null
                && string.Equals(Path.GetFileName(d.DumpFile), Path.GetFileName(l.DumpFile), StringComparison.OrdinalIgnoreCase)
                // The line is written at the restart after the crash: within a day of the dump's own time, with the same code and parameters.
                || l.At >= d.At.AddMinutes(-5) && l.At - d.At < TimeSpan.FromDays(1) && (l.Parameters is null || d.Parameters is null || l.Parameters.SequenceEqual(d.Parameters))));
            if (!have) all.Add(l);
        }
        return [.. all.OrderByDescending(c => c.At)];
    }
}
