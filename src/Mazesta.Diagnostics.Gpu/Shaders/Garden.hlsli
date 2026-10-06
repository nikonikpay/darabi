// Shared by the garden's two renderers (GardenRaster.hlsl, GardenRay.hlsl): the frame constants, the scene's records as GardenScene.cs
// lays them out, the materials (base colour, roughness, metalness, a normal map and its occlusion: the metal-roughness model the .blend's
// own Principled shader is), the light they give back (GGX), the sky, the fountain's water and the light falloff.
// Everything is a pure function of its inputs and the frame's Time: a frame drawn twice at one Time is the same image.

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
    float4 Logo;                          // the logo's turn toward the camera (cos, sin), its float (lift): GardenGpu.LogoMotion; and how much it glows of itself
    uint Samples; uint3 FramePad;         // ray tracer: camera rays a pixel
    float4 Backdrop;                      // the mountains round the horizon: the heights they cover as tangents (low, high), and 1 when they are there
    float4x4 SkyViewProj;                 // rasteriser: the view straight down over the courtyard (what stands between a point and the open sky)
    float4 Fountain;                      // the fountain's nozzle, and the radius of the water in its bowl
    float4 Fountain2;                     // that water's level, the radius of the bowl's rim, how many droplets are the jet's (the rest spill from the rim), 1 when there is a fountain
    float4 Ambience;                      // rasteriser: 1 when the occlusion image is bound, its taps, the near plane's distance
};

// Flags: 1 the logo, 2 the mirror sphere (moved by the CPU alone), 4 a droplet of the fountain, whose number is Index
struct Instance { float4 Row0; float4 Row1; float4 Row2; uint Mesh; uint Mask; uint Flags; uint Index; };
// Pattern: for a brick, its scale, mortar size, brick width and row height; for any other textured surface, how many times the texture
// repeats across the mesh's coordinates (x, y) and, when z is not 0, that it is laid by world position instead, z repeats a metre.
// LightTint 1: light passing through takes the pane's colour (stained glass).
struct Material { uint Kind; int Texture; float LightTint; int NormalTexture; float3 Base; float Alpha; float3 Color2; float Roughness; float3 Mortar; float Metallic; float3 Emission; float Transmission; float4 Pattern; };
struct Light { float3 Position; uint Kind; float3 Direction; float Range; float3 Color; float CosOuter; float CosInner; float Radius; float2 Pad; };

static const uint KFlat = 0, KCutout = 1, KBrick = 2, KWater = 3, KGlass = 4, KEmissive = 5;
static const uint LSun = 0, LPoint = 1, LSpot = 2;
static const uint FLogo = 1, FSphere = 2, FDroplet = 4;
static const float Pi = 3.14159265;

uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
float Hash01(uint x) { return (Hash(x) >> 8) * (1.0 / 16777216.0); }

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

float Hash1(float x) { return frac(sin(x * 127.1 + 311.7) * 43758.5453); }
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

