// The light volume's baker (GardenLightVolume.cs, GardenLightTracer.cs): the light bounced round the garden, worked out beforehand
// with the GPU's ray-tracing hardware (DXR 1.1 inline RayQuery in a compute shader) for the Direct3D test to read as it draws.
// From each point of a grid rays go out round each of the six directions a surface there can face; what they meet is lit with a
// shadow ray to the key light and to every lit lamp in reach (coloured where it has come through a stained pane of the hall's
// windows) and gives back what a ray of its own finds: two bounces. Water and glass let the rays through, polished surfaces mirror.
// The random numbers come from a hash of the entry's number: the same scene is the same light. Never run by the app itself.
// Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "CBV(b1), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), SRV(t5), SRV(t6), SRV(t7), DescriptorTable(SRV(t8, numDescriptors=2)), UAV(u1), RootConstants(num32BitConstants=4, b2), " \
           "StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR)"

#include "Garden.hlsli"

RaytracingAccelerationStructure Scene : register(t0);
StructuredBuffer<Instance> Instances : register(t1);
StructuredBuffer<Material> Materials : register(t2);
StructuredBuffer<Light> Lights : register(t3);
StructuredBuffer<MeshInfo> Meshes : register(t4);
StructuredBuffer<SubInfo> Subs : register(t5);
ByteAddressBuffer Vertices : register(t6);
ByteAddressBuffer Indices : register(t7);
Texture2DArray<float4> Textures : register(t8);
Texture2D<float4> BackdropImage : register(t9);
RWStructuredBuffer<float4> Ping : register(u1);    // what the baker found: an entry's light, and the share of its rays that met the back of a surface
cbuffer Pass : register(b2) { uint Step; uint Last; uint Stage; uint Keep; };   // Step: the first row of entries this dispatch does; Last: rays an entry
SamplerState Linear : register(s0);
#define TraceSampler Linear
#include "GardenTrace.hlsli"

float3 Mountains(float2 uv) { return BackdropImage.SampleLevel(Linear, uv, 0).rgb; }

static const bool Baking = true;   // the baker sees from no eye (no air between one and the surface); it follows light for two bounces with shadow rays at both, and adds no stand-in for the rest

float3 CosineAround(float3 n, inout uint seed)
{
    float a = Rand(seed) * 2 * Pi, r = sqrt(Rand(seed));
    float3 t = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0))), b = cross(n, t);
    return normalize(t * r * cos(a) + b * r * sin(a) + n * sqrt(max(0, 1 - r * r)));
}

// The sky's light along a ray that leaves the scene. What lights a matt surface is, by day, a share of the sky's brightness
// (Garden.hlsli SkyShare); what is seen or mirrored is the sky itself. For the baker the sky is one even light, of Post.y.
float3 SkyLight(float3 dir, bool matt) { return Baking ? SkyZenith * Post.y : Sky(dir) * (matt ? SkyLit() : 1); }

// Light arriving from the sun or the moon and every lit lamp in reach. With shadows, each is checked by a ray to a random point on
// it (the sun's or the moon's disc, the lamp's bulb), so shadows soften with distance from what casts them. The key light is
// always checked (unshadowed, the sun would light every room a bounced ray looks into); shadows: the lamps are too.
float3 Direct(Surface s, float3 p, float3 v, bool shadows, inout uint seed)
{
    float3 c = 0, o = p + s.Normal * 0.01;
    if (SunOn > 0 && dot(s.Normal, SunDir) > 0)
    {
        float3 toSun = normalize(SunDir + InBall(seed) * (Day.z > 0 ? 0.018 : 0.007));
        if (!Occluded(o, toSun, 300)) c += Brdf(s, v, SunDir, SunColor) * Through(o, toSun, 300);
    }
    for (uint k = 0; k < LightCount; k++)
    {
        Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, p, l, d) * LampLit(L, k);
        if (all(e <= 0) || dot(s.Normal, l) <= 0) continue;
        if (shadows)
        {
            float3 q = L.Position + InBall(seed) * L.Radius - o; float dq = length(q);
            if (Occluded(o, q / dq, max(0.01, dq - 0.05))) continue;
            e *= Through(o, q / dq, max(0.01, dq - 0.05));
        }
        c += Brdf(s, v, l, e);
    }
    return c;
}

// What a secondary ray sees, lit without further bounces or shadow rays. matt: the ray gathers light for a matt surface.
float3 Glance(float3 origin, float3 dir, float tmax, inout uint seed, bool matt)
{
    Hit h;
    if (!Trace(origin, dir, tmax, MaskVisible, h)) return SkyLight(dir, matt);
    Material m = Materials[h.Material];
    float3 v = -dir; Surface s = SurfaceAt(h, v);
    if (m.Kind == KWater || m.Kind == KGlass) return SkyColor(reflect(dir, s.Normal)) * 0.3 + s.Emission;
    if (Baking) return s.Emission + Direct(s, h.P, v, true, seed);
    return s.Albedo * AmbientAt(h.P, s.Normal) * (1 - s.Metallic) * 0.5 * s.Occlusion + s.Emission + Direct(s, h.P, v, false, seed);
}

