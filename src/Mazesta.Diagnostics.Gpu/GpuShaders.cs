using ComputeSharp;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// A chain of integer multiply/xor-shift rounds per thread: heavy on the shader ALUs, deterministic, and
/// integer-exact, so the CPU can recompute any thread's result and demand <b>equality</b> - a GPU that
/// computes wrongly under load (overheating, unstable memory, marginal power) is caught as a mismatch, not
/// as a rounding difference. With <c>chain</c> set a dispatch starts from what the previous dispatch left in
/// the buffer instead of the thread index, so the value read back after a submission of many dispatches has
/// passed through every one of them: a wrong result in any dispatch, not only the last, changes it.
/// <see cref="GpuHash.Reference"/> must stay in lock-step with this arithmetic.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HashStressShader(ReadWriteBuffer<uint> output, int rounds, uint seed, int chain) : IComputeShader
{
    public void Execute()
    {
        uint h = (chain != 0 ? output[ThreadIds.X] : (uint)ThreadIds.X) ^ seed;
        for (int r = 0; r < rounds; r++)
        {
            h = h * 1664525u + 1013904223u;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
        }
        output[ThreadIds.X] = h;
    }
}

/// <summary>
/// The temperature test's load: the same integer rounds, but every eighth round also reads a cell of a 256 MB table (an address of its own, not waiting for the arithmetic: the reads stream while the shader units work), so the memory controllers,
/// the cache and the memory chips work beside the shader units, the way a game loads a card. The ALU-only <see cref="HashStressShader"/> concentrates all the power
/// in the shader units with the memory idle; this one spreads the heat over the die. The table's cells are a formula of their index (<see cref="GpuHash.TableCell"/>),
/// so the CPU recomputes any thread's result exactly. <see cref="GpuHash.ThermalReference"/> must stay in lock-step.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ThermalShader(ReadWriteBuffer<uint> output, ReadOnlyBuffer<uint> table, int rounds, uint seed, int chain, uint mask) : IComputeShader
{
    public void Execute()
    {
        uint h = (chain != 0 ? output[ThreadIds.X] : (uint)ThreadIds.X) ^ seed;
        for (int r = 0; r < rounds; r++)
        {
            h = h * 1664525u + 1013904223u;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            if ((r & 7) == 7) h ^= table[(int)((uint)ThreadIds.X + (uint)r * 4099u) & (int)mask];
        }
        output[ThreadIds.X] = h;
    }
}

internal static class GpuHash
{
    public const uint TableCells = 1u << 26;   // 256 MB of 32-bit cells

    public static uint TableCell(uint i) => unchecked(i * 0x9E3779B1u + 0x7F4A7C15u);

    public static uint[] BuildTable()
    {
        var t = new uint[TableCells]; Parallel.For(0, 1 << 10, c => { uint from = (uint)c << 16, to = from + (1u << 16); for (uint i = from; i < to; i++) t[i] = TableCell(i); });
        return t;
    }

    public static uint ThermalReference(uint index, uint start, int rounds, uint seed)
    {
        uint h = start ^ seed;
        for (int r = 0; r < rounds; r++)
        {
            unchecked { h = h * 1664525u + 1013904223u; }
            h ^= h >> 13;
            unchecked { h *= 0x5BD1E995u; }
            h ^= h >> 15;
            if ((r & 7) == 7) h ^= TableCell((index + (uint)r * 4099u) & (TableCells - 1));
        }
        return h;
    }

    public static uint ThermalChained(uint index, int rounds, uint seed, int dispatches)
    {
        uint h = index;
        for (int d = 0; d < dispatches; d++) h = ThermalReference(index, h, rounds, seed);
        return h;
    }

    /// <summary>One dispatch's result for a thread that starts from <paramref name="start"/> (its index, or the previous dispatch's result).</summary>
    public static uint Reference(uint start, int rounds, uint seed)
    {
        uint h = start ^ seed;
        for (int r = 0; r < rounds; r++)
        {
            unchecked { h = h * 1664525u + 1013904223u; }
            h ^= h >> 13;
            unchecked { h *= 0x5BD1E995u; }
            h ^= h >> 15;
        }
        return h;
    }

    /// <summary>A thread's result after <paramref name="dispatches"/> chained dispatches: the first starts from its index, each next from the one before.</summary>
    public static uint Chained(uint index, int rounds, uint seed, int dispatches)
    {
        uint h = index;
        for (int d = 0; d < dispatches; d++) h = Reference(h, rounds, seed);
        return h;
    }
}

