using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>One physical core: its processor group, the logical processors (SMT threads) it runs, and its efficiency class (on a hybrid
/// Intel CPU the performance cores have the higher class; elsewhere every core is 0).</summary>
public sealed record CpuCore(int Index, ushort Group, ulong Mask, byte EfficiencyClass)
{
    public int Threads => System.Numerics.BitOperations.PopCount(Mask);
    /// <summary>The core's first logical processor alone, so a pinned thread never shares the core with its SMT sibling's work.</summary>
    public ulong FirstThreadMask => Mask & (~Mask + 1);
}

/// <summary>
/// The physical cores Windows reports (GetLogicalProcessorInformationEx, RelationProcessorCore) and pinning the calling thread to one of
/// them. Groups are honoured, so a machine with more than 64 logical processors is covered too.
/// </summary>
public static class CpuTopology
{
    private static readonly Lazy<IReadOnlyList<CpuCore>> Cached = new(Read);
    public static IReadOnlyList<CpuCore> Cores => Cached.Value;

    private static IReadOnlyList<CpuCore> Read()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
        if (length == 0) return Fallback();
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length)) return Fallback();
            var cores = new List<CpuCore>();
            for (int offset = 0; offset < length;)
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship (4), Size (4), then PROCESSOR_RELATIONSHIP:
                // Flags (1), EfficiencyClass (1), Reserved (20), GroupCount (2), GROUP_AFFINITY[] (Mask 8, Group 2, Reserved 6).
                var p = buffer + offset;
                int size = Marshal.ReadInt32(p, 4);
                byte efficiency = Marshal.ReadByte(p, 9);
                ushort groups = (ushort)Marshal.ReadInt16(p, 30);
                for (int g = 0; g < groups; g++)
                {
                    ulong mask = (ulong)Marshal.ReadInt64(p, 32 + g * 16); ushort group = (ushort)Marshal.ReadInt16(p, 40 + g * 16);
                    if (mask != 0) cores.Add(new(cores.Count, group, mask, efficiency));
                }
                offset += size;
            }
            return cores.Count > 0 ? cores : Fallback();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>When Windows cannot say: every logical processor of group 0 as its own core, which is still a valid place to pin a thread.</summary>
    private static List<CpuCore> Fallback() => [.. Enumerable.Range(0, Math.Min(64, Environment.ProcessorCount)).Select(i => new CpuCore(i, 0, 1UL << i, 0))];

    /// <summary>Pins the calling OS thread to <paramref name="mask"/> in <paramref name="group"/>; false when Windows refused. The caller must be
    /// on a dedicated thread (not a pool thread), since the pin outlives the work.</summary>
    public static bool Pin(ushort group, ulong mask)
    {
        var affinity = new GroupAffinity { Mask = (nuint)mask, Group = group };
        return SetThreadGroupAffinity(GetCurrentThread(), ref affinity, out _);
    }

    private const int RelationProcessorCore = 0;
    [StructLayout(LayoutKind.Sequential)] private struct GroupAffinity { public nuint Mask; public ushort Group; public ushort R0, R1, R2; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadGroupAffinity(IntPtr thread, ref GroupAffinity affinity, out GroupAffinity previous);
}
