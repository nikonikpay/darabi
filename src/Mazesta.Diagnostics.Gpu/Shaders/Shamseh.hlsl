// The GPU temperature test's scene (ShamsehScene): the Gate of All Nations (a scan, all two million of its triangles) standing on a lawn, under an aurora that fills the sky. The lawn is the work: grass drawn as layers, each blended over the one
// beneath it into a float target (the sky's aurora is layers too). A layer reads its strands' heights from a random texture at about a texel a
// pixel, so most reads are served from the cache and the cores do the work, as a fur test's do: what draws a card to its power limit with the
// clock held under its boost. (Before this the picture was a velvet star with the same layers; git tag pre-gate-thermal-scene.)
// Compiled offline by tools/compile-gpu-shaders.ps1 (the entry names are the star's: SolidVS/PS the gate and the lawn's soil,
// FurVS/PS the grass, GroundVS/PS the sky).

#define RS "RootConstants(num32BitConstants=12, b0), SRV(t3), DescriptorTable(SRV(t0, numDescriptors=3), SRV(t4)), " \
           "StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_WRAP, addressV=TEXTURE_ADDRESS_WRAP), " \
           "StaticSampler(s1, filter=FILTER_ANISOTROPIC, maxAnisotropy=8, addressU=TEXTURE_ADDRESS_WRAP, addressV=TEXTURE_ADDRESS_WRAP)"

cbuffer K : register(b0) { float Ry; float Rx; float Aspect; uint Layers; uint FurLayers; float Spread; float FurLength; float Time; float2 Size; float2 Pad2; };
struct Vertex { float3 Pos; float3 Normal; float2 Uv; float Material; };
StructuredBuffer<Vertex> Vertices : register(t3);
Texture2D<float4> Strands : register(t0);   // random heights, 2048 square, with mips (the grass reads by its footprint; the sky reads level 0)
Texture2D<float4> Albedo : register(t1);    // the gate's colour (BC3, mips)
Texture2D<float4> Sky : register(t4);       // the daylight panorama behind the gate (sRGB, 2000 x 848: all the way round, 153 degrees up and down)
Texture2DMS<float4> Light : register(t2);   // the frame before the lens, several samples a pixel
SamplerState Wrap : register(s0); SamplerState Aniso : register(s1);

static const float3 L = normalize(float3(0.72, 0.5, 0.5));   // the panorama's sun, turned to shine on the gate from the front right, a little higher than in the picture
static const float3 Moon = float3(1.0, 0.9, 0.75) * 1.5;   // (the name is the night scene's) the sun's light
static const float3 Eye = float3(0, 1.5, 3.4);
static const float Pitch = 0.24, TanHalf = 0.40, Turf = 1.2;   // the camera looking down at the gate and its whole turf (the stone's top and the turf's near edge both inside the frame); 43 degrees across the frame; the turf's radius
float2 Turn(float2 v, float a) { float c = cos(a), s = sin(a); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }
float3 ToWorld(float3 p) { p.yz = Turn(p.yz, -Rx); p.xz = Turn(p.xz, -Ry); return p; }
// the camera at Eye, looking down by Pitch; depth d runs from 0.3 to 60
float4 Project(float3 p)
{
    float3 q = p - Eye; float c = cos(Pitch), s = sin(Pitch);
    float d = -q.y * s - q.z * c, y = q.y * c - q.z * s;
    return float4(q.x / (TanHalf * Aspect), y / TanHalf, 60 * (d - 0.3) / 59.7, d);
}
static const float3 Horizon = float3(0.5, 0.42, 0.33);   // the haze
float3 Fog(float3 c, float3 world) { return lerp(c, Horizon, 1 - exp(-length(world - Eye) * 0.012)); }

struct Lit { float4 Pos : SV_Position; float3 World : W; float3 Normal : N; float3 Object : O; float2 Uv : UV; nointerpolation uint Material : M; float H : H; };