// ——— the fountain ———
// Water leaves the nozzle as droplets, each on its own ballistic flight (a pure function of its number and the time, so the CPU can
// place the same droplets for the ray tracer: GardenGpu.Droplet is this function's twin). A flight ends where it meets something:
// the water in the bowl, from which the droplet splashes up again, slower and scattered, or - once it has cleared the rim - the pool.
// The rest of the droplets are the bowl running over: they leave the rim's twelve lobes and fall to the pool.
static const float Gravity = 9.81;
void Droplet(uint id, float time, out float3 pos, out float3 vel, out float size)
{
    float3 nozzle = Fountain.xyz; float level = Fountain2.x, rim = Fountain2.y; uint jets = (uint)Fountain2.z;
    float h1 = Hash01(id * 16 + 1), h2 = Hash01(id * 16 + 2), h3 = Hash01(id * 16 + 3), h4 = Hash01(id * 16 + 4);
    float t; float3 p, v;
    if (id < jets)
    {
        t = frac(time / 2.3 + h1) * 2.3;
        float a = h3 * 2 * Pi, u = 0.10 + 0.50 * h2 * h2;   // most of them near the axis
        p = nozzle; v = float3(cos(a) * u, 4.0 + 0.7 * h4, sin(a) * u);
    }
    else
    {
        t = frac(time / 0.8 + h1) * 0.8;
        float a = ((id - jets) % 12 + 0.5 + (h3 - 0.5) * 0.4) / 12 * 2 * Pi;
        p = float3(nozzle.x + cos(a) * rim, level + 0.03, nozzle.z + sin(a) * rim); v = float3(cos(a), 0, sin(a)) * (0.30 + 0.25 * h2); v.y = -0.2;
    }
    size = 0.016 + 0.012 * h4;
    for (uint k = 0; k < 3; k++)
    {
        float toBowl = (v.y + sqrt(max(v.y * v.y + 2 * Gravity * (p.y - level), 0))) / Gravity;
        float2 q = p.xz + v.xz * toBowl - nozzle.xz; bool bowl = dot(q, q) < rim * rim;
        float end = bowl ? toBowl : (v.y + sqrt(max(v.y * v.y + 2 * Gravity * (p.y - WaterLevel), 0))) / Gravity;
        if (t <= end) { pos = p + v * t - float3(0, 0.5 * Gravity * t * t, 0); vel = v - float3(0, Gravity * t, 0); return; }
        if (!bowl || k == 2) break;
        p = float3(p.x + v.x * end, level + 0.002, p.z + v.z * end); t -= end; size *= 0.75;
        float sa = Hash01(id * 16 + 5 + k) * 2 * Pi, su = 0.5 + 0.9 * Hash01(id * 16 + 8 + k);
        v = float3(v.x * 0.3 + cos(sa) * su, 1.1 + 0.9 * Hash01(id * 16 + 11 + k), v.z * 0.3 + sin(sa) * su);
    }
    pos = float3(0, -100, 0); vel = 0; size = 0;   // spent: in the water until its next turn
}
// A droplet's mesh is a small ball: drawn out along its flight, as a falling drop is seen.
float3 DropletPlace(float3 unit, float3 pos, float3 vel, float size)
{
    float speed = length(vel); float3 d = vel / max(speed, 1e-4);
    return pos + size * (unit + d * dot(unit, d) * min(speed * 0.22, 1.6));
}

// Occlusion: how much of the surrounding light the material's own crevices let in (its map; 1 without one).
// Sheen: how much of its mirror-like reflection is kept (1; a crown of leaf cards, which face every way, has next to none).
struct Surface { float3 Albedo; float Alpha; float Roughness; float Metallic; float3 Emission; float3 Normal; float Occlusion; float Sheen; };

// Where a material's texture is looked up: the mesh's own coordinates, repeated as the material says, or - for stone that has none of
// its own - the world position on the face's plane. world is true in the second case (du and dv then run along the world's axes).
float2 TexCoords(Material m, float2 uv, float3 p, float3 n, out bool world)
{
    world = m.Pattern.z > 0 && m.Kind <= KCutout;
    if (world) return FaceCoords(p, n) * m.Pattern.z;
    return m.Kind <= KCutout && m.Pattern.x > 0 ? uv * m.Pattern.xy : uv;
}

