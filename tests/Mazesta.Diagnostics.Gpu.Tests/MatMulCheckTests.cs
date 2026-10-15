using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks; using Vortice.DirectML; using Xunit;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>The gpu.ai test's oracle: a right product passes, an exact INT8 product must match to the unit, and a wrong or NaN float entry is found.</summary>
public class MatMulCheckTests
{
    private const int N = 64;
    private static readonly List<(int, int)> All = [.. Enumerable.Range(0, N * N).Select(x => (x / N, x % N))];

    private static (byte[] A, byte[] B, float[] C) Floats(int seed)
    {
        var r = new Random(seed); float[] a = new float[N * N], b = new float[N * N], c = new float[N * N];
        for (int i = 0; i < a.Length; i++) { a[i] = r.NextSingle() * 2 - 1; b[i] = r.NextSingle() * 2 - 1; }
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) { float sum = 0; for (int k = 0; k < N; k++) sum += a[i * N + k] * b[k * N + j]; c[i * N + j] = sum; }   // float accumulation, as a GPU would round
        return (MemoryMarshal.AsBytes(a.AsSpan()).ToArray(), MemoryMarshal.AsBytes(b.AsSpan()).ToArray(), c);
    }
    private static string? Check(TensorDataType t, byte[] a, byte[] b, float[] c) => MatMulCheck.FirstWrong(t, a, b, MemoryMarshal.AsBytes(c.AsSpan()), N, All);

    [Fact] public void A_float_product_rounded_in_float_is_no_false_positive() { var (a, b, c) = Floats(1); Assert.Null(Check(TensorDataType.Float32, a, b, c)); }

    [Fact] public void A_corrupted_float_entry_is_named()
    {
        var (a, b, c) = Floats(2); c[5 * N + 7] = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(c[5 * N + 7]) ^ (1 << 27));   // one flipped exponent bit
        Assert.StartsWith("row 5, column 7:", Check(TensorDataType.Float32, a, b, c));
    }

    [Fact] public void A_nan_is_wrong() { var (a, b, c) = Floats(3); c[N + 1] = float.NaN; Assert.StartsWith("row 1, column 1:", Check(TensorDataType.Float32, a, b, c)); }

    [Fact] public void A_half_product_passes_and_an_infinity_does_not()
    {
        var r = new Random(4); Half[] a = new Half[N * N], b = new Half[N * N], c = new Half[N * N];
        for (int i = 0; i < a.Length; i++) { a[i] = (Half)(r.NextSingle() * 2 - 1); b[i] = (Half)(r.NextSingle() * 2 - 1); }
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) { Half sum = (Half)0; for (int k = 0; k < N; k++) sum += a[i * N + k] * b[k * N + j]; c[i * N + j] = sum; }   // half accumulation
        string? Run() => MatMulCheck.FirstWrong(TensorDataType.Float16, MemoryMarshal.AsBytes(a.AsSpan()), MemoryMarshal.AsBytes(b.AsSpan()), MemoryMarshal.AsBytes(c.AsSpan()), N, All);
        Assert.Null(Run());
        c[3] = Half.PositiveInfinity; Assert.StartsWith("row 0, column 3:", Run());
    }

    [Fact] public void An_int8_product_must_match_exactly()
    {
        var r = new Random(5); byte[] a = new byte[N * N], b = new byte[N * N]; r.NextBytes(a); r.NextBytes(b); int[] c = new int[N * N];
        for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) for (int k = 0; k < N; k++) c[i * N + j] += (sbyte)a[i * N + k] * (sbyte)b[k * N + j];
        Assert.Null(MatMulCheck.FirstWrong(TensorDataType.Int8, a, b, MemoryMarshal.AsBytes(c.AsSpan()), N, All));
        c[2 * N + 9] += 1; Assert.StartsWith("row 2, column 9:", MatMulCheck.FirstWrong(TensorDataType.Int8, a, b, MemoryMarshal.AsBytes(c.AsSpan()), N, All));
    }

    [Fact] public void The_sample_is_one_whole_row_plus_scattered_entries_and_repeats_for_a_seed()
    {
        var s = MatMulCheck.Sample(4096, 42);
        Assert.Equal(4096 + MatMulCheck.Scattered, s.Count); Assert.Single(s.Take(4096).Select(e => e.Row).Distinct()); Assert.Equal(s, MatMulCheck.Sample(4096, 42));
    }
}
