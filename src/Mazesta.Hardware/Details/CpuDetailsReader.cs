using System.Runtime.InteropServices; using System.Runtime.Intrinsics.X86; using Mazesta.Core.Inventory; using Microsoft.Win32;
namespace Mazesta.Hardware.Details;

/// <summary>
/// The processor details the inventory leaves out: every cache level (GetLogicalProcessorInformationEx, RelationCache), the microcode revision
/// Windows loaded (the registry's "Update Revision"), the family/model/stepping string, and the instruction sets the .NET runtime found usable on
/// this CPU and this Windows (AVX-512 counts only when the OS enables it). Checked against HWiNFO on the owner's Ryzen 9 3950X: caches 16 × 32 KB
/// L1D and L1I, 16 × 512 KB L2, 4 × 16 MB L3, microcode 0x8701030.
/// </summary>
internal static class CpuDetailsReader
{
    public static CpuDetails Read(int? baseClockMhz, int? busClockMhz, bool? virtualization, bool? slat)
    {
        string? identifier = null; uint? microcode = null;
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            identifier = (key?.GetValue("Identifier") as string)?.Trim();
            microcode = Microcode(key?.GetValue("Update Revision") as byte[]);
        }
        return new(identifier, microcode, baseClockMhz, busClockMhz, Caches(), InstructionSets(), virtualization, slat);
    }

    /// <summary>The revision in "Update Revision": AMD keeps it in the first four bytes, Intel in the last four of eight.</summary>
    internal static uint? Microcode(byte[]? raw) => raw switch
    {
        { Length: 8 } when BitConverter.ToUInt32(raw, 4) != 0 => BitConverter.ToUInt32(raw, 4),
        { Length: >= 4 } when BitConverter.ToUInt32(raw, 0) != 0 => BitConverter.ToUInt32(raw, 0),
        _ => null,
    };

    private static IReadOnlyList<CacheInfo> Caches()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationCache, IntPtr.Zero, ref length);
        if (length == 0) return [];
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationCache, buffer, ref length)) return [];
            var all = new List<(int Level, string Kind, long Size, int Ways, int Line)>();
            for (int offset = 0; offset < length;)
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship (4), Size (4), then CACHE_RELATIONSHIP: Level (1), Associativity (1),
                // LineSize (2), CacheSize (4), Type (4: unified, instruction, data, trace).
                var p = buffer + offset;
                int size = Marshal.ReadInt32(p, 4);
                all.Add((Marshal.ReadByte(p, 8), Marshal.ReadInt32(p, 16) switch { 0 => "Unified", 1 => "Instruction", 2 => "Data", _ => "Trace" },
                    (uint)Marshal.ReadInt32(p, 12), Marshal.ReadByte(p, 9), (ushort)Marshal.ReadInt16(p, 10)));
                offset += size;
            }
            return [.. all.GroupBy(c => c).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Kind == "Data" ? 0 : 1)
                .Select(g => new CacheInfo(g.Key.Level, g.Key.Kind, g.Key.Size, g.Count(), g.Key.Ways, g.Key.Line))];
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IReadOnlyList<string> InstructionSets()
    {
        var sets = new List<string>();
        void Add(bool supported, string name) { if (supported) sets.Add(name); }
        Add(Sse.IsSupported, "SSE"); Add(Sse2.IsSupported, "SSE2"); Add(Sse3.IsSupported, "SSE3"); Add(Ssse3.IsSupported, "SSSE3");
        Add(Sse41.IsSupported, "SSE4.1"); Add(Sse42.IsSupported, "SSE4.2"); Add(Popcnt.IsSupported, "POPCNT"); Add(Aes.IsSupported, "AES-NI");
        Add(Pclmulqdq.IsSupported, "PCLMULQDQ"); Add(Avx.IsSupported, "AVX"); Add(Avx2.IsSupported, "AVX2"); Add(Fma.IsSupported, "FMA3");
        Add(Bmi1.IsSupported, "BMI1"); Add(Bmi2.IsSupported, "BMI2"); Add(Lzcnt.IsSupported, "LZCNT"); Add(X86Base.IsSupported && (X86Base.CpuId(7, 0).Ebx & (1 << 29)) != 0, "SHA");   // CPUID.(EAX=7):EBX[29]; .NET has no SHA intrinsics to ask
        Add(AvxVnni.IsSupported, "AVX-VNNI"); Add(Avx512F.IsSupported, "AVX-512F"); Add(Avx512BW.IsSupported, "AVX-512BW");
        Add(Avx512DQ.IsSupported, "AVX-512DQ"); Add(Avx512Vbmi.IsSupported, "AVX-512VBMI");
        return sets;
    }

    private const int RelationCache = 2;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
