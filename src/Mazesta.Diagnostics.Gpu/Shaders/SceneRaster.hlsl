// Visual GPU test, Direct3D 12 rasterisation (GpuSceneExecutor): the test model (the Mazesta logo, or Models\gpu-test.obj) turning in the
// middle of a window, a ring of smaller copies orbiting it, over a procedural nebula - the FurMark idea of a heavy, always-changing frame,
// with Mazesta's own scene. Compiled offline by tools/compile-gpu-shaders.ps1.
//
// The model is a triangle list pulled from a structured buffer (position, normal); instance 0 is the centre model, the others are the ring.
// Every value depends only on Time and the instance, so a frame drawn twice at the same Time is the same image - the check frames rely on it.
// Load (1..4) multiplies the per-pixel work of the background and the surface detail.

#define RS "RootConstants(num32BitConstants=8, b0), SRV(t0, visibility=SHADER_VISIBILITY_VERTEX)"

cbuffer Frame : register(b0) { float Time; float Aspect; uint Ring; uint Load; float Radius; float3 Pad; };
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
