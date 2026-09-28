// Visual GPU test, Direct3D 12 rasterisation (GpuSceneExecutor): the test model (the Mazesta logo, or Models\gpu-test.obj) turning in the
// middle of a window, a ring of smaller copies orbiting it, over a procedural nebula - the FurMark idea of a heavy, always-changing frame,
// with Mazesta's own scene. Compiled offline by tools/compile-gpu-shaders.ps1.
//
// The model is a triangle list pulled from a structured buffer (position, normal); instance 0 is the centre model, the others are the ring.
// Every value depends only on Time and the instance, so a frame drawn twice at the same Time is the same image - the check frames rely on it.
// Load (1..4) multiplies the per-pixel work of the background and the surface detail.
// On top of the solid models: FurMark's own load - shell fur, Shells translucent layers of every model pushed out along its (smoothed)
// normals, each pixel of each layer lit strand by strand - and Particles glowing embers swirling round the scene, blended additively.
// Both are pure overdraw: they keep the GPU's shader cores, blending and memory busy even at 4K.

#define RS "RootConstants(num32BitConstants=8, b0), SRV(t0, visibility=SHADER_VISIBILITY_VERTEX)"

cbuffer Frame : register(b0) { float Time; float Aspect; uint Ring; uint Load; float Radius; uint Shells; uint Particles; float Pad; };
struct Vertex { float3 Position; float3 Normal; };
StructuredBuffer<Vertex> Model : register(t0);

static const float3 Eye = float3(0, 1.8, -8.5);
static const float3 Target = float3(0, 0.2, 0);

float4 Project(float3 world)
{
    float3 forward = normalize(Target - Eye), right = normalize(cross(float3(0, 1, 0), forward)), up = cross(forward, right);
    float3 d = world - Eye; float3 v = float3(dot(d, right), dot(d, up), dot(d, forward));
    const float f = 2.1445069, zn = 0.1, zf = 100;   // 50 degree vertical field of view
    return float4(v.x * f / Aspect, v.y * f, v.z * zf / (zf - zn) - zn * zf / (zf - zn), v.z);
}

float3x3 RotY(float a) { float s = sin(a), c = cos(a); return float3x3(c, 0, s, 0, 1, 0, -s, 0, c); }
float3x3 RotX(float a) { float s = sin(a), c = cos(a); return float3x3(1, 0, 0, 0, c, -s, 0, s, c); }

// The same placement the ray-traced mode builds on the CPU (GpuSceneExecutor.Placement), so both modes show the same scene.
void Place(uint instance, out float3x3 rotation, out float3 offset, out float scale)
{
    if (instance == 0) { rotation = mul(RotY(Time * 0.9), RotX(0.25 * sin(Time * 0.6))); offset = float3(0, 0.3, 0); scale = 1; return; }
    float i = instance, a = i / Ring * 6.2831853 + Time * 0.35;
    rotation = mul(RotY(-Time * 1.7 + i), RotX(Time * 1.1 + i));
    offset = float3(cos(a) * Radius, 0.9 * sin(Time * 1.3 + i * 0.7) + 0.3, sin(a) * Radius); scale = 0.22;
}

struct SceneOut { float4 Position : SV_Position; float3 Normal : NORMAL; float3 World : WORLD; float Tint : TINT; };

[RootSignature(RS)]
SceneOut SceneVS(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    Vertex v = Model[vertex];
    float3x3 r; float3 offset; float scale; Place(instance, r, offset, scale);
    SceneOut o; o.World = mul(r, v.Position) * scale + offset; o.Normal = mul(r, v.Normal); o.Position = Project(o.World);
    o.Tint = instance == 0 ? -1 : frac(instance * 0.618034);
    return o;
}

float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float Noise(float3 x)
{
    float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}
float Fbm(float3 p, uint octaves) { float v = 0, a = 0.5; for (uint o = 0; o < octaves; o++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; } return v; }

float3 Sky(float3 dir)
{
    float3 base = lerp(float3(0.02, 0.02, 0.05), float3(0.10, 0.12, 0.22), saturate(dir.y * 0.5 + 0.5));
    return base + pow(saturate(dot(dir, normalize(float3(-0.4, 0.6, -0.5)))), 64) * float3(1, 0.9, 0.6);
}

