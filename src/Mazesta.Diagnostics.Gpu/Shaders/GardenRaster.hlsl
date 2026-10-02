// Visual GPU test, Direct3D 12 rasterisation (GpuSceneExecutor + GardenRaster.cs): the Persian garden at golden hour.
// Passes: the sun's shadow map (depth only), the pool's mirror image (the scene drawn again from the eye reflected in the water, when the
// load level asks for it), then the frame itself - sky, solid geometry, leaves (alpha tested, or alpha to coverage under MSAA) and last
// the see-through water and glass. Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), RootConstants(num32BitConstants=12, b0), CBV(b1), SRV(t0), SRV(t1), SRV(t2), " \
           "DescriptorTable(SRV(t3, numDescriptors=3)), " \
           "StaticSampler(s0, filter=FILTER_ANISOTROPIC, maxAnisotropy=8), " \
           "StaticSampler(s1, filter=FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT, addressU=TEXTURE_ADDRESS_BORDER, addressV=TEXTURE_ADDRESS_BORDER, borderColor=STATIC_BORDER_COLOR_OPAQUE_WHITE, comparisonFunc=COMPARISON_LESS_EQUAL), " \
           "StaticSampler(s2, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP)"

#include "Garden.hlsli"

cbuffer Draw : register(b0) { uint InstanceBase; uint MaterialIndex; uint2 DrawPad; float3 Centre; float DrawPad2; float3 Extent; float DrawPad3; };
StructuredBuffer<Instance> Instances : register(t0);
StructuredBuffer<Material> Materials : register(t1);
StructuredBuffer<Light> Lights : register(t2);
Texture2DArray<float4> Textures : register(t3);
Texture2D<float> ShadowMap : register(t4);
Texture2D<float4> Reflection : register(t5);
SamplerState Aniso : register(s0);
SamplerComparisonState ShadowSampler : register(s1);
SamplerState Clamp : register(s2);

struct VIn { float4 Position : POSITION; float4 Normal : NORMAL; float2 Uv : TEXCOORD; };
struct VOut { float4 Position : SV_Position; float3 World : WORLD; float3 Normal : NORMAL; float2 Uv : TEXCOORD; float Out : OUTWARD; nointerpolation uint Id : INSTANCE; };

void Place(VIn v, uint instance, out float3 world, out float3 normal)
{
    Instance i = Instances[InstanceBase + instance];
    float3 local = Centre + Extent * v.Position.xyz;
    world = mul(Rotation(i), local) + Translation(i);
    normal = mul(Rotation(i), v.Normal.xyz);
    if (i.Flags & 1) { float3 pivot = Translation(i); world = LogoMove(world, pivot); normal = LogoTurn(normal); }
}

[RootSignature(RS)]
VOut MainVS(VIn v, uint instance : SV_InstanceID)
{
    VOut o; Place(v, instance, o.World, o.Normal);
    o.Position = mul(ViewProj, float4(o.World, 1)); o.Uv = v.Uv;
    o.Out = saturate(length(v.Position.xyz)); o.Id = InstanceBase + instance;   // how far out in its mesh's bounds: a leaf deep in a crown is shaded by the rest
    return o;
}

struct ShadowOut { float4 Position : SV_Position; float2 Uv : TEXCOORD; };

[RootSignature(RS)]
ShadowOut ShadowVS(VIn v, uint instance : SV_InstanceID)
{
    float3 w, n; Place(v, instance, w, n);
    ShadowOut o; o.Position = mul(ShadowViewProj, float4(w, 1)); o.Uv = v.Uv; return o;
}

// Leaves cut out of their cards cast leaf-shaped shadows.
void ShadowPS(ShadowOut i)
{
    Material m = Materials[MaterialIndex];
    if (m.Kind == KCutout && Textures.Sample(Aniso, float3(i.Uv, m.Texture)).a < 0.5) discard;
}

float Shadow(float3 p, float3 n)
{
    float4 s = mul(ShadowViewProj, float4(p + n * 0.04, 1));
    float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0) || any(uv > 1)) return 1;
    float depth = s.z - 0.0008, sum = 0; int r = (int)ShadowTaps;
    [loop] for (int y = -r; y <= r; y++) [loop] for (int x = -r; x <= r; x++) sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * ShadowTexel, depth);
    return sum / ((2 * r + 1) * (2 * r + 1));
}

float3 Lit(Surface s, float3 p, float3 v)
{
    // metal has no diffuse light of its own; a little of its surroundings' light stands in for the reflections a rasteriser cannot trace
    float3 c = s.Albedo * Ambient(s.Normal) * (1 - s.Metallic * 0.6) + s.Emission;
    if (SunOn > 0) c += Brdf(s, v, SunDir, SunColor) * Shadow(p, s.Normal);
    [loop] for (uint k = 0; k < LightCount; k++)
    {
        Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, p, l, d);
        if (any(e > 0)) c += Brdf(s, v, l, e);
    }
    // metals and polished floors mirror the sky
    float3 r = reflect(-v, s.Normal); float gloss = saturate(1 - s.Roughness * 2.5);
    c += SkyColor(r) * lerp(0.04, 1, s.Metallic) * gloss * lerp(1, s.Albedo, s.Metallic);
    return c;
}

