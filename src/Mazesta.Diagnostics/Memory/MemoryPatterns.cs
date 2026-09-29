using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// The data patterns of the RAM test (spec §10: "الگوهای متنوع، random و walking bits"), after MemTest86's families: eight walking
/// ones, eight walking zeros, two alternating-bit bytes, all zeros and all ones, an address-in-address pattern and its inverse (a cell
/// that returns another cell's data is an addressing fault a constant pattern cannot see; the inverse flips every bit the first one set)
/// and a seeded random stream. Fill and count are exact inverses, so a mismatch is a real read-back difference, never a generator
/// difference. "Address" here is the offset inside the test's own buffer, a virtual address: Windows does not tell a program which
/// physical address or DIMM a page lands on, so a mismatch names a buffer offset, never a memory slot.
/// </summary>
internal static class MemoryPatterns
{
    public const int Count = 23, AddressPass = 20, InverseAddressPass = 21, RandomPass = 22;
    private const ulong AddressMask = 0x9E3779B97F4A7C15UL;

    public static string Name(int pass) => (pass % Count) switch
    {
        < 8 => $"walking-1 bit {pass % Count}", < 16 => $"walking-0 bit {pass % Count - 8}", 16 => "0xA5", 17 => "0x5A", 18 => "all zeros", 19 => "all ones",
        AddressPass => "address", InverseAddressPass => "inverse address", _ => "random"
    };

    private static bool IsConstant(int pass, out byte value)
    {
        int p = pass % Count;
        value = p switch { < 8 => (byte)(1 << p), < 16 => (byte)~(1 << (p - 8)), 16 => 0xA5, 17 => 0x5A, 18 => 0x00, _ => 0xFF };
        return p < AddressPass;
    }

    /// <summary>Writes pass <paramref name="pass"/>'s pattern into a block. <paramref name="blockIndex"/> makes address and random data differ per block.</summary>
    public static void Fill(Span<byte> block, int pass, int blockIndex)
    {
        if (IsConstant(pass, out byte b)) { block.Fill(b); return; }
        var words = MemoryMarshal.Cast<byte, ulong>(block);
        if (IsAddress(pass, out ulong flip)) { ulong baseAddress = (ulong)blockIndex * (ulong)block.Length; for (int i = 0; i < words.Length; i++) words[i] = (baseAddress + (ulong)i * 8) ^ AddressMask ^ flip; }
        else { ulong s = Seed(pass, blockIndex); for (int i = 0; i < words.Length; i++) words[i] = Next(ref s); }
    }

    private static bool IsAddress(int pass, out ulong flip)
    {
        int p = pass % Count; flip = p == InverseAddressPass ? ~0UL : 0;
        return p is AddressPass or InverseAddressPass;
    }

    /// <summary>How many bytes of <paramref name="block"/> differ from what <see cref="Fill"/> wrote for the same pass and block.</summary>
    public static long CountMismatches(ReadOnlySpan<byte> block, int pass, int blockIndex)
    {
        if (IsConstant(pass, out byte b)) return block.Length - block.Count(b);
        var words = MemoryMarshal.Cast<byte, ulong>(block); long bad = 0;
        if (IsAddress(pass, out ulong flip)) { ulong baseAddress = (ulong)blockIndex * (ulong)block.Length; for (int i = 0; i < words.Length; i++) bad += WordDiff(words[i], (baseAddress + (ulong)i * 8) ^ AddressMask ^ flip); }
        else { ulong s = Seed(pass, blockIndex); for (int i = 0; i < words.Length; i++) bad += WordDiff(words[i], Next(ref s)); }
        return bad;
    }

    private static long WordDiff(ulong actual, ulong expected)
        => actual == expected ? 0 : System.Numerics.BitOperations.PopCount(ByteMask(actual ^ expected));

    /// <summary>One bit set per differing byte of an xor value.</summary>
    private static ulong ByteMask(ulong x) { x |= x >> 4; x |= x >> 2; x |= x >> 1; return x & 0x0101010101010101UL; }

    // ——— algorithms (MemTest86's tests 3-4, 6 and 8 in spirit): each checks as it goes and returns the bytes it found wrong ———

    public enum Algorithm { MovingInversions, BlockMove, Stride }
    public static string Name(Algorithm a) => a switch { Algorithm.MovingInversions => "moving inversions", Algorithm.BlockMove => "block move", _ => "stride" };

    /// <summary>Moving inversions: fill with <paramref name="basePattern"/>; walk up, checking each word and writing its complement; walk down, checking
    /// the complement and writing the pattern back. A cell that a write to a neighbour disturbs, or that holds a bit only in one direction, shows.
    /// <paramref name="between"/> lets a test disturb the memory between the two walks.</summary>
    public static long MovingInversions(Span<byte> block, ulong basePattern, SpanAction? between = null)
    {
        var w = MemoryMarshal.Cast<byte, ulong>(block); long bad = 0;
        w.Fill(basePattern);
        for (int i = 0; i < w.Length; i++) { bad += WordDiff(w[i], basePattern); w[i] = ~basePattern; }
        between?.Invoke(w);
        for (int i = w.Length - 1; i >= 0; i--) { bad += WordDiff(w[i], ~basePattern); w[i] = basePattern; }
        return bad;
    }

    /// <summary>Block move: seeded data in the first half, copied onto the second half in one memmove, both halves checked against the seed.</summary>
    public static long BlockMove(Span<byte> block, int pass, int blockIndex, SpanAction? between = null)
    {
        int half = block.Length / 2; var source = block[..half]; var target = block.Slice(half, half);
        Fill(source, RandomPass + pass * Count, blockIndex);
        source.CopyTo(target);
        between?.Invoke(MemoryMarshal.Cast<byte, ulong>(block));
        return CountMismatches(source, RandomPass + pass * Count, blockIndex) + CountMismatches(target, RandomPass + pass * Count, blockIndex);
    }

    /// <summary>Stride: each word gets a value made from its own index, written in one jumping order and read back in another (both strides odd, so
    /// every word is visited once): the address lines and the row buffers see a far from sequential pattern.</summary>
    public static long Stride(Span<byte> block, int pass, int blockIndex, SpanAction? between = null)
    {
        var w = MemoryMarshal.Cast<byte, ulong>(block); int n = w.Length; long bad = 0;
        ulong key = Seed(pass, blockIndex);
        static ulong Value(int index, ulong key) { ulong z = (ulong)index * 0x9E3779B97F4A7C15UL ^ key; z = (z ^ z >> 29) * 0xBF58476D1CE4E5B9UL; return z ^ z >> 32; }
        for (long i = 0, at = 0; i < n; i++, at = (at + 4099) % n) w[(int)at] = Value((int)at, key);
        between?.Invoke(w);
        for (long i = 0, at = 0; i < n; i++, at = (at + 8191) % n) bad += WordDiff(w[(int)at], Value((int)at, key));
        return bad;
    }

    public delegate void SpanAction(Span<ulong> words);

    private static ulong Seed(int pass, int block) => ((ulong)(uint)pass << 32 | (uint)block) * 0xD1B54A32D192ED03UL + 0x8CB92BA72F3D8DD7UL;
    private static ulong Next(ref ulong s) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DUL; }
}