// One camera ray's light: the surfaces it meets, through water and glass and off polished surfaces, up to Bounces deep.
float3 Radiance(float3 origin, float3 dir, inout uint seed)
{
    float3 color = 0, weight = 1; uint puffs = 0;
    for (uint bounce = 0; bounce <= Bounces; bounce++)
    {
        Hit h;
        if (!Trace(origin, dir, 400, MaskVisible, h)) { color += weight * SkyLight(dir, false); break; }
        Material m = Materials[h.Material];
        float3 v = -dir; Surface s = SurfaceAt(h, v);

        if (m.Kind == KSmoke)
        {
            // a puff of steam: it hides a little of what is behind it and gives back the light that falls on it; the ray goes
            // straight on (and its passing a puff is no bounce, for the first dozen)
            float a = Puff(m, h.N, v); Surface white = s; white.Roughness = 1; white.Metallic = 0; white.Sheen = 0;
            color += weight * a * (m.Base * Ambient(s.Normal) * 0.8 + Direct(white, h.P, v, true, seed) * 0.5);
            weight *= 1 - a; origin = h.P + dir * 0.003;
            if (puffs++ < 12) bounce--;
            continue;
        }

        if (m.Kind == KWater && abs(h.N.y) <= 0.7)
        {
            // falling water (a sheet between two basins, the fountain's streams): the ray bends going in and again coming out,
            // or is turned back inside it
            float3 n = Running(h.N, h.P); bool entering = dot(n, dir) < 0; if (!entering) n = -n;
            float3 r = refract(dir, n, entering ? 1 / 1.33 : 1.33);
            if (all(r == 0)) { origin = h.P + n * 0.004; dir = reflect(dir, n); continue; }
            if (entering)
            {
                float f = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
                color += weight * f * Glance(h.P + n * 0.004, reflect(dir, n), 400, seed, false);
                // running water is full of air: part of the light that falls on it comes back white
                Surface white = s; white.Albedo = float3(0.82, 0.92, 0.96); white.Roughness = 0.5; white.Metallic = 0; white.Normal = n; white.Sheen = 0;
                color += weight * (1 - f) * 0.45 * (white.Albedo * AmbientAt(h.P, n) * 0.6 + Direct(white, h.P, v, true, seed));
                weight *= (1 - f) * 0.55 * float3(0.93, 0.98, 0.98);
            }
            origin = h.P - n * 0.004; dir = r; continue;
        }
        if (m.Kind == KWater)
        {
            float3 n = WaterNormal(h.P, Time); if (dot(n, v) < 0) n = -n;
            float f = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
            color += weight * f * Glance(h.P + n * 0.01, reflect(dir, n), 400, seed, false);
            float3 r = refract(dir, n, 1 / 1.33); if (all(r == 0)) break;
            weight *= (1 - f) * float3(0.75, 0.92, 0.9);
            origin = h.P - n * 0.01; dir = r; continue;
        }
        if (m.Kind == KGlass)
        {
            float f = 0.04 + 0.96 * pow(1 - saturate(dot(s.Normal, v)), 5);
            color += weight * (s.Emission + f * Glance(h.P + s.Normal * 0.01, reflect(dir, s.Normal), 400, seed, false));
            weight *= (1 - f) * lerp(1, StainTint(m), 0.8) * (m.Alpha < 1 ? m.Alpha + 0.4 : 1);
            origin = h.P + dir * 0.01; continue;   // thin glass: straight on
        }

        // light bounced off what is near: one diffuse ray on the first surface (deeper ones use the ambient term alone)
        float3 bounced = bounce == 0 ? Glance(h.P + s.Normal * 0.01, CosineAround(s.Normal, seed), 40, seed, true) : AmbientAt(h.P, s.Normal) * 0.6;
        float3 around = Baking ? (bounce == 0 ? bounced : 0) : bounced * 0.8 + AmbientAt(h.P, s.Normal) * 0.15;
        float3 lit = s.Albedo * (1 - s.Metallic) * around * s.Occlusion + s.Emission + Direct(s, h.P, v, true, seed);
        if (bounce == 0 && !Baking) lit = Haze(lit, h.P);
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

// The light volume (GardenLightVolume), worked out for the rasteriser: for each point of the grid and each of the six ways a surface
// there can face, the mean of the light Last rays find, sent out round that direction as a matt surface would gather them. Each
// dispatch does some rows of the list (row Step on, BakeRow entries a row) into Ping: the light, and the share of the rays that met
// the back of a surface - a point that sees mostly backs is inside a wall.
static const uint BakeRow = 1024;
[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Bake(uint3 id : SV_DispatchThreadID)
{
    uint3 n = (uint3)Grid2.xyz; uint index = (id.y + Step) * BakeRow + id.x;
    if (id.x >= BakeRow || index >= n.x * n.y * n.z * 6) return;
    uint face = index % 6, at = index / 6;
    float3 p = Grid.xyz + float3(at % n.x, at / n.x % n.y, at / (n.x * n.y)) * Grid.w;
    float3 axis = float3(face / 2 == 0, face / 2 == 1, face / 2 == 2) * (face % 2 == 0 ? 1 : -1);
    uint seed = Hash(index * 9781 + 7); float3 sum = 0; float back = 0;
    for (uint k = 0; k < Last; k++)
    {
        float3 dir = CosineAround(axis, seed); Hit h;
        if (!Trace(p, dir, 400, MaskVisible, h)) { sum += min(SkyLight(dir, true), 8); continue; }
        if (dot(h.N, dir) > 0 && Materials[h.Material].Kind != KCutout) back += 1;
        sum += min(Radiance(p, dir, seed), 8);
    }
    Ping[index] = float4(sum / Last, back / Last);
}