float4 Opaque(VOut i, bool cutout)
{
    if ((Flags & 2) && i.World.y < WaterLevel - 0.02) discard;   // the mirror image holds only what is above the water
    Material m = Materials[MaterialIndex];
    float4 texel = m.Texture >= 0 ? Textures.Sample(Aniso, float3(i.Uv, m.Texture)) : 1;
    float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;   // two-sided: leaves and cards are seen from both faces
    Surface s = MaterialSurface(m, i.World, n, texel);
    float alpha = 1;
    if (cutout)
    {
        if (Flags & 4) alpha = saturate((s.Alpha - 0.5) / max(fwidth(s.Alpha), 1e-4) + 0.5);   // alpha to coverage: a crisp edge, smoothed by the samples
        else if (s.Alpha < 0.5) discard;
        // Foliage: no two plants quite the same green, a little less saturated than the card's photograph, darker inside the crown
        // (which the sky barely reaches), and the sun shining through a leaf seen against it.
        float h = Hash1(i.Id * 0.618);
        s.Albedo *= lerp(float3(0.82, 0.86, 0.80), float3(1.08, 1.04, 0.92), h);
        s.Albedo = lerp(dot(s.Albedo, float3(0.3, 0.59, 0.11)), s.Albedo, 0.74);
        float inner = lerp(0.42, 1, i.Out * i.Out);
        float3 c = Lit(s, i.World, v) - s.Albedo * Ambient(s.Normal) * (1 - inner);
        if (SunOn > 0) c += s.Albedo * s.Albedo * SunColor * 0.35 * saturate(-dot(n, SunDir)) * Shadow(i.World, -n) * inner;
        return float4(Tonemap(c), alpha);
    }
    return float4(Tonemap(Lit(s, i.World, v)), alpha);
}

float4 OpaquePS(VOut i) : SV_Target { return Opaque(i, false); }
float4 CutoutPS(VOut i) : SV_Target { return Opaque(i, true); }

// Water and glass: blended over what is behind them.
float4 TransparentPS(VOut i) : SV_Target
{
    if ((Flags & 2) && i.World.y < WaterLevel - 0.02) discard;
    Material m = Materials[MaterialIndex];
    float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;
    if (m.Kind == KWater)
    {
        n = WaterNormal(i.World, Time);
        float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
        float3 reflected;
        if ((Flags & 1) && abs(i.World.y - WaterLevel) < 0.2)
        {
            float2 uv = i.Position.xy / ViewSize + n.xz * 0.04;
            reflected = Reflection.SampleLevel(Clamp, uv, 0).rgb;   // already tone-mapped
            float3 sunGlint = pow(saturate(dot(reflect(-v, n), SunDir)), 400) * SunColor * SunOn;
            reflected += Tonemap(sunGlint);
        }
        else reflected = Tonemap(Sky(reflect(-v, n)) + pow(saturate(dot(reflect(-v, n), SunDir)), 400) * SunColor * SunOn);
        // the pool's tiles show through (blended at 1 - a); the water adds its mirror image and a faint teal body
        float3 body = Tonemap(m.Base * Ambient(float3(0, 1, 0)) * 0.5);
        float a = fresnel + (1 - fresnel) * 0.22;
        return float4((reflected * fresnel + body * (1 - fresnel) * 0.22) / a, a);
    }
    // a window: the room behind it, through the pane's colour, and the courtyard's sky mirrored on it - drawn whole, not blended
    if (m.Kind == KGlass && m.Pattern.x > 0)
    {
        float f = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
        float3 tint = StainTint(m), behind = Interior(m, i.World, -v, n) * tint;
        float3 c = behind * (1 - f) + Sky(reflect(-v, n)) * max(f, 0.06) + tint * Ambient(n) * 0.08;   // a little light caught in the colour
        c += pow(saturate(dot(reflect(-v, n), SunDir)), 300) * SunColor * SunOn * 0.5;   // the sun's glint
        return float4(Tonemap(c), 1);
    }
    // glass and spray: a tinted sheen and highlights
    Surface s; s.Albedo = m.Base; s.Alpha = m.Alpha; s.Roughness = m.Roughness; s.Metallic = 0; s.Emission = m.Emission; s.Normal = n;
    float fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
    float3 c = s.Emission + SkyColor(reflect(-v, n)) * fresnel;
    if (SunOn > 0) c += Brdf(s, v, SunDir, SunColor) * 0.5;
    [loop] for (uint k = 0; k < LightCount; k++) { Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, i.World, l, d); if (any(e > 0)) c += Brdf(s, v, l, e) * 0.5; }
    float a = m.Kind == KGlass ? saturate(max(0.18, fresnel) + max(max(m.Emission.r, m.Emission.g), m.Emission.b) * 0.05) : 0.5;
    if (m.Alpha < 1) a = m.Alpha;
    return float4(Tonemap(c), a);
}

// The sky: a full-screen triangle behind everything.
struct SkyOut { float4 Position : SV_Position; float2 Ndc : NDC; };

[RootSignature(RS)]
SkyOut SkyVS(uint vertex : SV_VertexID)
{
    float2 uv = float2((vertex << 1) & 2, vertex & 2);
    SkyOut o; o.Ndc = uv * float2(2, -2) + float2(-1, 1); o.Position = float4(o.Ndc, 1, 1); return o;
}

float4 SkyPS(SkyOut i) : SV_Target
{
    float3 dir = normalize(CamForward + CamRight * i.Ndc.x * TanHalfFovY * Aspect + CamUp * i.Ndc.y * TanHalfFovY);
    return float4(Tonemap(Sky(dir)), 1);
}
