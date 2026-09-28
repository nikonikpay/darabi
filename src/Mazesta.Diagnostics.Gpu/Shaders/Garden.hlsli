// Shared by the garden's two renderers (GardenRaster.hlsl, GardenRay.hlsl): the frame constants, the scene's records as GardenScene.cs
// lays them out, the materials (Blender's brick texture included, so the paving and walls look as they do in the .blend), the sky and the
// light falloff. Everything is a pure function of its inputs and the frame's Time: a frame drawn twice at one Time is the same image.

cbuffer Frame : register(b1)
{
    float4x4 ViewProj; float4x4 ShadowViewProj;
    float3 Eye; float Time;
    float3 SunDir; float SunOn;          // toward the sun (or the moon); SunOn 0 = no key light
    float3 SunColor; uint LightCount;    // SunColor: irradiance, colour times strength
    float3 SkyZenith; float WaterLevel;
    float3 SkyHorizon; float Exposure;
    float3 GroundColor; uint Flags;      // bit 0: a reflection image is bound; bit 1: this pass draws the reflection (clip under the water); bit 2: alpha to coverage
    float2 ViewSize; float ShadowTexel; uint ShadowTaps;
    float3 CamRight; float TanHalfFovY;
    float3 CamUp; float Aspect;
    float3 CamForward; uint Bounces;
    uint Width; uint Height; uint Pitch; uint Mode;   // Mode 1: golden hour (Direct3D), 2: blue hour (ray traced)
    float4 Logo;                          // the logo's turn toward the camera (cos, sin) and its float (lift): GardenGpu.LogoMotion
};

struct Instance { float4 Row0; float4 Row1; float4 Row2; uint Mesh; uint Mask; uint Flags; uint Pad; };
struct Material { uint Kind; int Texture; float2 Pad; float3 Base; float Alpha; float3 Color2; float Roughness; float3 Mortar; float Metallic; float3 Emission; float Transmission; float4 Pattern; };
struct Light { float3 Position; uint Kind; float3 Direction; float Range; float3 Color; float CosOuter; float CosInner; float Radius; float2 Pad; };

static const uint KFlat = 0, KCutout = 1, KBrick = 2, KWater = 3, KGlass = 4, KEmissive = 5;
static const uint LSun = 0, LPoint = 1, LSpot = 2;
static const float Pi = 3.14159265;

float3x3 Rotation(Instance i) { return float3x3(i.Row0.xyz, i.Row1.xyz, i.Row2.xyz); }
float3 Translation(Instance i) { return float3(i.Row0.w, i.Row1.w, i.Row2.w); }

// The logo turns about the vertical through its centre to face the camera, so it reads the right way round from anywhere on the walk,
// and floats up and down. The CPU works the turn out (GardenGpu.LogoMotion) and hands it over in Frame.Logo.
float3 LogoMove(float3 p, float3 pivot)
{
    float c = Logo.x, s = Logo.y; float3 d = p - pivot;
    return pivot + float3(c * d.x + s * d.z, d.y + Logo.z, -s * d.x + c * d.z);
}
float3 LogoTurn(float3 n) { float c = Logo.x, s = Logo.y; return float3(c * n.x + s * n.z, n.y, -s * n.x + c * n.z); }

// ——— Blender's brick texture (intern/cycles/kernel/svm/brick.h), offset 0.5 every second row, no squash ———

float BrickNoise(uint n)
{
    n = (n + 1013) & 0x7fffffff; n = (n >> 13) ^ n;
    uint nn = (n * (n * n * 60493 + 19990303) + 1376312589) & 0x7fffffff;
    return 0.5 * ((float)nn / 1073741824.0);
}

// x: mortar (0..1), y: tint between the two brick colours
float2 Brick(float2 p, float mortarSize, float brickWidth, float rowHeight)
{
    int row = (int)floor(p.y / rowHeight);
    float offset = (row % 2) != 0 ? 0 : brickWidth * 0.5;
    int brick = (int)floor((p.x + offset) / brickWidth);
    float x = (p.x + offset) - brickWidth * brick, y = p.y - rowHeight * row;
    float tint = saturate(BrickNoise((uint)((row << 16) + (brick & 0xFFFF))));
    float d = min(min(x, y), min(brickWidth - x, rowHeight - y));
    return float2(d < mortarSize ? 1 : 0, tint);
}

float Hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float Noise3(float3 x)
{
    float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// The brick's 2-D coordinates on a face, picked by where the face points - the .blend's own mapping, in Direct3D axes
// (Blender's x, y, z are x, z, y here): top faces use (x, z), faces along x use (z, y), faces along z use (x, y).
float2 FaceCoords(float3 p, float3 n)
{
    float3 a = abs(n);
    if (a.y > 0.6) return p.xz;
    if (a.x > 0.6) return float2(p.z, p.y);
    return float2(p.x, p.y);
}

struct Surface { float3 Albedo; float Alpha; float Roughness; float Metallic; float3 Emission; float3 Normal; };

Surface MaterialSurface(Material m, float3 p, float3 n, float4 texel)
{
    Surface s; s.Albedo = m.Base; s.Alpha = m.Alpha; s.Roughness = m.Roughness; s.Metallic = m.Metallic; s.Emission = m.Emission; s.Normal = n;
    if (m.Texture >= 0) { s.Albedo *= texel.rgb; s.Alpha *= texel.a; }
    if (m.Kind == KBrick)
    {
        float2 b = Brick(FaceCoords(p, n) * m.Pattern.x, m.Pattern.y, m.Pattern.z, m.Pattern.w);
        s.Albedo = b.x > 0 ? m.Mortar : lerp(m.Base, m.Color2, b.y);
        s.Albedo *= lerp(1, 0.75 + 0.5 * Noise3(p * 6), 0.25);   // the .blend multiplies a noise over it
        if (b.x > 0) s.Roughness = min(1, s.Roughness + 0.2);
    }
    else if (m.Kind == KFlat && m.Texture < 0)
        s.Albedo *= lerp(1, 0.8 + 0.4 * Noise3(p * 4), 0.3);
    return s;
}

// ——— lighting ———

float3 SkyColor(float3 dir)
{
    float up = saturate(dir.y), h = pow(1 - up, 3);
    float3 c = lerp(SkyZenith, SkyHorizon, h);
    float toSun = saturate(dot(dir, SunDir));
    if (Mode == 1) c += SunColor * (0.08 * pow(toSun, 6) + 0.25 * pow(toSun, 64)) + SunColor * 6 * smoothstep(0.9995, 0.9998, toSun);
    else c += float3(0.5, 0.6, 0.9) * (0.06 * pow(toSun, 12) + 3 * smoothstep(0.99975, 0.9999, toSun));
    if (dir.y < 0) c = lerp(c, GroundColor, saturate(-dir.y * 4));
    return c;
}

float3 Ambient(float3 n) { return lerp(GroundColor, lerp(SkyHorizon, SkyZenith, 0.5), saturate(n.y * 0.5 + 0.5)); }

// Radiance leaving a surface lit by one light of irradiance E from direction l: Lambert plus a GGX-shaped highlight.
float3 Brdf(Surface s, float3 v, float3 l, float3 e)
{
    float nl = saturate(dot(s.Normal, l)); if (nl <= 0) return 0;
    float3 h = normalize(l + v); float nh = saturate(dot(s.Normal, h)), a = max(0.03, s.Roughness * s.Roughness), a2 = a * a;
    float d = a2 / (Pi * pow(nh * nh * (a2 - 1) + 1, 2));
    float3 f0 = lerp(0.04, s.Albedo, s.Metallic);
    float3 f = f0 + (1 - f0) * pow(1 - saturate(dot(h, v)), 5);
    float3 spec = f * d * 0.25 / max(0.1, saturate(dot(s.Normal, v)));
    return e * nl * ((1 - s.Metallic) * s.Albedo / Pi + spec);
}

// Irradiance a point or spot light gives at p (0 outside its range), and the direction to it.
float3 LightAt(Light L, float3 p, out float3 l, out float dist)
{
    float3 d = L.Position - p; dist = length(d); l = d / max(dist, 1e-4);
    if (dist > L.Range) return 0;
    float window = pow(saturate(1 - pow(dist / L.Range, 4)), 2);
    float3 e = L.Color / max(dist * dist, L.Radius * L.Radius + 0.01) * window;
    if (L.Kind == LSpot) e *= smoothstep(L.CosOuter, L.CosInner, dot(-l, L.Direction));
    return e;
}

float3 Tonemap(float3 c)
{
    c *= Exposure;
    c = saturate((c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14));   // ACES (Narkowicz)
    return pow(c, 1 / 2.2);
}

float3 WaterNormal(float3 p, float t)
{
    float2 q = p.xz;
    float2 g = float2(0, 0);
    g += 0.030 * float2(cos(q.x * 1.7 + t * 1.3), 0) + 0.025 * float2(0, cos(q.y * 2.1 - t * 1.1));
    g += 0.012 * cos(dot(q, float2(3.1, 2.3)) + t * 2.2) * float2(3.1, 2.3) * 0.3;
    g += 0.008 * cos(dot(q, float2(-4.7, 5.3)) - t * 2.9) * float2(-4.7, 5.3) * 0.2;
    return normalize(float3(-g.x, 1, -g.y));
}

// ——— vertex decoding, for the ray tracer (the rasteriser's input assembler does it in hardware) ———

float3 DecodePosition(uint2 raw, float3 centre, float3 extent)
{
    int3 q = int3((int)(raw.x << 16) >> 16, (int)raw.x >> 16, (int)(raw.y << 16) >> 16);
    return centre + extent * max(float3(q) / 32767.0, -1);
}
float3 DecodeNormal(uint raw)
{
    int3 q = int3((int)(raw << 24) >> 24, (int)(raw << 16) >> 24, (int)(raw << 8) >> 24);
    return normalize(max(float3(q) / 127.0, -1) + 1e-6);
}
float2 DecodeUv(uint raw) { return float2(f16tof32(raw & 0xFFFF), f16tof32(raw >> 16)); }
