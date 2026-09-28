// Visual GPU test, DirectX Raytracing (GpuSceneExecutor, ray-traced mode): the same scene as SceneRaster.hlsl - the test model turning in the
// middle with a ring of smaller copies - but every pixel is ray traced through the GPU's ray-tracing hardware (DXR 1.1 inline RayQuery from
// a compute shader): a camera ray, a shadow ray per hit and up to Bounces mirror reflections, over a reflective checker floor.
// Compiled offline by tools/compile-gpu-shaders.ps1.
//
// Instance 0 is the floor (its own two-triangle BLAS); every other instance is the model, placed by the CPU each frame. The model's triangle
// list is bound too, so a hit on it reads its triangle's normal (object space) and turns it with the instance's own transform.

#define RS "RootConstants(num32BitConstants=8, b0), SRV(t0), SRV(t1), UAV(u0)"

cbuffer Frame : register(b0) { uint Width; uint Height; uint Pitch; uint Bounces; float Time; float Aspect; float2 Pad; };
struct Vertex { float3 Position; float3 Normal; };
RaytracingAccelerationStructure Scene : register(t0);
StructuredBuffer<Vertex> Model : register(t1);
RWStructuredBuffer<uint> Pixels : register(u0);

static const float3 Eye = float3(0, 1.8, -8.5);
static const float3 Target = float3(0, 0.2, 0);
static const float3 Sun = normalize(float3(-0.45, 0.8, -0.4));

float3 Sky(float3 dir)
{
    float3 base = lerp(float3(0.03, 0.03, 0.07), float3(0.18, 0.22, 0.38), saturate(dir.y * 0.5 + 0.5));
    return base + pow(saturate(dot(dir, Sun)), 256) * float3(6, 5.4, 4.2);
}

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    float3 forward = normalize(Target - Eye), right = normalize(cross(float3(0, 1, 0), forward)), up = cross(forward, right);
    float2 ndc = (float2(id.xy) + 0.5) / float2(Width, Height) * 2 - 1;
    const float t = 0.46630766;   // tan(25 degrees): the raster mode's 50 degree vertical field of view
    float3 origin = Eye, dir = normalize(forward + right * ndc.x * t * Aspect - up * ndc.y * t);
    float3 color = 0, weight = 1;

    for (uint bounce = 0; bounce <= Bounces; bounce++)
    {
        RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.001; ray.TMax = 200;
        RayQuery<RAY_FLAG_FORCE_OPAQUE> q;
        q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray); while (q.Proceed()) { }
        if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) { color += weight * Sky(dir); break; }

        float3 p = origin + dir * q.CommittedRayT();
        uint instance = q.CommittedInstanceID();
        float3 n, albedo; float mirror;
        if (instance == 0)
        {
            n = float3(0, 1, 0);
            albedo = ((int)floor(p.x * 0.8) + (int)floor(p.z * 0.8)) & 1 ? float3(0.55, 0.55, 0.58) : float3(0.08, 0.08, 0.09);
            mirror = 0.35;
        }
        else
        {
            float3 local = Model[q.CommittedPrimitiveIndex() * 3].Normal;
            n = normalize(mul((float3x3)q.CommittedObjectToWorld3x4(), local));
            if (dot(n, dir) > 0) n = -n;   // seen from inside a thin wall
            albedo = instance == 1 ? float3(0.99, 0.83, 0.0) : lerp(float3(0.25, 0.55, 1.0), float3(1.0, 0.35, 0.55), frac(instance * 0.618034));
            mirror = instance == 1 ? 0.55 : 0.3;
        }

        RayDesc shadow; shadow.Origin = p + n * 0.002; shadow.Direction = Sun; shadow.TMin = 0.001; shadow.TMax = 200;
        RayQuery<RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> s;
        s.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, shadow); while (s.Proceed()) { }
        float lit = s.CommittedStatus() == COMMITTED_NOTHING ? saturate(dot(n, Sun)) : 0;
        float specular = lit > 0 ? pow(saturate(dot(n, normalize(Sun - dir))), 64) : 0;

        color += weight * (1 - mirror) * (albedo * (0.08 + 0.92 * lit) + specular);
        weight *= mirror * lerp(float3(1, 1, 1), albedo, instance == 1 ? 0.6 : 0.2);
        origin = p + n * 0.002; dir = reflect(dir, n);
    }

    uint3 c = (uint3)(saturate(color / (1 + color)) * 255);
    Pixels[id.y * Pitch + id.x] = c.r | c.g << 8 | c.b << 16 | 0xFF000000;
}
