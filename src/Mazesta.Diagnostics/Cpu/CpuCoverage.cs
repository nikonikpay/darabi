using System.Numerics; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Cpu;

/// <summary>
/// Which logical processors a test's threads actually ran on. Starting as many threads as there are logical processors does not mean each
/// one was loaded: Windows places threads where it likes, parks cores, and on machines with more than 64 logical processors keeps a process in
/// processor groups. Each worker marks the processor it is on (GetCurrentProcessorNumberEx) after every block of work, and the report names
/// how many of the machine's logical processors - performance and efficiency cores apart on a hybrid CPU, processor groups apart where there
/// are several - were seen: a coverage with a real denominator, never assumed.
/// </summary>
public sealed class CpuCoverage
{
    private readonly ulong[] _seen = new ulong[16];   // 16 groups of 64: the Windows maximum

    public void Mark()
    {
        GetCurrentProcessorNumberEx(out var p);
        if (p.Group < _seen.Length) Interlocked.Or(ref _seen[p.Group], 1UL << p.Number);
    }

    internal void Mark(int group, int number) => Interlocked.Or(ref _seen[group], 1UL << number);

    /// <summary>"ran on 30 of 32 logical processors (P-cores 16/16, E-cores 14/16; groups 0: 32/32)"; the P/E split only on a hybrid CPU,
    /// the groups only when there are several.</summary>
    public string Describe(IReadOnlyList<CpuCore> cores)
    {
        int Seen(CpuCore c) => BitOperations.PopCount(_seen[c.Group] & c.Mask);
        int total = cores.Sum(c => BitOperations.PopCount(c.Mask)), seen = cores.Sum(Seen);
        var parts = new List<string>();
        if (cores.Select(c => c.EfficiencyClass).Distinct().Count() > 1)
        {
            byte top = cores.Max(c => c.EfficiencyClass);
            foreach (var (name, pick) in new[] { ("P-cores", (Func<CpuCore, bool>)(c => c.EfficiencyClass == top)), ("E-cores", c => c.EfficiencyClass != top) })
                parts.Add($"{name} {cores.Where(pick).Sum(Seen)}/{cores.Where(pick).Sum(c => BitOperations.PopCount(c.Mask))} threads");
        }
        var groups = cores.GroupBy(c => c.Group).OrderBy(g => g.Key).ToList();
        if (groups.Count > 1) parts.Add("groups " + string.Join(", ", groups.Select(g => $"{g.Key}: {g.Sum(Seen)}/{g.Sum(c => BitOperations.PopCount(c.Mask))}")));
        return $"ran on {seen} of {total} logical processors" + (parts.Count > 0 ? $" ({string.Join("; ", parts)})" : "");
    }

    [StructLayout(LayoutKind.Sequential)] private struct ProcessorNumber { public ushort Group; public byte Number; public byte Reserved; }
    [DllImport("kernel32.dll")] private static extern void GetCurrentProcessorNumberEx(out ProcessorNumber number);
}
