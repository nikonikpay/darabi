// Visual GPU test, DirectX Raytracing (GpuSceneExecutor + GardenRay.cs): the courtyard through a day and a night, every pixel ray
// traced through the GPU's ray-tracing hardware (DXR 1.1 inline RayQuery in a compute shader). Samples camera rays a pixel (stratified,
// so edges are antialiased); at each surface a shadow ray to the sun or the moon and, once they are lit, to every lamp in reach, aimed
// at a random point on the light (soft shadows, which cross the garden with the sun), coloured where it has come through a stained
// pane of the hall's windows - the orsi's colours lie on the floor and move over it; one diffuse ray for the light bounced off nearby surfaces (global illumination); leaves are cut out of their cards
// during traversal; the water reflects and refracts (into the lit pool), glass lets light through and mirrors, metal and polished stone
// reflect (the mirror sphere gliding round the pool most of all) - up to Bounces deep. After the denoiser the frame goes through the
// same lens as the rasteriser's: what is out of focus blurred, a glow round the lamps and the moon, the tone curve.
// The random numbers come from a hash of the pixel, the sample and the frame's Time, never the clock: the same Time gives the same image.
// Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "CBV(b1), SRV(t0), SRV(t1), SRV(t2), SRV(t3), SRV(t4), SRV(t5), SRV(t6), SRV(t7), DescriptorTable(SRV(t8, numDescriptors=2)), UAV(u0), UAV(u1), UAV(u2), UAV(u3), RootConstants(num32BitConstants=4, b2), UAV(u4), UAV(u5), " \
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
Texture2D<float4> BackdropImage : register(t9);
RWStructuredBuffer<uint> Pixels : register(u0);
RWStructuredBuffer<float4> Ping : register(u1);    // the denoiser's light, as it goes from pass to pass (Ping -> Pong -> Ping ...)
RWStructuredBuffer<float4> Guide : register(u2);   // per pixel: first surface's normal and distance, then its colour
RWStructuredBuffer<float4> Pong : register(u3);
RWStructuredBuffer<float4> PrevGuide : register(u4);   // the frame shown before: each pixel's first surface (its normal in two numbers, how light its own colour is or -1 for water, glass and what glows, its distance)
RWStructuredBuffer<float4> PrevLight : register(u5);   // and the light gathered for it so far (alpha: over how many frames)
cbuffer Pass : register(b2) { uint Step; uint Last; uint Stage; uint Keep; };   // Step: tap spacing, bit 16 set when this pass reads Pong; Stage: the lens's pass; Keep: this pass keeps the frame for the next
SamplerState Linear : register(s0);

float3 Mountains(float2 uv) { return BackdropImage.SampleLevel(Linear, uv, 0).rgb; }

static bool Baking = false;   // the light volume's baker sees from no eye (no air between one and the surface); it follows light for two bounces with shadow rays at both, and adds no stand-in for the rest
static const uint MaskVisible = 1, MaskShadow = 2, MaskTint = 4;   // what a camera ray meets; what blocks light; the stained panes, which colour it

// T, B: the directions the texture's u and v run in over the triangle; Lod: the mip level at which a texel is the size of a pixel there
struct Hit { float3 P; float3 N; float2 Uv; uint Material; float3 T; float3 B; float Lod; uint Flags; };

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
    Material mat = Materials[s.Material];
    return (a + (b - a) * bary.x + (c - a) * bary.y) * (mat.Pattern.x > 0 ? mat.Pattern.xy : 1);
}

