using System.Runtime.InteropServices; using Vortice.DirectML;
namespace Mazesta.Diagnostics.Gpu.Benchmarks;

/// <summary>
/// Checks a square matrix product the GPU computed (row-major n×n, C = A·B) against a CPU reference on a seeded sample of entries:
/// one whole row plus <see cref="Scattered"/> entries anywhere, so a fault confined to a tile or a lane still has a chance to show
/// while the check stays a few milliseconds. INT8 into INT32 must match exactly. Floats may differ by the worst-case rounding of an
/// n-term dot product, n·u·Σ|a||b|, plus the rounding of the stored result u·|c| (u = 2^-24 for FP32; 2^-11 for FP16, which may also
/// accumulate in half precision); the reference sums in double from the very inputs the GPU had, so their own rounding does not count.
/// NaN or infinity is always wrong. For FP16 the bound is wide (n·u = 2 at n = 4096), so only gross corruption shows there.
/// </summary>
internal static class MatMulCheck
{
    public const int Scattered = 256;

    /// <summary>The entries to check: one row the seed picks, in full, then <see cref="Scattered"/> random (row, column) pairs.</summary>
    public static List<(int Row, int Col)> Sample(int n, int seed)
    {
        var random = new Random(seed); int row = random.Next(n);
        List<(int, int)> entries = [.. Enumerable.Range(0, n).Select(j => (row, j))];
        for (int i = 0; i < Scattered; i++) entries.Add((random.Next(n), random.Next(n)));
        return entries;
    }

    /// <summary>The first sampled entry the GPU got wrong, described for the report, or null when every one is right.</summary>
    public static string? FirstWrong(TensorDataType input, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c, int n, IEnumerable<(int Row, int Col)> entries)
    {
        double u = input switch { TensorDataType.Float32 => Math.ScaleB(1, -24), TensorDataType.Float16 => Math.ScaleB(1, -11), TensorDataType.Int8 => 0, _ => throw new ArgumentOutOfRangeException(nameof(input)) };
        foreach (var (i, j) in entries)
        {
            double exact = 0, magnitude = 0;
            for (int k = 0; k < n; k++) { double p = At(input, a, i * n + k) * At(input, b, k * n + j); exact += p; magnitude += Math.Abs(p); }
            double gpu = input == TensorDataType.Int8 ? MemoryMarshal.Cast<byte, int>(c)[i * n + j] : At(input, c, i * n + j);
            double bound = u * (n * magnitude + Math.Abs(exact));
            if (!double.IsFinite(gpu) || Math.Abs(gpu - exact) > bound)
                return $"row {i}, column {j}: GPU {gpu:R}, CPU reference {exact:R}" + (u == 0 ? " (must match exactly)" : $" (allowed error {bound:G3})");
        }
        return null;
    }

    private static double At(TensorDataType type, ReadOnlySpan<byte> s, int index) => type switch
    {
        TensorDataType.Float32 => MemoryMarshal.Cast<byte, float>(s)[index],
        TensorDataType.Float16 => (double)MemoryMarshal.Cast<byte, Half>(s)[index],
        _ => (sbyte)s[index],
    };
}
