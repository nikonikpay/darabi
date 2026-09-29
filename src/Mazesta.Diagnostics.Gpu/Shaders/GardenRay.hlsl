// Visual GPU test, DirectX Raytracing (GpuSceneExecutor + GardenRay.cs): the courtyard at blue hour, every pixel ray traced through
// the GPU's ray-tracing hardware (DXR 1.1 inline RayQuery in a compute shader). Samples camera rays a pixel (stratified, so edges are
// antialiased); at each surface a shadow ray to the moon and to every lamp in reach, aimed at a random point on the lamp (soft shadows),
// and one diffuse ray for the light bounced off nearby surfaces (global illumination); leaves are cut out of their cards during
// traversal; the water reflects and refracts (into the lit pool), glass lets light through and mirrors, metal and polished stone
// reflect - up to Bounces deep. The random numbers come from a hash of the pixel and sample, never the clock: the same Time gives the same image.
// Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "CBV(b1), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), SRV(t5), SRV(t6), SRV(t7), DescriptorTable(SRV(t8)), UAV(u0), " \
           "StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR)"

#include "Garden.hlsli"

struct MeshInfo { float3 Centre; uint FirstSubmesh; float3 Extent; uint BaseVertex; };
struct SubInfo { uint IndexStart; uint Material; uint Opaque; uint Pad; };

RaytracingAccelerationStructure Scene : register(t0);
StructuredBuffer<Instance> Instances : register(t1);
StructuredBuffer<Material> Materials : register(t2);
StructuredBuffer<Light> Lights : register(t3);
StructuredBuffer<MeshInfo> Meshes : register(t4);
StructuredBuffer<SubInfo> Subs : register(t5);
ByteAddressBuffer Vertices : register(t6);
ByteAddressBuffer Indices : register(t7);
Texture2DArray<float4> Textures : register(t8);
RWStructuredBuffer<uint> Pixels : register(u0);
SamplerState Linear : register(s0);

static const uint MaskVisible = 1, MaskShadow = 2;

struct Hit { float3 P; float3 N; float2 Uv; uint Material; };

uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
float Rand(inout uint seed) { seed = Hash(seed); return (seed >> 8) * (1.0 / 16777216.0); }
float3 InBall(inout uint seed)
{
    float z = Rand(seed) * 2 - 1, a = Rand(seed) * 2 * Pi, r = sqrt(1 - z * z);
    return float3(r * cos(a), r * sin(a), z) * pow(Rand(seed), 1.0 / 3);
}
float3 CosineAround(float3 n, inout uint seed)
{
    float a = Rand(seed) * 2 * Pi, r = sqrt(Rand(seed));
    float3 t = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0))), b = cross(n, t);
    return normalize(t * r * cos(a) + b * r * sin(a) + n * sqrt(max(0, 1 - r * r)));
}

uint3 Triangle(SubInfo s, uint prim) { return Indices.Load3((s.IndexStart + prim * 3) * 4); }