// A leaf card is a hit only where its texture is opaque.
bool AlphaPass(uint instance, uint geometry, uint prim, float2 bary)
{
    MeshInfo m = Meshes[Instances[instance].Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    Material mat = Materials[s.Material];
    if (mat.Texture < 0) return true;
    return Textures.SampleLevel(Linear, float3(UvAt(instance, geometry, prim, bary), mat.Texture), 1).a >= 0.5;
}

Hit Fetch(uint instance, uint geometry, uint prim, float2 bary, float3 p, float dist)
{
    Instance inst = Instances[instance]; MeshInfo m = Meshes[inst.Mesh]; SubInfo s = Subs[m.FirstSubmesh + geometry];
    uint3 t = Triangle(s, prim);
    uint3 o = (m.BaseVertex + t) * 16;
    float3 na = DecodeNormal(Vertices.Load(o.x + 8)), nb = DecodeNormal(Vertices.Load(o.y + 8)), nc = DecodeNormal(Vertices.Load(o.z + 8));
    float2 ua = DecodeUv(Vertices.Load(o.x + 12)), ub = DecodeUv(Vertices.Load(o.y + 12)), uc = DecodeUv(Vertices.Load(o.z + 12));
    Hit h; h.P = p; h.Material = s.Material; h.Flags = inst.Flags;
    h.N = normalize(mul(Rotation(inst), na + (nb - na) * bary.x + (nc - na) * bary.y));
    if (inst.Flags & FLogo) h.N = LogoTurn(h.N);
    h.Uv = ua + (ub - ua) * bary.x + (uc - ua) * bary.y;
    float3 pa = DecodePosition(Vertices.Load2(o.x), 0, m.Extent), e1 = mul(Rotation(inst), DecodePosition(Vertices.Load2(o.y), 0, m.Extent) - pa), e2 = mul(Rotation(inst), DecodePosition(Vertices.Load2(o.z), 0, m.Extent) - pa);
    float2 d1 = ub - ua, d2 = uc - ua; float det = d1.x * d2.y - d2.x * d1.y, area = length(cross(e1, e2));
    h.T = 0; h.B = 0; h.Lod = 0;
    uint w, hh, layers, levels; Textures.GetDimensions(0, w, hh, layers, levels);
    Material mat = Materials[s.Material]; bool world; float2 uv = TexCoords(mat, h.Uv, p, h.N, world);
    if (world)   // laid by world position: u and v run along the world's own axes on this face
    {
        float3 a = abs(h.N);
        h.T = a.y > 0.6 || a.x <= 0.6 ? float3(1, 0, 0) : float3(0, 0, 1); h.B = a.y > 0.6 ? float3(0, 0, 1) : float3(0, 1, 0);
        h.Lod = clamp(log2(max(mat.Pattern.z * w * dist * 2 * TanHalfFovY / Height, 1e-6)) - 0.5, 0, levels - 1);
    }
    else if (abs(det) > 1e-12 && area > 1e-12)
    {
        h.T = (e1 * d2.y - e2 * d1.y) / det; h.B = (e2 * d1.x - e1 * d2.x) / det;
        if (inst.Flags & FLogo) { h.T = LogoTurn(h.T); h.B = LogoTurn(h.B); }
        float repeats = mat.Kind <= KCutout && mat.Pattern.x > 0 ? max(mat.Pattern.x, mat.Pattern.y) : 1;
        // texels a metre (the triangle's share of the texture over its size) times the metres a pixel spans at this distance
        h.Lod = clamp(log2(max(sqrt(abs(det) / area) * repeats * w * dist * 2 * TanHalfFovY / Height, 1e-6)) - 0.5, 0, levels - 1);
    }
    h.Uv = uv;
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
    hit = Fetch(q.CommittedInstanceID(), q.CommittedGeometryIndex(), q.CommittedPrimitiveIndex(), q.CommittedTriangleBarycentrics(), origin + dir * q.CommittedRayT(), q.CommittedRayT());
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
    float4 texel = m.Texture >= 0 ? Textures.SampleLevel(Linear, float3(h.Uv, m.Texture), h.Lod) : 1;
    float3 n = h.N; if (dot(n, v) < 0) n = -n;
    Surface s = MaterialSurface(m, h.P, n, texel);
    if (m.NormalTexture >= 0) Relief(s, h.T, h.B, Textures.SampleLevel(Linear, float3(h.Uv, m.NormalTexture), h.Lod));
    if (h.Flags & FLogo) s.Emission += s.Albedo * Logo.w;   // the logo is a lit sign at night
    return s;
}

// What colour light keeps on its way along a ray: that of the first stained pane it passes (white when it meets none).
float3 Through(float3 origin, float3 dir, float tmax)
{
    RayDesc ray; ray.Origin = origin; ray.Direction = dir; ray.TMin = 0.002; ray.TMax = tmax;
    RayQuery<RAY_FLAG_FORCE_OPAQUE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, MaskTint, ray); q.Proceed();
    if (q.CommittedStatus() != COMMITTED_TRIANGLE_HIT) return 1;
    MeshInfo m = Meshes[Instances[q.CommittedInstanceID()].Mesh];
    Material mat = Materials[Subs[m.FirstSubmesh + q.CommittedGeometryIndex()].Material];
    return mat.LightTint > 0 ? StainTint(mat) : 1;
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

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    uint n = max(1, Samples), side = (uint)ceil(sqrt((float)n));
    float3 color = 0; uint i = id.y * Width + id.x;
    {   // the denoiser's guide: what the pixel's centre ray meets first
        Hit h0; float2 ndc = (float2(id.xy) + 0.5) / float2(Width, Height) * 2 - 1;
        float3 d0 = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect - CamUp * ndc.y * TanHalfFovY);
        if (Trace(Eye, d0, 400, MaskVisible, h0))
        {
            Material m0 = Materials[h0.Material]; Surface s0 = SurfaceAt(h0, -d0);
            bool plain = m0.Kind != KWater && m0.Kind != KGlass && m0.Kind != KEmissive && m0.Kind != KSmoke;
            Guide[i * 2] = float4(s0.Normal, length(h0.P - Eye)); Guide[i * 2 + 1] = float4(plain ? max(s0.Albedo, 0.03) : 1, plain ? 1 : 0);
        }
        else { Guide[i * 2] = float4(0, 0, 0, -1); Guide[i * 2 + 1] = 1; }
    }
    for (uint k = 0; k < n; k++)
    {
        uint seed = Hash(id.y * 8191 + id.x * 131071 + k * 524287 + 17 + Hash(asuint(Time)));   // other rays at another moment, the same at the same
        float2 jitter = n == 1 ? 0.5 : (float2(k % side, k / side) + float2(Rand(seed), Rand(seed))) / side;   // stratified within the pixel
        float2 ndc = (float2(id.xy) + jitter) / float2(Width, Height) * 2 - 1;
        float3 dir = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect - CamUp * ndc.y * TanHalfFovY);
        color += Radiance(Eye, dir, seed);
    }
    Ping[i] = float4(color / n / Guide[i * 2 + 1].rgb, 0);   // light without the surface's own colour, so the filter keeps texture detail
}