// ----- the gate and the soil under the grass, solid
[RootSignature(RS)]
Lit SolidVS(uint id : SV_VertexID)
{
    Vertex v = Vertices[id]; Lit o; o.World = ToWorld(v.Pos); o.Normal = ToWorld(v.Normal); o.Object = v.Pos; o.Uv = v.Uv; o.Material = (uint)v.Material; o.H = 0;
    o.Pos = Project(o.World); return o;
}
[RootSignature(RS)]
float4 SolidPS(Lit i) : SV_Target
{
    float3 n = normalize(i.Normal), v = normalize(Eye - i.World);
    float2 du1 = ddx(i.Uv), du2 = ddy(i.Uv);   // taken before the branch: the derivatives are of the whole quad
    if (i.Material != 0) return float4(Fog((i.Object.y < -0.001 || abs(n.y) < 0.5 ? float3(0.07, 0.045, 0.03) : float3(0.012, 0.04, 0.012)) * (0.3 + 0.7 * saturate(dot(n, L))) * Moon, i.World), 1);   // the soil under the grass, and the earth round the turf's edge
    float3 nn = n;   // the scan's own geometry carries the detail: no map
    float3 a = Albedo.SampleGrad(Aniso, i.Uv, du1, du2).rgb;
    float dif = saturate(dot(nn, L)), fres = pow(1 - saturate(dot(n, v)), 4), spec = pow(saturate(dot(nn, normalize(L + v))), 28) * 0.12;
    float3 amb = lerp(float3(0.06, 0.07, 0.04), float3(0.22, 0.27, 0.34), nn.y * 0.5 + 0.5);   // the grass's bounce below, the aurora's glow above
    float ao = lerp(0.4, 1, saturate(i.Object.y / 0.3));   // the foot of the stone, where the grass shuts the light out
    float3 c = a * (amb + Moon * dif) * ao + spec * Moon + a * float3(0.1, 0.5, 0.3) * fres * 0.15;
    return float4(Fog(c, i.World), 1);
}

// ----- the grass: the ground again for each layer, a step further up, the outer layers swaying in a breeze
[RootSignature(RS)]
Lit FurVS(uint id : SV_VertexID, uint layer : SV_InstanceID)
{
    Vertex v = Vertices[id]; Lit o; float h = (layer + 1.0) / FurLayers;
    o.World = ToWorld(v.Pos + v.Normal * h * FurLength);
    float3 sway = float3(sin(Time * 1.7 + v.Pos.x * 3 + v.Pos.z * 2) * 0.6, -0.6, cos(Time * 1.3 + v.Pos.z * 2.5) * 0.4);
    o.World += sway * FurLength * 0.5 * h * h;
    o.Normal = ToWorld(v.Normal); o.Object = v.Pos; o.Uv = v.Uv; o.Material = 1; o.H = h;
    o.Pos = Project(o.World); return o;
}
// The blades are the texture's random heights, filtered, about a pixel and a half each, so neighbouring ones clump into tufts: a blade covers
// a layer where it is taller than the layer is high, and the many layers make it a dense lawn.
[RootSignature(RS)]
float4 FurPS(Lit i) : SV_Target
{
    float3 n = normalize(i.Normal), v = normalize(Eye - i.World);
    // four reads near the blade's own scale (so they stay in the cache) and a blade's own highlight: more work for the cores per blended pixel,
    // which holds the clock lower at the power limit, as a fur test's does, without sending more reads to the memory
    float4 s = Strands.Sample(Wrap, i.Uv * 0.55), s2 = Strands.Sample(Wrap, i.Uv * 1.27 + 0.37);
    float4 s3 = Strands.Sample(Wrap, i.Uv * 0.83 + 0.71), s4 = Strands.Sample(Wrap, i.Uv * 0.21 + 0.19);
    float dist = length(i.World - Eye), reach = smoothstep(Turf, Turf - 0.04, length(i.Object.xz));   // the turf's round edge
    float outside = max(abs(i.Object.x) - 0.75, abs(i.Object.z) - 0.8);       // how far from the gate's foot (its footprint, about 1.4 by 1.5)
    float tall = s.r * 0.6 + s2.r * 0.25 + s3.r * 0.15, a = saturate((tall - (0.22 + 0.6 * i.H)) * 9) * 0.85 * reach;
    float3 base = lerp(float3(0.05, 0.3, 0.05), float3(0.3, 0.55, 0.08), s.g) * (0.85 + 0.3 * s4.b);   // the lawn's colour in soft patches
    float shut = lerp(0.45, 1, saturate(outside / 0.7));   // the gate keeps the light off the grass at its foot
    float dif = saturate(dot(n, L)) * 0.8 + 0.2, rim = pow(1 - abs(dot(n, v)), 2);
    // the blade's sheen (Kajiya-Kay): it lies along the blade, which leans out along the normal, bent a little by its own tuft
    float3 t = normalize(n + (s3.gba - 0.5) * 0.6), hv = normalize(L + v); float th = dot(t, hv), sheen = pow(sqrt(saturate(1 - th * th)), 48);
    float3 c = (base * (0.2 + 0.8 * i.H) * dif * Moon * 0.7 + base * rim * 0.5 * i.H + float3(0.7, 0.9, 1) * (0.05 * pow(saturate(dot(reflect(-L, n), v)), 6) + 0.1 * sheen) * i.H) * shut;
    c = lerp(c, Horizon, 1 - exp(-dist * 0.012));
    return float4(c * a, a);
}