float4 ScenePS(SceneOut i) : SV_Target
{
    float3 n = normalize(i.Normal), view = normalize(Eye - i.World);
    // Surface detail: a brushed-metal ripple on the normal, its cost set by Load.
    n = normalize(n + 0.06 * (float3(Fbm(i.World * 6, 2 + Load * 2), Fbm(i.World * 6 + 3.1, 2 + Load * 2), Fbm(i.World * 6 + 7.7, 2 + Load * 2)) - 0.5));
    float3 albedo = i.Tint < 0 ? float3(0.99, 0.83, 0.0) : lerp(float3(0.25, 0.55, 1.0), float3(1.0, 0.35, 0.55), i.Tint);
    float3 color = albedo * 0.05;
    [unroll] for (int l = 0; l < 3; l++)
    {
        float3 lp = float3(sin(Time * 0.7 + l * 2.094) * 6, 3 + l, cos(Time * 0.7 + l * 2.094) * 6 - 2);
        float3 ld = lp - i.World; float falloff = 60 / (dot(ld, ld) + 4); ld = normalize(ld);
        float3 lc = l == 0 ? float3(1, 0.95, 0.85) : l == 1 ? float3(0.4, 0.6, 1) : float3(1, 0.5, 0.3);
        color += falloff * lc * (albedo * saturate(dot(n, ld)) + pow(saturate(dot(n, normalize(ld + view))), 80));
    }
    float fresnel = pow(1 - saturate(dot(n, view)), 5);
    color += Sky(reflect(-view, n)) * lerp(0.25, 1, fresnel);
    return float4(color / (1 + color), 1);
}

[RootSignature(RS)]
float4 BackgroundVS(uint vertex : SV_VertexID) : SV_Position
{
    float2 uv = float2((vertex << 1) & 2, vertex & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 1, 1);
}

float4 BackgroundPS(float4 position : SV_Position) : SV_Target
{
    float2 p = position.xy / 720.0;
    float3 q = float3(p * 1.7, Time * 0.08);
    float warp = Fbm(q + Fbm(q * 1.3 + Time * 0.05, 3 + Load), 3 + Load * 2);
    float3 c = lerp(float3(0.01, 0.01, 0.03), float3(0.09, 0.05, 0.16), warp);
    c += float3(0.35, 0.25, 0.0) * pow(saturate(Fbm(q * 3 - warp, 2 + Load) - 0.35), 3);
    return float4(c, 1);
}

// ——— fur: Shells layers of every model, drawn innermost first (instance = layer * copies + copy, and D3D keeps instance order) ———

static const float FurLength = 0.16, FurDensity = 38;
struct FurOut { float4 Position : SV_Position; float3 Normal : NORMAL; float3 World : WORLD; float3 Root : ROOT; float Height : HEIGHT; float Tint : TINT; };

[RootSignature(RS)]
FurOut FurVS(uint vertex : SV_VertexID, uint id : SV_InstanceID)
{
    uint copies = Ring + 1, instance = id % copies; float h = (id / copies + 1) / (float)Shells;
    Vertex v = Model[vertex];   // the fur pass binds the model with smoothed normals, so the layers stay closed over sharp edges
    float3x3 r; float3 offset; float scale; Place(instance, r, offset, scale);
    FurOut o;
    // Gravity and a slow wind bend the tips, more the further out the layer is.
    float3 bend = float3(0.5 * sin(Time * 1.3 + instance), -1, 0.4 * cos(Time * 0.9 + instance)) * (0.09 * scale * h * h);
    o.World = mul(r, v.Position + v.Normal * (FurLength * h)) * scale + offset + bend;
    o.Normal = mul(r, v.Normal); o.Position = Project(o.World);
    o.Root = v.Position * FurDensity; o.Height = h;   // the strand is found at its root, so it stands straight out through every layer
    o.Tint = instance == 0 ? -1 : frac(instance * 0.618034);
    return o;
}