// A direction as two numbers (octahedral), and back.
float2 Squeezed(float3 n) { float2 p = n.xy / (abs(n.x) + abs(n.y) + abs(n.z)); return n.z >= 0 ? p : (1 - abs(p.yx)) * float2(p.x >= 0 ? 1 : -1, p.y >= 0 ? 1 : -1); }
float3 Unsqueezed(float2 p)
{
    float3 n = float3(p, 1 - abs(p.x) - abs(p.y));
    if (n.z < 0) n.xy = (1 - abs(n.yx)) * float2(n.x >= 0 ? 1 : -1, n.y >= 0 ? 1 : -1);
    return normalize(n);
}

// The denoiser's first half, for frames shown one after another: a pixel's light is added to what the frames before gathered for the
// surface it shows. That surface is looked for in the last frame through that frame's view; the light kept there counts if what was
// kept is the same surface (as far from that eye as this one is, facing the same way, about as light in its own colour - the light
// is kept without that colour, and a dark lattice's would flood the pane behind it), blended from the four pixels round where it
// falls. Up to eight frames' light is held, so a pixel's few rays become some thirty and the speckle no longer crawls; a surface
// just come into view starts afresh, and so does water, glass and what glows every frame (what is seen in or through them does not
// stay where their surface does). A frame drawn on its own skips this pass.
[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Gather(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    uint i = id.y * Width + id.x; float4 g = Guide[i * 2], own = Guide[i * 2 + 1]; float3 c = Ping[i].rgb; float frames = 1;
    if (g.w > 0 && own.a > 0.5 && PrevEye.w > 0)
    {
        float light = dot(own.rgb, 1.0 / 3);
        float2 ndc = (float2(id.xy) + 0.5) / float2(Width, Height) * 2 - 1;
        float3 p = Eye + normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect - CamUp * ndc.y * TanHalfFovY) * g.w;
        float4 clip = mul(PrevViewProj, float4(p, 1));
        if (clip.w > 0.01)
        {
            float2 q = (clip.xy / clip.w * float2(0.5, -0.5) + 0.5) * float2(Width, Height) - 0.5; int2 q0 = (int2)floor(q); float2 f = q - q0;
            float was = length(p - PrevEye.xyz); float3 sum = 0; float held = 0, weight = 0;
            [unroll] for (int k = 0; k < 4; k++)
            {
                int2 t = q0 + int2(k & 1, k >> 1);
                if (any(t < 0) || t.x >= (int)Width || t.y >= (int)Height) continue;
                uint j = t.y * Width + t.x; float4 pg = PrevGuide[j];
                if (pg.w <= 0 || pg.z <= 0 || abs(pg.w - was) > 0.03 * was + 0.03 || max(pg.z, light) > 2 * min(pg.z, light) || dot(Unsqueezed(pg.xy), g.xyz) < 0.9) continue;
                float w = ((k & 1) ? f.x : 1 - f.x) * ((k >> 1) ? f.y : 1 - f.y); float4 kept = PrevLight[j];
                sum += kept.rgb * w; held += kept.a * w; weight += w;
            }
            if (weight > 0.05) { frames = min(held / weight + 1, 8); c = lerp(sum / weight, c, 1 / frames); }
        }
    }
    Ping[i] = float4(c, frames);
}

