// DirectX Raytracing benchmark (GpuRayTracingBenchmark): inline ray tracing (DXR 1.1 RayQuery) from a compute
// shader, so the whole frame goes through the GPU's ray-tracing hardware without a ray-tracing pipeline state.
// Compiled offline by tools/compile-gpu-shaders.ps1.
//
// Per pixel: a camera ray, then for each of up to three hits a shadow ray and a mirror bounce. Instance 0 is the
// ground plane; every other instance is a unit icosphere that is only translated and uniformly scaled, so the
// object-space hit point is also the world-space normal. With Count set, the rays traced are summed into Rays[0].

#define RS "RootConstants(num32BitConstants=4, b0), SRV(t0), UAV(u0), UAV(u1)"

cbuffer Frame : register(b0) { uint Width; uint Height; uint Count; float Unused; };
RaytracingAccelerationStructure Scene : register(t0);
RWStructuredBuffer<uint> Pixels : register(u0);
RWStructuredBuffer<uint> Rays : register(u1);

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    float2 uv = (float2(id.xy) + 0.5) / float2(Width, Height) * 2 - 1;
    float3 origin = float3(0, 9, -30), dir = normalize(float3(uv.x * Width / (float)Height, -uv.y - 0.25, 1.6));
    const float3 sun = normalize(float3(-0.5, 1, -0.4));
    float3 color = 0; float weight = 1; uint rays = 0;

    for (int bounce = 0; bounce < 3; bounce++)
    {
        RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.001; ray.TMax = 1000;
        RayQuery<RAY_FLAG_FORCE_OPAQUE> hit;
        hit.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray); while (hit.Proceed()) { } rays++;
        if (hit.CommittedStatus() != COMMITTED_TRIANGLE_HIT) { color += weight * lerp(float3(0.8, 0.85, 0.9), float3(0.3, 0.5, 0.9), saturate(dir.y)); break; }

        float t = hit.CommittedRayT(); float3 p = origin + dir * t;
        bool ground = hit.CommittedInstanceID() == 0;
        float3 n = ground ? float3(0, 1, 0) : normalize(hit.CommittedObjectRayOrigin() + hit.CommittedObjectRayDirection() * t);
        float3 albedo = ground ? (((int)floor(p.x) + (int)floor(p.z)) & 1 ? 0.8 : 0.3) : frac(hit.CommittedInstanceID() * float3(0.618, 0.382, 0.771));

        RayDesc shadow; shadow.Origin = p + n * 0.002; shadow.Direction = sun; shadow.TMin = 0.001; shadow.TMax = 1000;
        RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> occluded;
        occluded.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, shadow); while (occluded.Proceed()) { } rays++;
        float light = occluded.CommittedStatus() == COMMITTED_NOTHING ? saturate(dot(n, sun)) : 0;

        color += weight * albedo * (0.12 + 0.88 * light);
        origin = p + n * 0.002; dir = reflect(dir, n); weight *= ground ? 0.2 : 0.5;
    }

    uint3 c = (uint3)(saturate(color / (1 + color)) * 255);
    Pixels[id.y * Width + id.x] = c.r | c.g << 8 | c.b << 16 | 0xFF000000;
    if (Count != 0) { uint total = WaveActiveSum(rays); if (WaveIsFirstLane()) InterlockedAdd(Rays[0], total); }
}
