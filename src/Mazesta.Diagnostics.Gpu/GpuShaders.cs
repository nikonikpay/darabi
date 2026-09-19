using ComputeSharp;
namespace Mazesta.Diagnostics.Gpu;

/// <summary>
/// A chain of integer multiply/xor-shift rounds per thread: heavy on the shader ALUs, deterministic, and
/// integer-exact, so the CPU can recompute any thread's result and demand <b>equality</b> - a GPU that
/// computes wrongly under load (overheating, unstable memory, marginal power) is caught as a mismatch, not
/// as a rounding difference. <see cref="GpuHash.Reference"/> must stay in lock-step with this arithmetic.
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HashStressShader(ReadWriteBuffer<uint> output, int rounds, uint seed) : IComputeShader
{
    public void Execute()
    {
        uint h = (uint)ThreadIds.X ^ seed;
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
    public static uint Reference(uint index, int rounds, uint seed)
    {
        uint h = index ^ seed;
        for (int r = 0; r < rounds; r++)
        {
            unchecked { h = h * 1664525u + 1013904223u; }
            h ^= h >> 13;
            unchecked { h *= 0x5BD1E995u; }
            h ^= h >> 15;
        }
        return h;
    }
}

/// <summary>
/// VRAM pattern pass over one buffer, dispatched 2-D because a single dimension cannot address hundreds of
/// megabytes. <c>verify == 0</c> writes the pass's pattern; otherwise every cell is compared with what the
/// pattern says it should hold and each difference bumps the error counter atomically - the comparison runs
/// on the GPU, so the <b>whole</b> allocation is checked, not a sample of it (spec §10).
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VramPatternShader(ReadWriteBuffer<uint> buffer, ReadWriteBuffer<int> errors, int width, uint pass, uint baseIndex, int verify) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.Y * width + ThreadIds.X;
        uint index = (uint)i + baseIndex;
        uint mixed = index * 2654435761u;
        mixed ^= mixed >> 15;
        uint kind = pass % 4u;
        uint expected = kind == 0u ? mixed : kind == 1u ? ~mixed : kind == 2u ? 0xAAAAAAAAu : 0x55555555u;
        if (verify == 0) buffer[i] = expected;
        else if (buffer[i] != expected) Hlsl.InterlockedAdd(ref errors[0], 1);
    }
}

internal static class VramPattern
{
    public static uint Expected(uint index, uint pass)
    {
        uint mixed = unchecked(index * 2654435761u); mixed ^= mixed >> 15;
        return (pass % 4u) switch { 0u => mixed, 1u => ~mixed, 2u => 0xAAAAAAAAu, _ => 0x55555555u };
    }
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