float2 UvAt(uint instance, uint geometry, uint prim, float2 bary)
{
    MeshInfo m = Meshes[Instances[instance].Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    uint3 t = Triangle(s, prim);
    float2 a = DecodeUv(Vertices.Load((m.BaseVertex + t.x) * 16 + 12)), b = DecodeUv(Vertices.Load((m.BaseVertex + t.y) * 16 + 12)), c = DecodeUv(Vertices.Load((m.BaseVertex + t.z) * 16 + 12));
    return a + (b - a) * bary.x + (c - a) * bary.y;
}

// A leaf card is a hit only where its texture is opaque.
bool AlphaPass(uint instance, uint geometry, uint prim, float2 bary)
{
    MeshInfo m = Meshes[Instances[instance].Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    Material mat = Materials[s.Material];
    if (mat.Texture < 0) return true;
    return Textures.SampleLevel(Linear, float3(UvAt(instance, geometry, prim, bary), mat.Texture), 1).a >= 0.5;
}

Hit Fetch(uint instance, uint geometry, uint prim, float2 bary, float3 p)
{
    Instance inst = Instances[instance]; MeshInfo m = Meshes[inst.Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    uint3 t = Triangle(s, prim);
    uint3 o = (m.BaseVertex + t) * 16;
    float3 na = DecodeNormal(Vertices.Load(o.x + 8)), nb = DecodeNormal(Vertices.Load(o.y + 8)), nc = DecodeNormal(Vertices.Load(o.z + 8));
    float2 ua = DecodeUv(Vertices.Load(o.x + 12)), ub = DecodeUv(Vertices.Load(o.y + 12)), uc = DecodeUv(Vertices.Load(o.z + 12));
    Hit h; h.P = p; h.Material = s.Material;
    h.N = normalize(mul(Rotation(inst), na + (nb - na) * bary.x + (nc - na) * bary.y));
    if (inst.Flags & 1) h.N = LogoTurn(h.N);
    h.Uv = ua + (ub - ua) * bary.x + (uc - ua) * bary.y;
    return h;
}

bool Trace(float3 origin, float3 dir, float tmax, uint mask, out Hit hit)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, mask, ray);
    while (q.Proceed())
        if (q.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE && AlphaPass(q.CandidateInstanceID(), q.CandidateGeometryIndex(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    hit = (Hit)0;
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return false;
    hit = Fetch(q.CommittedInstanceID(), q.CommittedGeometryIndex(), q.CommittedPrimitiveIndex(), q.CommittedTriangleBarycentrics(), origin + dir * q.CommittedRayT());
    return true;
}

bool Occluded(float3 origin, float3 dir, float tmax)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, MaskShadow, ray);
    while (q.Proceed())
        if (AlphaPass(q.CandidateInstanceID(), q.CandidateGeometryIndex(), q.CandidatePrimitiveIndex(), q.CandidateTriangleBarycentrics()))
            q.CommitNonOpaqueTriangleHit();
    return q.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
}

Surface SurfaceAt(Hit h, float3 v)
{
    Material m = Materials[h.Material];
    float4 texel = m.Texture >= 0 ? Textures.SampleLevel(Linear, float3(h.Uv, m.Texture), 0) : 1;
    float3 n = h.N; if (dot(n, v) < 0) n = -n;
    return MaterialSurface(m, h.P, n, texel);
}

// Light arriving from the moon and every lamp in reach. With shadows, each is checked by a ray to a random point on it (the moon's
// disc, the lamp's bulb), so shadows soften with distance from what casts them.
float3 Direct(Surface s, float3 p, float3 v, bool shadows, inout uint seed)
{
    float3 c = 0, o = p + s.Normal * 0.01;
    if (SunOn > 0 && dot(s.Normal, SunDir) > 0)
    {
        float3 toSun = shadows ? normalize(SunDir + InBall(seed) * 0.012) : SunDir;
        if (!(shadows && Occluded(o, toSun, 300))) c += Brdf(s, v, SunDir, SunColor);
    }
    for (uint k = 0; k < LightCount; k++)
    {
        Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, p, l, d);
        if (all(e <= 0) || dot(s.Normal, l) <= 0) continue;
        if (shadows)
        {
            float3 q = L.Position + InBall(seed) * L.Radius - o; float dq = length(q);
            if (Occluded(o, q / dq, max(0.01, dq - 0.05))) continue;
        }
        c += Brdf(s, v, l, e);
    }
    return c;
}

// What a secondary ray sees, lit without further bounces or shadow rays.
float3 Glance(float3 origin, float3 dir, float tmax, inout uint seed)
{
    Hit h;
    if (!Trace(origin, dir, tmax, MaskVisible, h)) return SkyColor(dir);
    Material m = Materials[h.Material];
    float3 v = -dir; Surface s = SurfaceAt(h, v);
    if (m.Kind == KWater || m.Kind == KGlass) return SkyColor(reflect(dir, s.Normal)) * 0.3 + s.Emission;
    return s.Albedo * Ambient(s.Normal) * (1 - s.Metallic) * 0.5 + s.Emission + Direct(s, h.P, v, false, seed);
}

// One camera ray's light: the surfaces it meets, through water and glass and off polished surfaces, up to Bounces deep.
float3 Radiance(float3 origin, float3 dir, inout uint seed)
{
    float3 color = 0, weight = 1;
    for (uint bounce = 0; bounce <= Bounces; bounce++)
    {
        Hit h;
        if (!Trace(origin, dir, 400, MaskVisible, h)) { color += weight * SkyColor(dir); break; }
        Material m = Materials[h.Material];
        float3 v = -dir; Surface s = SurfaceAt(h, v);

        if (m.Kind == KWater)
        {
            float3 n = WaterNormal(h.P, Time); if (dot(n, v) < 0) n = -n;
            float f = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
            color += weight * f * Glance(h.P + n * 0.01, reflect(dir, n), 400, seed);
            float3 r = refract(dir, n, 1 / 1.33); if (all(r == 0)) break;
            weight *= (1 - f) * float3(0.75, 0.92, 0.9);
            origin = h.P - n * 0.01; dir = r; continue;
        }
        if (m.Kind == KGlass)
        {
            float f = 0.04 + 0.96 * pow(1 - saturate(dot(s.Normal, v)), 5);
            color += weight * (s.Emission + f * Glance(h.P + s.Normal * 0.01, reflect(dir, s.Normal), 400, seed));
            weight *= (1 - f) * lerp(1, m.Base, 0.3) * (m.Alpha < 1 ? m.Alpha + 0.4 : 1);
            origin = h.P + dir * 0.01; continue;   // thin glass: straight on
        }

        // light bounced off what is near: one diffuse ray on the first surface (deeper ones use the ambient term alone)
        float3 bounced = bounce == 0 ? Glance(h.P + s.Normal * 0.01, CosineAround(s.Normal, seed), 12, seed) : Ambient(s.Normal) * 0.6;
        float3 lit = s.Albedo * (1 - s.Metallic) * (bounced * 0.8 + Ambient(s.Normal) * 0.15) + s.Emission + Direct(s, h.P, v, true, seed);
        // mirror-like surfaces (metal, polished stone, glazed tiles) carry on as a reflection
        float gloss = saturate(1 - s.Roughness * 3);
        float3 f0 = lerp(0.04, s.Albedo, s.Metallic);
        float3 reflectance = (f0 + (1 - f0) * pow(1 - saturate(dot(s.Normal, v)), 5)) * gloss;
        color += weight * lit * (1 - max(reflectance.r, max(reflectance.g, reflectance.b)) * (1 - s.Metallic));
        if (max(reflectance.r, max(reflectance.g, reflectance.b)) < 0.03) break;
        weight *= reflectance;
        origin = h.P + s.Normal * 0.01; dir = reflect(dir, s.Normal);
    }
    return color;
}

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    uint n = max(1, Samples), side = (uint)ceil(sqrt((float)n));
    float3 color = 0;
    for (uint k = 0; k < n; k++)
    {
        uint seed = Hash(id.y * 8191 + id.x * 131071 + k * 524287 + 17);
        float2 jitter = n == 1 ? 0.5 : (float2(k % side, k / side) + float2(Rand(seed), Rand(seed))) / side;   // stratified within the pixel
        float2 ndc = (float2(id.xy) + jitter) / float2(Width, Height) * 2 - 1;
        float3 dir = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect - CamUp * ndc.y * TanHalfFovY);
        color += Radiance(Eye, dir, seed);
    }
    uint3 c = (uint3)(Tonemap(color / n) * 255 + 0.5);
    Pixels[id.y * Pitch + id.x] = c.r | c.g << 8 | c.b << 16 | 0xFF000000;
}