Surface MaterialSurface(Material m, float3 p, float3 n, float4 texel)
{
    Surface s; s.Albedo = m.Base; s.Alpha = m.Alpha; s.Roughness = m.Roughness; s.Metallic = m.Metallic; s.Emission = m.Emission; s.Normal = n; s.Occlusion = 1; s.Sheen = m.Kind == KCutout ? 0.12 : 1;
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
    else if (m.Kind == KFlat && m.Texture < 0 && m.Metallic < 0.5)
    {
        // earth, turf and bare stone are never one colour: broad patches, a finer mottle, and grain at the scale of a pebble
        float v = 0.55 * Noise3(p * 2.3) + 0.30 * Noise3(p * 9.1) + 0.15 * Noise3(p * 37);
        s.Albedo *= lerp(1, 0.5 + v, m.Roughness > 0.5 ? 0.5 : 0.2);
    }
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
// The rest of a normal map's texel: the material's ambient occlusion in red, its metalness in blue.
void Relief(inout Surface s, float3 t, float3 b, float4 texel) { s.Normal = Bumped(s.Normal, t, b, texel); s.Occlusion = texel.r; s.Metallic = texel.b; }

// ——— lighting ———

float3 SkyColor(float3 dir)
{
    float up = saturate(dir.y), h = pow(1 - up, 3);
    float3 c = lerp(SkyZenith, SkyHorizon, h);
    float toSun = saturate(dot(dir, SunDir));
    if (Mode == 1) c += SunColor * (0.05 * pow(toSun, 6) + 0.16 * pow(toSun, 64)) + SunColor * 2 * smoothstep(0.99988, 0.99996, toSun);
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
    float3 lit = Mode == 1 ? dot(SkyHorizon, 0.333) * float3(1.55, 1.42, 1.3) + SunColor * (0.026 + 0.08 * pow(toSun, 8))
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
    m = Mode == 1 ? m * 0.76 : lerp(dot(m, float3(0.3, 0.59, 0.11)), m, 0.3) * float3(0.07, 0.09, 0.18);
    return lerp(c, m, w);
}

// The sky's light on a surface facing n: the sky's own colour from above (brighter toward the low sun), and from below what the lit
// paving and earth give back. By day it is about half the bright sky's colour, so the sun and its shadows give the courtyard its shape.
float3 Ambient(float3 n)
{
    float3 sky = lerp(SkyHorizon, SkyZenith, 0.5 + 0.3 * saturate(n.y));
    float3 ground = GroundColor + SunColor * saturate(SunDir.y) * (Mode == 1 ? 0.10 : 0.03) * float3(0.85, 0.74, 0.58);
    float3 c = lerp(ground, sky, saturate(n.y * 0.5 + 0.5));
    if (Mode != 1) return c;
    c += SkyHorizon * 0.3 * saturate(dot(normalize(n.xz + 1e-5), normalize(SunDir.xz + 1e-5))) * (1 - abs(n.y));
    return c * 0.34;
}

// The air between the eye and a far surface takes on the low sky's colour: the trees beyond the walls and the hills stand back.
float3 Haze(float3 c, float3 p)
{
    float f = 1 - exp(-length(p - Eye) * (Mode == 1 ? 0.0035 : 0.0015));
    return lerp(c, lerp(SkyHorizon, SkyZenith, 0.25) * (Mode == 1 ? 0.8 : 0.6), f);
}

// What a stained pane passes: its own hue at full strength (clear glass passes nearly everything).
float3 StainTint(Material m) { return m.Base / max(max(m.Base.r, m.Base.g), max(m.Base.b, 1e-3)) * 0.9; }

// Radiance leaving a surface lit by one light of irradiance E from direction l - the metal-roughness model: a GGX highlight with
// Smith's height-correlated shadowing and Schlick's Fresnel, over Lambert's diffuse for what the surface does not mirror (a metal: nothing).
float3 Brdf(Surface s, float3 v, float3 l, float3 e)
{
    float nl = saturate(dot(s.Normal, l)); if (nl <= 0) return 0;
    float3 h = normalize(l + v); float nh = saturate(dot(s.Normal, h)), nv = max(abs(dot(s.Normal, v)), 1e-3);
    float a = max(0.045, s.Roughness * s.Roughness), a2 = a * a;
    float d = a2 / (Pi * pow(nh * nh * (a2 - 1) + 1, 2));
    float vis = 0.5 / (nl * sqrt(nv * nv * (1 - a2) + a2) + nv * sqrt(nl * nl * (1 - a2) + a2));
    float3 f0 = lerp(0.04, s.Albedo, s.Metallic);
    float3 f = f0 + (1 - f0) * pow(1 - saturate(dot(h, v)), 5);
    return e * nl * ((1 - f * s.Sheen) * (1 - s.Metallic) * s.Albedo / Pi + f * min(d * vis, 60) * s.Sheen);
}

// How much of its surroundings a surface mirrors, over all the directions its roughness spreads the mirror image across
// (Karis' fit to the split-sum integral): f0 scaled and offset by the angle it is seen at.
float3 EnvBrdf(Surface s, float3 v)
{
    float nv = saturate(dot(s.Normal, v)); float3 f0 = lerp(0.04, s.Albedo, s.Metallic);
    float4 r = s.Roughness * float4(-1, -0.0275, -0.572, 0.022) + float4(1, 0.0425, 1.04, -0.04);
    float2 ab = float2(-1.04, 1.04) * (min(r.x * r.x, exp2(-9.28 * nv)) * r.x + r.y) + r.zw;
    return (f0 * ab.x + ab.y) * s.Sheen;
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
    if (Fountain2.w > 0)   // rings running out from where the fountain's water falls: round the bowl's rim, and in the bowl from the jet
    {
        float2 d = q - Fountain.xz; float r = max(length(d), 1e-3), from = p.y > WaterLevel + 0.3 ? 0 : Fountain2.y;
        g += d / r * 0.045 * cos((r - from) * 11 - t * 7) * exp(-abs(r - from) * 0.6);
    }
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
