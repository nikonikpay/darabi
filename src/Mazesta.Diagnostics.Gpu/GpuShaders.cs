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

internal static class GpuHash
{
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
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VramPatternShader(ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> errors, int width, uint pass, uint baseIndex, int verify, uint mask) : IComputeShader
{
    public void Execute()
    {
        uint i = (uint)(ThreadIds.Y * width + ThreadIds.X);
        uint kind = pass % 8u;
        // The cell this thread handles: its own, except when the stride pattern is written (mask = cells - 1, a power of two minus one).
        uint cell = verify == 0 && kind == 6u ? (i * 40503u) & mask : i;
        uint index = cell + baseIndex;
        uint mixed = index * 2654435761u;
        mixed ^= mixed >> 15;
        uint walk = 1u << (int)((index + pass / 8u) & 31u);
        uint scramble = (index ^ (pass * 0x9E3779B9u)) * 0x85EBCA6Bu;
        scramble ^= scramble >> 13;
        uint expected = kind == 0u ? mixed : kind == 1u ? ~mixed : kind == 2u ? 0xAAAAAAAAu : kind == 3u ? 0x55555555u
            : kind == 4u ? walk : kind == 5u ? ~walk : kind == 6u ? mixed ^ 0x0F0F0F0Fu : scramble;
        if (verify == 0) buffer[(int)cell] = expected;
        else if (buffer[(int)cell] != expected) Hlsl.InterlockedAdd(ref errors[0], 1);
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
    public static uint StrideCell(uint thread, uint mask) => unchecked(thread * 40503u) & mask;
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