float4 FurPS(FurOut i) : SV_Target
{
    float3 cell = floor(i.Root), f = frac(i.Root);
    float3 centre = 0.25 + 0.5 * float3(Hash(cell), Hash(cell + 11.3), Hash(cell + 27.1));
    float strand = 0.55 + 0.45 * Hash(cell + 5.7), h = i.Height / strand;
    if (h > 1 || length(f - centre) > 0.45 * (1 - h)) discard;

    float3 t = normalize(i.Normal), view = normalize(Eye - i.World);
    float3 albedo = i.Tint < 0 ? float3(0.99, 0.78, 0.05) : lerp(float3(0.25, 0.55, 1.0), float3(1.0, 0.35, 0.55), i.Tint);
    albedo *= 0.8 + 0.4 * Hash(cell + 3.3);
    float occlusion = 0.2 + 0.8 * i.Height;   // roots sit in the shadow of the strands around them
    float3 color = albedo * 0.06 * occlusion;
    [unroll] for (int l = 0; l < 3; l++)
    {
        float3 lp = float3(sin(Time * 0.7 + l * 2.094) * 6, 3 + l, cos(Time * 0.7 + l * 2.094) * 6 - 2);
        float3 ld = lp - i.World; float falloff = 60 / (dot(ld, ld) + 4); ld = normalize(ld);
        float3 lc = l == 0 ? float3(1, 0.95, 0.85) : l == 1 ? float3(0.4, 0.6, 1) : float3(1, 0.5, 0.3);
        // Kajiya-Kay: a hair is lit by the angle to its own direction, not by a surface normal.
        float tl = dot(t, ld), th = dot(t, normalize(ld + view));
        color += falloff * lc * occlusion * (albedo * sqrt(1 - tl * tl) * 0.7 + 0.35 * pow(sqrt(saturate(1 - th * th)), 90));
    }
    return float4(color / (1 + color), 1 - 0.5 * h);
}

// ——— particles: embers in a swirling disc and a rising column, each a camera-facing glow; nothing is simulated, so any Time is reproducible ———

uint Pcg(uint v) { uint s = v * 747796405u + 2891336453u; uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u; return (w >> 22u) ^ w; }
float Rand(uint id, uint k) { return Pcg(id * 8u + k) * (1.0 / 4294967295.0); }

struct ParticleOut { float4 Position : SV_Position; float2 Corner : CORNER; float3 Color : COLOR; };

[RootSignature(RS)]
ParticleOut ParticleVS(uint corner : SV_VertexID, uint id : SV_InstanceID)
{
    // Both swarms keep clear of the centre model (half its width is 2) and of the camera, so they frame the logo instead of hiding it.
    float r = 2.5 + 3.4 * pow(Rand(id, 0), 0.8), a = Rand(id, 1) * 6.2831853, glow;
    float3 p;
    if (id & 1)
    {   // the disc: orbits faster near the centre
        a += Time * 1.4 / sqrt(r); p = float3(cos(a) * r, 0.3 + (Rand(id, 2) - 0.5) * 0.12 * r + 0.15 * sin(Time + r), sin(a) * r); glow = 0.6;
    }
    else
    {   // the column: rises, spirals and fades in and out along its loop
        float phase = frac(Rand(id, 2) + Time * (0.04 + 0.08 * Rand(id, 3)));
        a += Time * 0.6 + phase * 4; p = float3(cos(a) * r, -1.2 + phase * 6.5, sin(a) * r); glow = 0.8 * sin(phase * 3.1415927);
    }
    float3 forward = normalize(Target - Eye), right = normalize(cross(float3(0, 1, 0), forward)), up = cross(forward, right);
    float2 c = float2(corner == 1 || corner >= 4 ? 1 : -1, corner == 2 || corner == 4 || corner == 5 ? -1 : 1);
    float size = 0.012 + 0.03 * Rand(id, 4);
    ParticleOut o; o.Position = Project(p + (right * c.x + up * c.y) * size); o.Corner = c;
    o.Color = lerp(float3(1.0, 0.62, 0.15), float3(0.3, 0.6, 1.0), Rand(id, 5) < 0.7 ? 0 : 1) * glow * (0.35 + 0.65 * Rand(id, 6));
    return o;
}

float4 ParticlePS(ParticleOut i) : SV_Target
{
    float d = dot(i.Corner, i.Corner);
    if (d > 1) discard;
    return float4(i.Color * (exp(-d * 5) * 0.9 + 0.1 * (1 - d)), 1);
}