/// <summary>
/// VRAM pattern pass over one buffer, dispatched 2-D because a single dimension cannot address hundreds of
/// megabytes. <c>verify == 0</c> writes the pass's pattern; otherwise every cell is compared with what the
/// pattern says it should hold and each difference bumps the error counter atomically - the comparison runs
/// on the GPU, so the <b>whole</b> allocation is checked, not a sample of it (spec §10). Eight patterns rotate: address-derived and its inverse,
/// 0xAAAAAAAA and 0x55555555, a walking one and a walking zero (the bit moves from cell to cell and from pass to pass, so every data line carries
/// a lone 1 and a lone 0), an address pattern written in stride order (thread i writes cell i·K mod n, K odd: every cell once, far from where its
/// neighbours are written, so an address line that is stuck or shorted puts data in the wrong cell), and a pass-seeded scramble.
/// </summary>
[ThreadGroupSize(128, 1, 1)]   // a row of 128 consecutive cells per group: the default 8x8 group touched only 8 consecutive cells per row and left most of each memory transaction unused
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VramPatternShader(ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> errors, int slot, int width, uint pass, uint baseIndex, int verify, uint mask) : IComputeShader
{
    public void Execute()
    {
        uint i = (uint)(ThreadIds.Y * width + ThreadIds.X);
        uint kind = pass % 8u;
        // The cell this thread handles: its own, except when the stride pattern is written (mask = cells - 1, a power of two minus one).
        uint cell = verify == 0 && kind == 6u ? ((((i >> 5) * 40503u) << 5) | (i & 31u)) & mask : i;   // scattered by 32-cell blocks (128 B): still every cell once, but a warp writes whole sectors
        uint index = cell + baseIndex;
        // Only the pass's own pattern is computed (kind is the same for every thread): the shader is memory-bound, not busy with all eight at once.
        uint expected;
        if (kind == 2u) expected = 0xAAAAAAAAu;
        else if (kind == 3u) expected = 0x55555555u;
        else if (kind == 4u || kind == 5u) { uint walk = 1u << (int)((index + pass / 8u) & 31u); expected = kind == 4u ? walk : ~walk; }
        else if (kind == 7u) { uint scramble = (index ^ (pass * 0x9E3779B9u)) * 0x85EBCA6Bu; expected = scramble ^ (scramble >> 13); }
        else { uint mixed = index * 2654435761u; mixed ^= mixed >> 15; expected = kind == 0u ? mixed : kind == 1u ? ~mixed : mixed ^ 0x0F0F0F0Fu; }
        if (verify == 0) buffer[(int)cell] = expected;
        else if (buffer[(int)cell] != expected) Hlsl.InterlockedAdd(ref errors[slot], 1);   // this buffer's own counter (see GpuVramExecutor)
    }
}

internal static class VramPattern
{
    public const int Kinds = 8;
    public static readonly string[] Names = ["address", "~address", "0xAAAAAAAA", "0x55555555", "walking 1", "walking 0", "address, stride-order write", "scramble"];

    /// <summary>The shader's pattern on the CPU, for the tests.</summary>
    public static uint Expected(uint index, uint pass) => unchecked(Mix(index, pass));
    private static uint Mix(uint index, uint pass)
    {
        uint mixed = unchecked(index * 2654435761u); mixed ^= mixed >> 15;
        uint walk = 1u << (int)((index + pass / 8u) & 31u);
        uint scramble = unchecked((index ^ (pass * 0x9E3779B9u)) * 0x85EBCA6Bu); scramble ^= scramble >> 13;
        return (pass % 8u) switch { 0u => mixed, 1u => ~mixed, 2u => 0xAAAAAAAAu, 3u => 0x55555555u, 4u => walk, 5u => ~walk, 6u => mixed ^ 0x0F0F0F0Fu, _ => scramble };
    }

    /// <summary>The cell thread <paramref name="thread"/> writes in the stride pass (40503 is odd, so over a power-of-two buffer every cell is hit once).</summary>
    public static uint StrideCell(uint thread, uint mask) => unchecked((((thread >> 5) * 40503u) << 5) | (thread & 31u)) & mask;
}

/// <summary>A fixed scene of 25 spheres traced with four reflection bounces per pixel - a real 3-D-style
/// ray-tracing workload whose image must come out identical every frame.</summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RayTraceShader(ReadWriteBuffer<float> pixels) : IComputeShader
{
    public void Execute()
    {
        int index = ThreadIds.X;
        float u = ((index % 512) + 0.5f) / 256f - 1f, v = ((index / 512) + 0.5f) / 256f - 1f;
        float3 origin = new(0, 0, -5), direction = Hlsl.Normalize(new float3(u, v, 1.8f));
        float light = 0, weight = 1;
        for (int bounce = 0; bounce < 4; bounce++)
        {
            float nearest = 1000; float3 normal = new(0, 1, 0); float tint = 0;
            for (int sphere = 0; sphere < 25; sphere++)
            {
                float3 center = new((sphere % 5 - 2) * 1.2f, (sphere / 5 - 2) * 1.2f, 1f + (sphere % 3) * .7f);
                float3 offset = origin - center;
                float b = Hlsl.Dot(offset, direction), c = Hlsl.Dot(offset, offset) - .3f;
                float discriminant = b * b - c;
                if (discriminant > 0)
                {
                    float hit = -b - Hlsl.Sqrt(discriminant);
                    if (hit > .001f && hit < nearest) { nearest = hit; normal = Hlsl.Normalize(origin + direction * hit - center); tint = .3f + .025f * sphere; }
                }
            }
            if (nearest < 1000)
            {
                light += weight * tint * (.15f + .85f * Hlsl.Max(0, Hlsl.Dot(normal, Hlsl.Normalize(new float3(-1, 2, -2)))));
                origin += direction * nearest + normal * .002f; direction = Hlsl.Reflect(direction, normal); weight *= .3f;
            }
            else { light += weight * (.1f + .1f * (direction.Y + 1)); weight = 0; }
        }
        pixels[index] = light;
    }
}
