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

    private static ulong Seed(int pass, int block) => ((ulong)(uint)pass << 32 | (uint)block) * 0xD1B54A32D192ED03UL + 0x8CB92BA72F3D8DD7UL;
    private static ulong Next(ref ulong s) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DUL; }
}
