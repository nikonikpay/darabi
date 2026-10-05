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
    uint Samples; uint3 FramePad;         // ray tracer: camera rays a pixel
    float4 Backdrop;                      // the mountains round the horizon: the heights they cover as tangents (low, high), and 1 when they are there
};

struct Instance { float4 Row0; float4 Row1; float4 Row2; uint Mesh; uint Mask; uint Flags; uint Pad; };
struct Material { uint Kind; int Texture; float RoomOffset; int NormalTexture; float3 Base; float Alpha; float3 Color2; float Roughness; float3 Mortar; float Metallic; float3 Emission; float Transmission; float4 Pattern; };
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
    if (m.Texture >= 0)   // alpha: a leaf card's opacity, any other surface's roughness
    {
        s.Albedo *= texel.rgb;
        if (m.Kind == KCutout) s.Alpha *= texel.a; else s.Roughness = texel.a;
    }
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

// A normal map's direction (tangent space, x in alpha and y in green) on a surface whose texture runs along t (its u) and b (its v):
// the stone's joints, the plaster's straw and the wood's grain catch the light as relief, on flat triangles.
float3 Bumped(float3 n, float3 t, float3 b, float4 texel)
{
    float2 xy = float2(texel.a, texel.g) * 2 - 1;
    t -= n * dot(n, t); b -= n * dot(n, b);
    float lt = dot(t, t), lb = dot(b, b); if (lt < 1e-12 || lb < 1e-12) return n;
    return normalize(t * rsqrt(lt) * xy.x + b * rsqrt(lb) * xy.y + n * sqrt(saturate(1 - dot(xy, xy))));
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

// Clouds over the sky gradient: a few octaves of value noise on a plane high above, drifting slowly with Time (so a frame drawn twice
// at one Time is the same), thinning toward the horizon, lit on the sun's side. Used where the sky itself is seen (background, the water's
// mirror, a ray that leaves the scene); the glossy sheen on surfaces keeps the plain gradient, which is what they would blur it to.
float Fbm(float3 p) { float a = 0.5, f = 0; [unroll] for (int k = 0; k < 5; k++) { f += a * Noise3(p); p = p * 2.03 + 17.1; a *= 0.5; } return f; }
float3 Clouds(float3 c, float3 dir)
{
    float2 q = dir.xz / (dir.y + 0.12) * 1.6 + float2(Time * 0.004, Time * 0.0015);
    float d = Fbm(float3(q, 3.7)), cover = smoothstep(0.42, 0.72, d) * smoothstep(0.01, 0.18, dir.y);
    if (cover <= 0) return c;
    float toSun = saturate(dot(dir, SunDir)), thick = smoothstep(0.5, 0.85, Fbm(float3(q * 1.7, 9.1)));
    float3 lit = Mode == 1 ? dot(SkyHorizon, 0.333) * float3(1.55, 1.42, 1.3) + SunColor * (0.04 + 0.12 * pow(toSun, 8))
                           : dot(SkyZenith, 0.333) * float3(1.1, 1.15, 1.45) + float3(0.5, 0.6, 0.9) * 0.05 * pow(toSun, 8);
    return lerp(c, lit * lerp(1, 0.62, thick), cover * 0.92);
}

// The mountains round the horizon (GardenBackdrop: a picture all the way round, its rows the tangent of the height): they stand in
// front of the low sky and its clouds and give way to them above the peaks. At blue hour they are the same range after dusk.
// Each renderer binds the picture and samples it its own way.
float3 Mountains(float2 uv);
float3 Sky(float3 dir)
{
    float3 c = SkyColor(dir);
    if (dir.y > 0.01) c = Clouds(c, dir);
    if (Backdrop.z <= 0) return c;
    float t = dir.y / max(length(dir.xz), 1e-4), span = Backdrop.y - Backdrop.x;
    float w = (1 - smoothstep(Backdrop.x + span * 0.55, Backdrop.x + span * 0.95, t)) * saturate(1 + (t - Backdrop.x) * 25);   // under the horizon the ground's haze takes over
    if (w <= 0) return c;
    float3 m = Mountains(float2(atan2(dir.x, dir.z) / (2 * Pi) + 0.5, clamp((t - Backdrop.x) / span, 0.004, 0.996)));
    m = Mode == 1 ? m * 1.15 : lerp(dot(m, float3(0.3, 0.59, 0.11)), m, 0.3) * float3(0.07, 0.09, 0.18);
    return lerp(c, m, w);
}

// The sky's light on a surface. By day it is about half the bright sky's colour, so the sun and its shadows give the courtyard its shape.
float3 Ambient(float3 n) { return lerp(GroundColor, lerp(SkyHorizon, SkyZenith, 0.5), saturate(n.y * 0.5 + 0.5)) * (Mode == 1 ? 0.5 : 1.0); }

// ——— a room behind a window (interior mapping) ———
// A window's glass shows a furnished room that is not there: the view ray is carried on into a box behind the pane (one bay of the
// building wide, Pattern's depth deep, from its floor to its ceiling) and the wall, floor or ceiling it meets is painted here - plaster
// over a tiled dado, a carpet on a wooden floor, a niche and a cushioned bench on the back wall, a lamp. Each bay gets its own colours.
// Pattern: bay width, room depth, floor height, ceiling height; RoomOffset: where a bay starts along the facade.
float Hash1(float x) { return frac(sin(x * 127.1 + 311.7) * 43758.5453); }
float3 Interior(Material m, float3 p, float3 dir, float3 nOut)
{
    float3 inward = -nOut; float3 along = abs(inward.x) > abs(inward.z) ? float3(0, 0, 1) : float3(1, 0, 0);
    float W = m.Pattern.x, D = m.Pattern.y, F = m.Pattern.z, H = m.Pattern.w - m.Pattern.z;
    float u = dot(p, along) - m.RoomOffset, bay = floor(u / W); u -= bay * W;
    float3 o = float3(u, p.y - F, 0), d = float3(dot(dir, along), dir.y, dot(dir, inward));
    float tu = d.x > 0 ? (W - o.x) / d.x : -o.x / min(d.x, -1e-5);
    float tv = d.y > 0 ? (H - o.y) / d.y : -o.y / min(d.y, -1e-5);
    float tw = D / max(d.z, 1e-5), t = min(tu, min(tv, tw));
    float3 h = o + d * t;
    float r1 = Hash1(bay), r2 = Hash1(bay + 31.7), r3 = Hash1(bay + 77.3);
    float3 plaster = lerp(float3(0.62, 0.52, 0.40), float3(0.55, 0.47, 0.42), r1), c;
    if (t == tv && d.y < 0)   // floor: walnut boards under a carpet with a border
    {
        float2 f = float2(h.x, h.z);
        c = float3(0.20, 0.10, 0.05) * (0.8 + 0.4 * Hash1(floor(f.x / 0.18) + bay * 13));
        float2 rug = abs(f - float2(W * 0.5, D * 0.55)) - float2(W * 0.36, D * 0.3);
        if (max(rug.x, rug.y) < 0)
        {
            float3 field = lerp(float3(0.42, 0.06, 0.05), float3(0.10, 0.12, 0.32), step(0.6, r2));
            float edge = -max(rug.x, rug.y);
            c = edge < 0.12 ? float3(0.55, 0.42, 0.18) : field * (0.8 + 0.25 * Noise3(float3(f * 9, bay)));
            float2 med = (f - float2(W * 0.5, D * 0.55)) / float2(W * 0.36, D * 0.3);
            if (length(med * float2(1, 0.8)) < 0.4) c = float3(0.62, 0.48, 0.22);
        }
    }
    else if (t == tv)          // ceiling: wooden beams
        c = frac(h.z / 0.6) < 0.25 ? float3(0.16, 0.08, 0.04) : plaster * 0.85;
    else
    {
        float2 w = t == tw ? float2(h.x, h.y) : float2(h.z, h.y);   // across the wall, up it
        float span = t == tw ? W : D;
        c = h.y < 1.0 ? lerp(float3(0.08, 0.28, 0.42), float3(0.85, 0.80, 0.68), step(0.5, frac((floor(w.x / 0.2) + floor(h.y / 0.2)) * 0.5))) : plaster;
        if (h.y > 0.98 && h.y < 1.04) c = float3(0.45, 0.36, 0.20);
        if (t == tw)
        {
            float2 n = float2(abs(w.x - span * 0.5), w.y - 1.5);   // a pointed niche
            if (n.x < span * 0.18 && n.y > 0 && n.y < 1.4 + 0.25 * (1 - n.x / (span * 0.18))) c = plaster * 0.45;
            if (w.y < 0.55 && abs(w.x - span * 0.5) < span * 0.42) c = lerp(float3(0.45, 0.10, 0.08), float3(0.20, 0.30, 0.15), step(0.5, r3)) * (w.y > 0.45 ? 1.25 : 1);
        }
        else if (abs(w.x - span * 0.6) < 0.35 && abs(w.y - 2.2) < 0.45)   // a framed picture on a side wall
            c = abs(w.x - span * 0.6) > 0.3 || abs(w.y - 2.2) > 0.4 ? float3(0.40, 0.30, 0.12) : lerp(float3(0.30, 0.38, 0.25), float3(0.55, 0.40, 0.25), Noise3(float3(w * 6, bay)));
    }
    // daylight from the window fades into the room; a warm lamp hangs in the middle (lit in about two bays of three)
    float depth = saturate(h.z / D);
    float3 day = Ambient(float3(0, 1, 0)) * lerp(1.1, 0.3, depth);
    float3 lampAt = float3(W * 0.5, H - 0.9, D * 0.5);
    float3 lamp = (r2 > 0.3 ? 1 : 0.15) * float3(1.0, 0.62, 0.30) * (Mode == 1 ? 0.35 : 0.9) / (0.6 + dot(h - lampAt, h - lampAt) * 0.35);
    float3 col = c * (day + lamp);
    if (length(h - lampAt) < 0.18) col += float3(4, 2.6, 1.3) * (r2 > 0.3 ? 1 : 0.1);
    return col;
}

// What a stained pane passes: its own hue at full strength (clear glass passes nearly everything).
float3 StainTint(Material m) { return m.Base / max(max(m.Base.r, m.Base.g), max(m.Base.b, 1e-3)) * 0.9; }

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