// ----- the sky behind: an aurora turning in a cone, as layers over the whole screen behind the gate (the depth test keeps them off it).
// Every pixel of every layer does the same work wherever the gate is, so the frame's cost hardly changes as it turns.
struct Flat { float4 Pos : SV_Position; float H : H; };
[RootSignature(RS)]
Flat GroundVS(uint id : SV_VertexID, uint layer : SV_InstanceID)
{
    Flat o; float2 p = float2((id << 1) & 2, id & 2); o.Pos = float4(p * float2(2, -2) + float2(-1, 1), 0.9999, 1); o.H = (layer + 1.0) / Layers; return o;
}
// Smooth noise: the strands' random texture read where one texel spans many pixels, so the filter blends it (wrap, so the angle closes on itself).
float Soft(float2 uv) { return Strands.SampleLevel(Wrap, uv, 0).r; }
[RootSignature(RS)]
float4 GroundPS(Flat i) : SV_Target
{
    // Looking up into a cone: each layer is one ring of the cone's wall at its own distance, its curtain of light rippling round it as the cone turns.
    float2 p = (i.Pos.xy - Size * 0.5) / Size.y + float2(0.12 * sin(Time * 0.11), 0.08 * cos(Time * 0.13) - 0.3);   // the cone's axis above the gate
    float r = length(p), a = atan2(p.y, p.x) / 6.2831853 + 0.5, spin = Time * (0.025 + 0.02 * i.H);
    float2 q = float2(a * 4 + spin, i.H * 0.6 + Time * 0.01);
    float n = Soft(q * float2(48, 24) / 2048) * 0.55 + Soft(q * float2(140, 60) / 2048 + 0.31) * 0.3 + Soft(q * float2(420, 90) / 2048 + 0.67) * 0.15;
    float ring = lerp(0.18, 1.25, i.H) * (0.85 + 0.35 * n);
    float band = exp(-pow((r - ring) * (16 - 7 * i.H), 2));
    float rays = 0.45 + 0.55 * Soft(float2(a * 900 + spin * 200, Time * 0.4) / 2048);   // the curtain's folds, streaming along the wall
    float4 grain = Strands.SampleLevel(Wrap, i.Pos.xy * (Spread / 2048) + i.H * float2(0.37, 0.13), 0);   // a texel a pixel, a layer at a time: as before, the memory's share
    float3 hue = lerp(float3(0.1, 1.0, 0.55), float3(0.55, 0.25, 1.0), smoothstep(0.35, 0.9, i.H + 0.3 * (n - 0.5)));
    float arc = smoothstep(0.5, 0.75, Soft(q * float2(20, 10) / 2048 + 0.13));   // the curtains come and go round the ring: separate arcs, sky between
    float cover = saturate(band * band * arc * rays * rays * (0.7 + 0.3 * grain.r) * 0.45);
    float3 c = hue * (0.6 + 1.6 * band * rays) + float3(0.6, 0.9, 1) * pow(grain.g, 40) * 0.6 * (1 - band);   // a few stars between the curtains
    return float4(c * cover, max(cover, 0.002));
}

// ----- the sky: the panorama as the camera sees it, drawn at the far plane so only where nothing else is; its sun is behind the camera, where L comes from
static const float SkyTurn = -0.2417, SkySpan = 2.664, SkyGain = 0.85;   // the picture's turn about the vertical; its height in radians (848 / 2000 of a turn); its brightness before the lens
[RootSignature(RS)]
float4 SkyVS(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * float2(2, -2) + float2(-1, 1), 1, 1); }
[RootSignature(RS)]
float4 SkyPS(float4 pos : SV_Position) : SV_Target
{
    float2 n = pos.xy / Size * 2 - 1; n.y = -n.y; float c = cos(Pitch), s = sin(Pitch), x = n.x * TanHalf * Aspect, y = n.y * TanHalf;
    float3 d = normalize(float3(x, y * c - s, -y * s - c));
    float2 uv = float2(atan2(d.x, -d.z) / 6.2831853 + 0.5 + SkyTurn, saturate(0.5 - asin(d.y) / SkySpan));
    return float4(Sky.SampleLevel(Wrap, uv, 0).rgb * SkyGain, 1);
}

// ----- the lens: the light to the screen's colours
[RootSignature(RS)]
float4 LensVS(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * float2(2, -2) + float2(-1, 1), 0, 1); }
[RootSignature(RS)]
float4 LensPS(float4 pos : SV_Position) : SV_Target
{
    float3 sum = 0;   // each sample to the screen's colours first, then the mean: a bright edge keeps its shape instead of blooming
    uint w, h, n; Light.GetDimensions(w, h, n);
    for (uint k = 0; k < n; k++) { float3 c = Light.Load(int2(pos.xy), k).rgb; c = c * (2.51 * c + 0.03) / (c * (2.43 * c + 0.59) + 0.14); sum += pow(saturate(c), 1 / 2.2); }
    return float4(sum / n, 1);
}
