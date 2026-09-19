using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

public readonly record struct MemoryStatus(long TotalBytes, long AvailableBytes);

/// <summary>How much RAM the machine has and how much is free right now - a fact the RAM test must ask the
/// OS for (spec §10: "ظرفیت آزاد را خودکار تشخیص دهد"), and the seam that keeps it testable.</summary>
public interface IMemoryProbe { MemoryStatus Read(); }

public sealed class Win32MemoryProbe : IMemoryProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public MemoryStatus Read()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new((long)m.TotalPhys, (long)m.AvailPhys);
    }
}

/// <summary>One block of native memory. Native rather than a managed array so a multi-gigabyte test does not
/// feed the garbage collector, and is released deterministically the moment the test ends.</summary>
internal sealed unsafe class NativeBlock : IDisposable
{
    private void* _pointer;
    public int Length { get; }
    public NativeBlock(int length)
    {
        _pointer = NativeMemory.AlignedAlloc((nuint)length, 4096);
        Length = length;
    }
    public Span<byte> Span => _pointer is null ? throw new ObjectDisposedException(nameof(NativeBlock)) : new(_pointer, Length);
    public void Dispose() { if (_pointer is not null) { NativeMemory.AlignedFree(_pointer); _pointer = null; } }
}
