// The GPU temperature test's scene (ShamsehScene): an eight-point star (shamseh) in turquoise velvet with a gold medallion engraved
// "مازستا / MAZESTA", turning in front of a velvet ground that fills the screen. Both are fur drawn as layers, each blended over the one
// beneath it into a 16-bit target: a layer reads its strands' heights from a random texture at several texels a pixel, so the reads miss the
// cache and go to the card's memory. Measured on an RTX 3090 at its 350 W limit, that is what holds the core clock as low as a fur test does
// (about 1270 MHz, against 1660 MHz for the same layers read where neighbouring pixels share texels, and 1720 MHz for arithmetic alone).
// Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "RootConstants(num32BitConstants=12, b0), SRV(t3), DescriptorTable(SRV(t0, numDescriptors=3)), " \
           "StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_WRAP, addressV=TEXTURE_ADDRESS_WRAP), " \
           "StaticSampler(s1, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP)"

cbuffer K : register(b0) { float Ry; float Rx; float Aspect; uint Layers; uint FurLayers; float Spread; float FurLength; float Time; float2 Size; float2 Pad2; };
struct Vertex { float3 Pos; float3 Normal; float2 Uv; float Material; };
StructuredBuffer<Vertex> Vertices : register(t3);
Texture2D<float4> Strands : register(t0);   // random heights, 2048 square, no mips
Texture2D<float4> Words : register(t1);     // the engraving: white on black
Texture2D<float4> Light : register(t2);     // the frame before the lens
SamplerState Wrap : register(s0); SamplerState Clamp : register(s1);

static const float3 L = normalize(float3(0.6, 0.75, 0.55));
static const float3 Gold = float3(1.0, 0.74, 0.32);
float2 Turn(float2 v, float a) { float c = cos(a), s = sin(a); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }
float3 ToWorld(float3 p) { p.yz = Turn(p.yz, -Rx); p.xz = Turn(p.xz, -Ry); return p; }
// the camera at z 4.2 looking down -z; depth runs 0 at 1 unit away to 1 at 8
float4 Project(float3 p) { float d = 4.2 - p.z; return float4(p.x * 3.4 / Aspect, p.y * 3.4, (d - 1) / 7 * d, d); }
float3 Env(float3 d)
{
    float3 c = lerp(float3(0.02, 0.03, 0.05), float3(0.18, 0.2, 0.24), smoothstep(-0.4, 0.8, d.y));
    c += float3(1.4, 1.3, 1.15) * smoothstep(0.86, 0.95, dot(d, normalize(float3(-0.5, 0.6, 0.7))));
    c += float3(0.25, 0.6, 0.7) * smoothstep(0.75, 0.95, dot(d, normalize(float3(0.8, 0.1, -0.6))));
    c += float3(1.0, 0.8, 0.55) * 0.6 * smoothstep(0.9, 0.98, dot(d, L));
    return c;
}

struct Lit { float4 Pos : SV_Position; float3 World : W; float3 Normal : N; float3 Object : O; float2 Uv : UV; nointerpolation uint Material : M; float H : H; };

// ----- the star and its gold, solid
[RootSignature(RS)]
Lit SolidVS(uint id : SV_VertexID)
{
    Vertex v = Vertices[id]; Lit o; o.World = ToWorld(v.Pos); o.Normal = ToWorld(v.Normal); o.Object = v.Pos; o.Uv = v.Uv; o.Material = (uint)v.Material; o.H = 0;
    o.Pos = Project(o.World); return o;
}
[RootSignature(RS)]
float4 SolidPS(Lit i) : SV_Target
{
    float3 n = normalize(i.Normal), v = normalize(float3(0, 0, 4.2) - i.World);
    if (i.Material == 0) return float4(float3(0.02, 0.2, 0.22) * (0.2 + 0.8 * saturate(dot(n, L))), 1);   // the velvet's ground, under its fur
    float cut = 0;
    if (i.Material == 1)
    {   // the medallion's faces: the words cut into the gold, their edges catching the light
        float side = i.Object.z >= 0 ? 1 : -1; float2 uv = float2(i.Object.x * side, -i.Object.y) / 0.64 + 0.5; const float e = 1.0 / 512;
        float a = Words.SampleLevel(Clamp, uv, 0).r; float2 g = float2(Words.SampleLevel(Clamp, uv + float2(e, 0), 0).r - a, Words.SampleLevel(Clamp, uv + float2(0, e), 0).r - a);
        n = normalize(n - ToWorld(float3(g * 6 * float2(side, -1), 0))); cut = a;
    }
    // the highlight widens where the normal turns fast across a pixel (a thin tube seen edge on), so it does not flicker from frame to frame
    float3 dn = fwidth(n); float sharp = 90 / (1 + 90 * 6 * dot(dn, dn));
    float dif = saturate(dot(n, L)), fres = pow(1 - saturate(dot(n, v)), 5), spec = pow(saturate(dot(n, normalize(L + v))), sharp) * saturate(sharp / 30);
    float3 c = Gold * (0.08 + Env(reflect(-v, n)) * lerp(0.7, 1.0, fres)) + Gold * dif * 0.5 + spec * 2.5;
    return float4(lerp(c, Gold * 0.12, cut * 0.85), 1);
}

