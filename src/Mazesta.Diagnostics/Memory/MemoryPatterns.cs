using System.Runtime.InteropServices;
namespace Mazesta.Diagnostics.Memory;

/// <summary>
/// The data patterns of the RAM test (spec §10: "الگوهای متنوع، random و walking bits"): eight walking
/// ones, eight walking zeros, two alternating-bit bytes, an address-in-address pattern (a cell that
/// returns another cell's data is an addressing fault a constant pattern cannot see) and a seeded random
/// stream. Fill and count are exact inverses, so a mismatch is a real read-back difference, never a
/// generator difference.
/// </summary>
internal static class MemoryPatterns
{
    public const int Count = 20;
    private const ulong AddressMask = 0x9E3779B97F4A7C15UL;

    public static string Name(int pass) => (pass % Count) switch { < 8 => $"walking-1 bit {pass % Count}", < 16 => $"walking-0 bit {pass % Count - 8}", 16 => "0xA5", 17 => "0x5A", 18 => "address", _ => "random" };

    private static bool IsConstant(int pass, out byte value)
    {
        int p = pass % Count;
        value = p switch { < 8 => (byte)(1 << p), < 16 => (byte)~(1 << (p - 8)), 16 => 0xA5, _ => 0x5A };
        return p < 18;
    }

    /// <summary>Writes pass <paramref name="pass"/>'s pattern into a block. <paramref name="blockIndex"/> makes address and random data differ per block.</summary>
    public static void Fill(Span<byte> block, int pass, int blockIndex)
    {
        if (IsConstant(pass, out byte b)) { block.Fill(b); return; }
        var words = MemoryMarshal.Cast<byte, ulong>(block);
        if (pass % Count == 18) { ulong baseAddress = (ulong)blockIndex * (ulong)block.Length; for (int i = 0; i < words.Length; i++) words[i] = (baseAddress + (ulong)i * 8) ^ AddressMask; }
        else { ulong s = Seed(pass, blockIndex); for (int i = 0; i < words.Length; i++) words[i] = Next(ref s); }
    }

    /// <summary>How many bytes of <paramref name="block"/> differ from what <see cref="Fill"/> wrote for the same pass and block.</summary>
    public static long CountMismatches(ReadOnlySpan<byte> block, int pass, int blockIndex)
    {
        if (IsConstant(pass, out byte b)) return block.Length - block.Count(b);
        var words = MemoryMarshal.Cast<byte, ulong>(block); long bad = 0;
        if (pass % Count == 18) { ulong baseAddress = (ulong)blockIndex * (ulong)block.Length; for (int i = 0; i < words.Length; i++) bad += WordDiff(words[i], (baseAddress + (ulong)i * 8) ^ AddressMask); }
        else { ulong s = Seed(pass, blockIndex); for (int i = 0; i < words.Length; i++) bad += WordDiff(words[i], Next(ref s)); }
        return bad;
    }

    private static long WordDiff(ulong actual, ulong expected)
        => actual == expected ? 0 : System.Numerics.BitOperations.PopCount(ByteMask(actual ^ expected));

    /// <summary>One bit set per differing byte of an xor value.</summary>
    private static ulong ByteMask(ulong x) { x |= x >> 4; x |= x >> 2; x |= x >> 1; return x & 0x0101010101010101UL; }

    private static ulong Seed(int pass, int block) => ((ulong)(uint)pass << 32 | (uint)block) * 0xD1B54A32D192ED03UL + 0x8CB92BA72F3D8DD7UL;
    private static ulong Next(ref ulong s) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DUL; }
}