// The denoiser: an edge-aware a-trous filter (Dammertz et al. 2010) over the light, a few passes with taps Step apart (1, 2, 4, 8).
// A tap counts for less the more its surface differs (direction, distance) or its light does, so edges, shadows and texture stay
// while the speckle of a few rays a pixel is smoothed out. From this frame's light alone (after Gather, in a shown frame): a frame drawn on its own twice is the same image.
[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Denoise(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= Width || id.y >= Height) return;
    uint i = id.y * Width + id.x; bool fromPing = (Step >> 16) == 0; int step = (int)(Step & 0xFFFF);
    float4 g = Guide[i * 2]; float3 c0 = fromPing ? Ping[i].rgb : Pong[i].rgb;
    if (Keep)   // for the next frame: what this pixel met, and the light gathered for it
    {
        float4 own = Guide[i * 2 + 1];
        PrevGuide[i] = float4(Squeezed(g.xyz), own.a > 0.5 ? dot(own.rgb, 1.0 / 3) : -1, g.w); PrevLight[i] = float4(c0, max(Ping[i].a, 1));
    }
    float3 sum = c0; float wsum = 1;
    if (g.w > 0)
    {
        const float K[3] = { 3.0 / 8, 1.0 / 4, 1.0 / 16 };
        float l0 = dot(c0, float3(0.3, 0.59, 0.11));
        [unroll] for (int y = -2; y <= 2; y++) [unroll] for (int x = -2; x <= 2; x++)
        {
            if (x == 0 && y == 0) continue;
            int2 q = int2(id.xy) + int2(x, y) * step;
            if (any(q < 0) || q.x >= (int)Width || q.y >= (int)Height) continue;
            uint j = q.y * Width + q.x; float4 gj = Guide[j * 2]; if (gj.w <= 0) continue;
            float3 cj = fromPing ? Ping[j].rgb : Pong[j].rgb;
            float w = K[abs(x)] * K[abs(y)] / (K[0] * K[0])
                    * pow(saturate(dot(g.xyz, gj.xyz)), 32)
                    * exp(-abs(gj.w - g.w) / (0.01 * g.w * step + 0.02))
                    * exp(-abs(dot(cj, float3(0.3, 0.59, 0.11)) - l0) / (0.6 * max(l0, 0.02) + 0.03));
            sum += cj * w; wsum += w;
        }
    }
    float3 c = sum / wsum;
    if (Last) c *= Guide[i * 2 + 1].rgb;   // the surface's own colour again: the frame's light, for the lens
    if (fromPing) Pong[i] = float4(c, 0); else Ping[i] = float4(c, 0);
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
    Baking = true;
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

// The lens, as the rasteriser has it (Garden.hlsli): the frame's light is in Ping, and Pong, done with, holds the glow at a quarter
// of the size each way - A, the bright part of the frame (Stage 0: the mean of four by four pixels), blurred across into B and down
// back into A three times over, with taps one, two and four texels apart (Stages 1 to 6) - before the last pass (Stage 7) gathers
// each pixel's blur from the pixels round it, adds the glow and writes the picture.
uint2 GlowSize() { return uint2((Width + 3) / 4, (Height + 3) / 4); }
float3 GlowAt(uint2 q, bool b) { uint2 s = GlowSize(); q = min(q, s - 1); return Pong[(b ? s.x * s.y : 0) + q.y * s.x + q.x].rgb; }

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void Finish(uint3 id : SV_DispatchThreadID)
{
    uint2 gs = GlowSize();
    if (Stage == 0)
    {
        if (id.x >= gs.x || id.y >= gs.y) return;
        float3 c = 0;
        [unroll] for (uint y = 0; y < 4; y++) [unroll] for (uint x = 0; x < 4; x++) { uint2 q = min(id.xy * 4 + uint2(x, y), uint2(Width, Height) - 1); c += Glow(Ping[q.y * Width + q.x].rgb); }
        Pong[id.y * gs.x + id.x] = float4(c / 16, 0);
    }
    else if (Stage < 7)
    {
        if (id.x >= gs.x || id.y >= gs.y) return;
        bool across = (Stage & 1) != 0; int step = (int)(1u << ((Stage - 1) / 2));
        const float K[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
        float3 c = 0;
        [unroll] for (int k = -4; k <= 4; k++) { int2 q = int2(id.xy) + (across ? int2(k, 0) : int2(0, k)) * step; c += GlowAt((uint2)max(q, 0), !across) * K[abs(k)]; }
        Pong[(across ? gs.x * gs.y : 0) + id.y * gs.x + id.x] = float4(c, 0);
    }
    else
    {
        if (id.x >= Width || id.y >= Height) return;
        uint i = id.y * Width + id.x; float3 c = Ping[i].rgb; int taps = (int)Lens.w;
        if (taps > 0)
        {
            float z0 = Guide[i * 2].w; if (z0 <= 0) z0 = 1e6;
            float b0 = Blur(z0), a = Turn(float2(id.xy) + 0.5), n = 1;
            [loop] for (int k = 0; k < taps; k++)
            {
                float r = sqrt((k + 0.5) / taps) * Lens.z, t = k * 2.399963 + a;
                int2 q = clamp(int2(id.xy) + int2(round(float2(cos(t), sin(t)) * r)), 0, int2(Width, Height) - 1); uint j = q.y * Width + q.x;
                float zq = Guide[j * 2].w; if (zq <= 0) zq = 1e6;
                float bq = Blur(zq); if (zq > z0) bq = min(bq, b0 * 2);
                c += lerp(c / n, Ping[j].rgb, smoothstep(r - 0.5, r + 0.5, bq)); n += 1;
            }
            c /= n;
        }
        // the glow, between the four quarter-size texels round this pixel
        float2 g = (float2(id.xy) + 0.5) / 4 - 0.5; int2 g0 = (int2)floor(g); float2 f = g - g0;
        float3 glow = lerp(lerp(GlowAt((uint2)max(g0, 0), false), GlowAt((uint2)max(g0 + int2(1, 0), 0), false), f.x),
                           lerp(GlowAt((uint2)max(g0 + int2(0, 1), 0), false), GlowAt((uint2)max(g0 + 1, 0), false), f.x), f.y);
        uint3 o = (uint3)(saturate(Finished(c + glow * Post.x, id.xy)) * 255 + 0.5);
        Pixels[id.y * Pitch + id.x] = o.r | o.g << 8 | o.b << 16 | 0xFF000000;
    }
}