// ----- the star's fur: the star again for each layer, a step further out along its normals, the outer layers swaying in a breeze
[RootSignature(RS)]
Lit FurVS(uint id : SV_VertexID, uint layer : SV_InstanceID)
{
    Vertex v = Vertices[id]; Lit o; float h = (layer + 1.0) / FurLayers;
    o.World = ToWorld(v.Pos + v.Normal * h * FurLength);
    float3 sway = float3(sin(Time * 1.7 + o.World.y * 4) * 0.6, -1, cos(Time * 1.3 + o.World.x * 3) * 0.4);   // gravity, and the breeze
    o.World += sway * FurLength * 0.35 * h * h;
    o.Normal = ToWorld(v.Normal); o.Object = v.Pos; o.Uv = v.Uv; o.Material = 0; o.H = h;
    o.Pos = Project(o.World); return o;
}
// The strands are the texture's random heights, filtered, about a pixel and a half each, so neighbouring ones clump into tufts: a strand covers
// a layer where it is taller than the layer is high, and the many layers make it a dense pile (none over the medallion).
[RootSignature(RS)]
float4 FurPS(Lit i) : SV_Target
{
    float3 n = normalize(i.Normal), v = normalize(float3(0, 0, 4.2) - i.World);
    float4 s = Strands.SampleLevel(Wrap, i.Uv * 0.55, 0), s2 = Strands.SampleLevel(Wrap, i.Uv * 1.27 + 0.37, 0);
    float tall = s.r * 0.7 + s2.r * 0.3, a = saturate((tall - (0.22 + 0.6 * i.H)) * 9) * 0.85 * smoothstep(0.34, 0.42, length(i.Object.xy));
    float3 base = float3(0.06, 0.62, 0.66) * (0.85 + 0.3 * s.g);
    float dif = saturate(dot(n, L)) * 0.8 + 0.2, rim = pow(1 - abs(dot(n, v)), 2);
    float3 c = base * (0.2 + 0.8 * i.H) * dif + base * rim * 0.8 * i.H + float3(0.9, 0.95, 1) * 0.12 * pow(saturate(dot(reflect(-L, n), v)), 6) * i.H;
    return float4(c * a, a);
}

// ----- the velvet ground: layers over the whole screen, behind the star (the depth test keeps them off it)
struct Flat { float4 Pos : SV_Position; float H : H; };
[RootSignature(RS)]
Flat GroundVS(uint id : SV_VertexID, uint layer : SV_InstanceID)
{
    Flat o; float2 p = float2((id << 1) & 2, id & 2); o.Pos = float4(p * float2(2, -2) + float2(-1, 1), 0.9999, 1); o.H = (layer + 1.0) / Layers; return o;
}
// A girih of eight-point stars over the ground, as faint gold seams: the distance to the nearest seam of a square-and-star tiling.
float Seam(float2 p)
{
    float2 q = abs(frac(p) - 0.5); float a = abs(max(q.x, q.y) - 0.25), b = abs((q.x + q.y) * 0.7071 - 0.3);
    return min(a, b);
}
// The ground moves: the girih turns slowly, light runs out along its seams in rings, and waves of wind comb the pile so it catches the light in moving bands.
[RootSignature(RS)]
float4 GroundPS(Flat i) : SV_Target
{
    // each layer reads its own part of the texture, several texels a pixel: what keeps the card's memory busy (see the top of the file)
    float2 uv = i.Pos.xy * (Spread / 2048) + i.H * float2(0.37, 0.13) + Time * float2(0.011, 0.007);
    float4 s = Strands.SampleLevel(Wrap, uv, 0);
    float2 screen = (i.Pos.xy - Size * 0.5) / Size.y;
    float wind = sin(dot(screen, float2(0.8, 0.6)) * 6 - Time * 1.4) * 0.6 + sin(dot(screen, float2(-0.5, 0.85)) * 4.3 + Time * 0.9) * 0.4;
    float cover = saturate((s.r - i.H * (0.85 - 0.12 * wind)) * 8) * 0.6;
    float glow = 1 - 0.6 * dot(screen, screen), r = length(screen);
    float run = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(r * 10 - Time * 2.2), 6);
    float seam = smoothstep(0.014, 0.0, Seam(Turn(screen, Time * 0.05) * 3.2)) * (0.4 + 0.6 * i.H) * run;
    float3 c = lerp(float3(0.015, 0.06, 0.08), float3(0.05, 0.2, 0.23), i.H) * glow * (0.8 + 0.4 * s.g) * (0.75 + 0.5 * wind) + Gold * seam * 0.55 * glow;
    return float4(c * cover, cover);
}

// ----- the lens: the light to the screen's colours
[RootSignature(RS)]
float4 LensVS(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * float2(2, -2) + float2(-1, 1), 0, 1); }
[RootSignature(RS)]
float4 LensPS(float4 pos : SV_Position) : SV_Target
{
    float3 c = Light.Load(int3(pos.xy, 0)).rgb;
    c = c * (2.51 * c + 0.03) / (c * (2.43 * c + 0.59) + 0.14);
    return float4(pow(saturate(c), 1 / 2.2), 1);
}
