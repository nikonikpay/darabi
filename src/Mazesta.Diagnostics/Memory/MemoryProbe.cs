using System.Buffers; using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

/// <param name="AvailableCommitBytes">What Windows will still let programs commit, RAM and page file together (0 where it is not read).</param>
public readonly record struct MemoryStatus(long TotalBytes, long AvailableBytes, long AvailableCommitBytes = 0);

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
        return new((long)m.TotalPhys, (long)m.AvailPhys, (long)m.AvailPageFile);
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
    /// <summary>The block as <see cref="Memory{T}"/>, for asynchronous (overlapped) I/O that must not move the buffer.</summary>
    public Memory<byte> Memory => new Manager(this).Memory;
    private bool _locked;
    /// <summary>Keeps the block's pages in RAM (VirtualLock) until it is freed; false when Windows refuses (the working-set minimum is too small).</summary>
    public bool Lock() => _locked = _pointer is not null && VirtualLock((nint)_pointer, (nuint)Length);
    public void Dispose()
    {
        if (_pointer is null) return;
        if (_locked) VirtualUnlock((nint)_pointer, (nuint)Length);
        NativeMemory.AlignedFree(_pointer); _pointer = null;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualLock(nint address, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualUnlock(nint address, nuint size);

    private sealed class Manager(NativeBlock block) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => block.Span;
        public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)block._pointer + elementIndex);   // native memory never moves
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
