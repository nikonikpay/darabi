// Visual GPU test, Direct3D 12 rasterisation (GpuSceneExecutor + GardenRaster.cs): the Persian garden through a day and a night.
// Passes: the key light's shadow maps (the sun's or the moon's: one of the whole courtyard and a finer one of the hall alone, with
// the colour its stained panes give the light that passes them) and, once, the view straight down that says where the open sky is
// and what each lamp sees round it (all depth only); the garden all round from the middle of the courtyard and of the hall, for
// what surfaces mirror (a face of those pictures a frame, as the light changes); the pool's mirror
// image (the scene drawn again from the eye reflected in the water, when the load level asks for it); the frame's depth alone, from
// which the ambient occlusion image is worked out; then the frame itself - sky, solid geometry, leaves (alpha tested, or alpha to
// coverage under MSAA) and last the see-through water and glass - in light's own units (Packed into ten bits a colour); and the lens:
// the glow of what is bright (halved five times and summed back up), the blur of what is out of focus, the tone curve.
// Depth is reversed (1 at the near plane, 0 infinitely far), which keeps surfaces a centimetre apart from flickering across the whole
// garden.
// With ray tracing switched on (the shaders compiled with RT defined, on a GPU with DXR 1.1) the same frame asks the GPU's ray-tracing
// hardware what its maps only approximate: shadow rays to the sun or the moon and to every strong lamp in reach, and a ray along what the
// water, glass, metal and polished stone mirror, lit where it lands, and the light standing in the air. They all go in compute shaders before the
// colour pass (RaysCS, MirrorCS, LayersCS, FaceCS, AirCS); the colour pass's shaders, compiled with RT but not RAYS, hold no ray code and read what those found.
// Measured on an RX 9060 XT: the same rays draw the card's whole power limit from a compute shader (181 W, in a scene made to test it) and 147 W from a
// pixel shader, at a lower frame rate; ray code in the colour pass's shaders held the pass at 133 W even where no ray was sent (171 W without it).
// Glass and water are not in the frame's depth: LayerPS lists the surfaces of them at each pixel, up to four, for LayersCS. The pool's second drawing and the shadow maps are then not made.
// Compiled offline by tools/compile-gpu-shaders.ps1.
// The tables of views are DATA_VOLATILE: the frame draws into pictures that the same tables list (the pool's mirror image, the faces of the surroundings, the occlusion and the depth, the hall's tint). With the
// default (DATA_STATIC_WHILE_SET_AT_EXECUTE) the data behind a bound view may not change before the draw, the card is free to have read it early, and the frames come out different when two are in flight.

#define RS "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), RootConstants(num32BitConstants=12, b0), CBV(b1), SRV(t0), SRV(t1), SRV(t2), SRV(t17), SRV(t18), SRV(t19), SRV(t20), SRV(t21), SRV(t22), SRV(t23), " \
           "DescriptorTable(SRV(t3, numDescriptors=7, flags=DESCRIPTORS_VOLATILE | DATA_VOLATILE)), DescriptorTable(SRV(t10, numDescriptors=2, flags=DESCRIPTORS_VOLATILE | DATA_VOLATILE)), DescriptorTable(SRV(t12, numDescriptors=5, flags=DESCRIPTORS_VOLATILE | DATA_VOLATILE)), " \
           "DescriptorTable(SRV(t24, numDescriptors=11, flags=DESCRIPTORS_VOLATILE | DATA_VOLATILE)), DescriptorTable(UAV(u0, numDescriptors=11, flags=DESCRIPTORS_VOLATILE | DATA_VOLATILE)), " \
           "StaticSampler(s0, filter=FILTER_ANISOTROPIC, maxAnisotropy=16), " \
           "StaticSampler(s1, filter=FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT, addressU=TEXTURE_ADDRESS_BORDER, addressV=TEXTURE_ADDRESS_BORDER, borderColor=STATIC_BORDER_COLOR_OPAQUE_WHITE, comparisonFunc=COMPARISON_LESS_EQUAL), " \
           "StaticSampler(s2, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP, addressW=TEXTURE_ADDRESS_CLAMP)"

#include "Garden.hlsli"

cbuffer Draw : register(b0) { uint InstanceBase; uint MaterialIndex; uint2 DrawPad; float3 Centre; float DrawPad2; float3 Extent; float DrawPad3; };
StructuredBuffer<Instance> Instances : register(t0);
StructuredBuffer<Material> Materials : register(t1);
StructuredBuffer<Light> Lights : register(t2);
StructuredBuffer<float4> Movers : register(t17);       // where each mover is this frame: three rows of a 3x4 matrix apiece
StructuredBuffer<uint> LightGrid : register(t18);      // which lit lamps reach each square of the garden's plan (GardenGpu.Lamps): a first entry and a count for each square, then the lists
Texture2DArray<float4> Textures : register(t3);
Texture2D<float> ShadowMap : register(t4);
Texture2D<float4> Reflection : register(t5);
Texture2D<float4> BackdropImage : register(t6);
Texture2D<float> SkyMap : register(t7);        // depth seen from straight above
Texture2D<float> SceneDepth : register(t8);    // the frame's own depth, one sample a pixel
Texture2D<float> Occlusion : register(t9);     // the ambient occlusion worked out from it
Texture2D<float4> LensA : register(t10);       // the lens passes: what this one reads (the frame, or a level of its glow)
Texture2D<float4> LensB : register(t11);       // the last pass: the glow, to add to the frame
TextureCubeArray<float> LampShadows : register(t12);   // what each lamp sees round it, as depth: the still scene, drawn once
Texture3D<float4> LightVolume : register(t13);         // the light bounced round the courtyard: six blocks side by side, one for each way a surface can face
TextureCubeArray<float4> Surroundings : register(t14); // what is all round the middle of the courtyard, and of the hall: the still scene, drawn once (Packed), blurrier in each mip
Texture2D<float> HallShadow : register(t15);           // the key light's view of the hall alone, as depth
Texture2D<float4> HallTint : register(t16);            // and, through the same view, the colour the stained panes in the light's way give it (white where there are none)
Texture2DArray<float4> RoundFaces : register(t10);     // those pictures' faces at one mip, while the next is made from it
SamplerState Aniso : register(s0);
SamplerComparisonState ShadowSampler : register(s1);
SamplerState Clamp : register(s2);

#ifdef RT
RaytracingAccelerationStructure Scene : register(t19);
StructuredBuffer<MeshInfo> Meshes : register(t20);
StructuredBuffer<SubInfo> Subs : register(t21);
ByteAddressBuffer Vertices : register(t22);
ByteAddressBuffer Indices : register(t23);
Texture2D<uint> LampSeen : register(t24);   // which lamps of its square's list each point of the frame's depth sees (RaysCS)
Texture2D<float4> KeyColour : register(t25);   // the colour the key light has at each point of the frame's depth (the stained panes on its way)
RWTexture2D<float4> KeyColourOut : register(u0);
RWTexture2D<uint> LampOut : register(u1);
RWTexture2D<float> KeyOut : register(u2);
Texture2D<float4> Solids : register(t26);      // the frame's solid surfaces: which way each faces (xyz) and how rough (a; 1: it asks for no mirror image) (GBufferPS)
Texture2D<float4> MirrorHalf : register(t27);  // what they and the pool mirror; a: 1 the pool's, 0.5 a solid's, 0 none (MirrorCS)
RWTexture2D<float4> MirrorOut : register(u3);
// The surfaces of glass and water at each pixel (LayerPS): how many; and for each, how far (view distance), which way it faces (x, y), which material (whole part; a half: z of its facing is negative)
Texture2D<uint> LayerCount : register(t28); Texture2DArray<float4> LayerData : register(t29);
Texture2DArray<float4> LayerSeen : register(t30);   // what LayersCS found for it: what it mirrors (solid glass: all that is seen of it); a: the key light's share plus one, negative where another surface of the same glass stands before it
Texture2DArray<uint> LayerLamps : register(t31);
globallycoherent RWTexture2D<uint> LayerCountOut : register(u4); RWTexture2DArray<float4> LayerDataOut : register(u5); RWTexture2DArray<float4> LayerSeenOut : register(u6); RWTexture2DArray<uint> LayerLampsOut : register(u7);
static const uint MaxLayers = 4;
RWTexture2D<float4> AirOut : register(u10);   // the light standing in the air, found with rays (AirCS); the lens reads it where the hall's tint is bound (Flags 64)
Texture2D<float> FaceDepth : register(t32); Texture2D<float> FaceKey : register(t33); Texture2D<float4> FaceTint : register(t34); RWTexture2D<float> FaceKeyOut : register(u8); RWTexture2D<float4> FaceTintOut : register(u9);   // a picture of the surroundings: its depth, and what FaceCS found from it (Flags 32)
static bool SeenRead = false; static uint SeenBits = 0;   // the lamps this pixel's surface sees, read once (a read per lamp per pixel holds the pass up)
static int Transparent = 0; static bool FromPasses = false;   // a surface of the colour pass: what the compute passes found is there to read
static int Layer = -1; static float LayerKey = 1; static uint LayerOpen = 0; static float3 LayerColour = 0; static bool LayerHidden = false;   // the surface of glass or water being lit, when one was listed
#define TraceSampler Aniso
#include "GardenTrace.hlsli"
static float2 Pixel;   // the pixel being lit: its rays are aimed by a hash of where it is, so the same frame is the same picture
static bool Sign = false;   // the logo is being lit: a sign of gilt letters, which keeps the soft picture of its surroundings (a ray off each facet of a letter shows the facets)
#endif

float3 Mountains(float2 uv) { return BackdropImage.SampleLevel(Aniso, uv, 0).rgb; }   // level 0: the way round wraps, which a screen-space derivative would smear

struct VIn { float4 Position : POSITION; float4 Normal : NORMAL; float2 Uv : TEXCOORD; };
struct VOut { float4 Position : SV_Position; float3 World : WORLD; float3 Normal : NORMAL; float2 Uv : TEXCOORD; float Out : OUTWARD; nointerpolation uint Id : INSTANCE; };

void Place(VIn v, uint instance, out float3 world, out float3 normal)
{
    Instance i = Instances[InstanceBase + instance];
    float3 local = Centre + Extent * v.Position.xyz;
    if (i.Flags & FDroplet)
    {
        float3 pos, vel; float size; Droplet(i.Index, Time, pos, vel, size);
        world = DropletPlace(local, pos, vel, size); normal = local; return;
    }
    if (i.Flags & FMover)
    {
        float4 r0 = Movers[i.Index * 3], r1 = Movers[i.Index * 3 + 1], r2 = Movers[i.Index * 3 + 2];
        world = float3(dot(r0.xyz, local) + r0.w, dot(r1.xyz, local) + r1.w, dot(r2.xyz, local) + r2.w);
        normal = float3(dot(r0.xyz, v.Normal.xyz), dot(r1.xyz, v.Normal.xyz), dot(r2.xyz, v.Normal.xyz)); return;
    }
    world = Swayed(mul(Rotation(i), local) + Translation(i), i);
    normal = mul(Rotation(i), v.Normal.xyz);
    if (i.Flags & FLogo) { float3 pivot = Translation(i); world = LogoMove(world, pivot); normal = LogoTurn(normal); }
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

// Depth alone, through whichever view the pass's constants hold as ShadowViewProj: the sun's, the one straight down, or the camera's own.
[RootSignature(RS)]
ShadowOut ShadowVS(VIn v, uint instance : SV_InstanceID)
{
    float3 w, n; Place(v, instance, w, n);
    ShadowOut o; o.Position = mul(ShadowViewProj, float4(w, 1)); o.Uv = v.Uv; return o;
}

// The frame's leaves as depth alone, before its light (MainVS's own depth, to the bit): a leaf's edge takes as many of the pixel's
// samples as it covers of the pixel, so the edge is as smooth as alpha to coverage makes it - and afterwards each sample is lit once,
// by the leaf that owns it, however many leaves lie behind one another there.
uint LeafDepthPS(VOut i) : SV_Coverage
{
    Material m = Materials[MaterialIndex];
    float a = m.Texture >= 0 ? Textures.Sample(Aniso, float3(m.Pattern.x > 0 ? i.Uv * m.Pattern.xy : i.Uv, m.Texture)).a * m.Alpha : m.Alpha;
    uint n = Samples > 1 ? (uint)round(saturate((a - 0.5) / max(fwidth(a), 1e-4) + 0.5) * Samples) : (a >= 0.5 ? 1 : 0);
    if (n == 0) discard;
    return (1u << n) - 1;
}

// Leaves cut out of their cards cast leaf-shaped shadows.
void ShadowPS(ShadowOut i)
{
    Material m = Materials[MaterialIndex];
    if (m.Kind == KCutout && Textures.Sample(Aniso, float3(m.Pattern.x > 0 ? i.Uv * m.Pattern.xy : i.Uv, m.Texture)).a < 0.5) discard;
}

// The frame's light as it is kept until the lens: squeezed into 0..1 (c / (1 + its brightest colour)) and stored as its square root,
// which ten bits a colour hold without banding from the hall's shade up to the sun's glint. Averaging the squeezed values (MSAA's
// resolve, a see-through surface's blend) weighs a pixel's samples as the eye would, so a bright edge stays smooth.
float3 Pack(float3 c) { c = max(c, 0); return sqrt(c / (1 + max(c.r, max(c.g, c.b)))); }
float3 Unpack(float3 p) { float3 c = p * p; return c / max(1 - max(c.r, max(c.g, c.b)), 1.0 / 128); }

// A stained pane as the key light's view of the hall has it: the colour it gives the light that passes (blended as a product, so
// two panes in a row both count).
float4 TintPS(ShadowOut i) : SV_Target { return float4(StainTint(Materials[MaterialIndex]), 1); }

// The hall has a shadow map of its own (InHall says where), three times as fine as the courtyard's: its light comes through lattices a finger wide.

float ShadowMapped(float3 p, float3 n)
{
    bool hall = InHall(p);
    float4 s = mul(hall ? HallViewProj : ShadowViewProj, float4(p + n * (hall ? 0.015 : 0.014), 1));
    float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0) || any(uv > 1)) return 1;
    // three taps by three (five by five at the heaviest level), each the mean of four texels, spread over as wide a square as the level asks
    // (the hall's always five by five, two texels apart: the low sun lays its windows' light along the floor and up the far wall, a
    // texel of the map drawn out to a hand's breadth there - its edge is spread over several, or it shows as steps and shimmers as the sun moves)
    float depth = s.z - (hall ? 0.0006 : 0.00025), sum = 0; int r = hall || ShadowTaps >= 3 ? 2 : 1; float2 apart = (hall ? 2.0 : (2.0 * ShadowTaps + 1) / (2 * r + 1)) * ShadowTexel;
    if (hall) { [loop] for (int y = -r; y <= r; y++) [loop] for (int x = -r; x <= r; x++) sum += HallShadow.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * apart, depth); }
    else { [loop] for (int y = -r; y <= r; y++) [loop] for (int x = -r; x <= r; x++) sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * apart, depth); }
    return sum / ((2 * r + 1) * (2 * r + 1));
}

#ifdef RT
// How much of the key light reaches p: of ShadowTaps rays aimed at points of its disc, the share that meet nothing - shadows as
// sharp as what casts them is near, soft-edged far from it, of every leaf and lattice whatever its size.
// The k-th of n directions spread evenly over the key light's disc (a spiral from its middle out), turned by an angle of the pixel's own.
float3 ToKey(uint k, uint n, float turn)
{
    float3 t = normalize(cross(SunDir, abs(SunDir.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0))), b = cross(SunDir, t);
    float r = sqrt((k + 0.5) / n) * (Day.z > 0 ? 0.012 : 0.007), a = k * 2.399963 + turn;
    return normalize(SunDir + (t * cos(a) + b * sin(a)) * r);
}

// The share of the key light's disc seen from o: two rays to opposite sides of its rim first and, only where they disagree (the edge of a
// shadow), five more over the whole of it.
float KeySeen(float3 o, float2 pixel)
{
    float turn = Turn(pixel), open = 0; uint k;
    float3 t = normalize(cross(SunDir, abs(SunDir.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0))), side = (t * cos(turn) + cross(SunDir, t) * sin(turn)) * (Day.z > 0 ? 0.012 : 0.007);
    open = (Occluded(o, normalize(SunDir + side), 300) ? 0 : 1) + (Occluded(o, normalize(SunDir - side), 300) ? 0 : 1);
    if (open == 0 || open == 2) return open / 2;
    [loop] for (k = 0; k < 5; k++) open += Occluded(o, ToKey(k, 5, turn), 300) ? 0 : 1;
    return open / 7;
}

static const int2 Around[8] = { int2(-1, -1), int2(0, -1), int2(1, -1), int2(-1, 0), int2(1, 0), int2(-1, 1), int2(0, 1), int2(1, 1) };

// The frame's own surfaces take the key light's share from the picture worked out before the frame (SunShadePS, smoothed: in the
// place the pool's mirror image has without rays), which is free of the grain a few rays a pixel leave at a shadow's edge. What is
// not the surface that picture saw at this pixel - glass, water, steam, an edge where the pixel's samples are of two surfaces, the
// pictures of the surroundings - asks for itself.
// The point of the frame's depth at or next to this pixel that lies on p's surface (at an edge, the pixel's centre may be of another).
int2 SeenPixel(float3 p)
{
    float z = dot(p - Eye, CamForward), seen = Ambience.z / max(SceneDepth.Load(int3(Pixel, 0)), 1e-6);
    if (abs(z - seen) < 0.015 * z + 0.01) return int2(Pixel);
    [unroll] for (int k = 0; k < 8; k++)
    {
        int2 q = clamp(int2(Pixel) + Around[k], 0, int2(ViewSize) - 1); float near = Ambience.z / max(SceneDepth.Load(int3(q, 0)), 1e-6);
        if (abs(z - near) < 0.015 * z + 0.01) return q;
    }
    return int2(Pixel);
}

float Shadow(float3 p, float3 n)
{
    if (Layer >= 0) return LayerKey;
    if (Flags & 32) return FaceKey.Load(int3(Pixel, 0));
    if (FromPasses)
    {
        float z = dot(p - Eye, CamForward), seen = Ambience.z / max(SceneDepth.Load(int3(Pixel, 0)), 1e-6);
        if (abs(z - seen) < 0.015 * z + 0.01) return Reflection.Load(int3(Pixel, 0)).r;
        // an edge, where this sample is of another surface than the pixel's centre: the mean of the neighbours that are of this one, so no hard grain runs along it
        float sum = 0, count = 0;
        [unroll] for (int k = 0; k < 8; k++)
        {
            int2 q = clamp(int2(Pixel) + Around[k], 0, int2(ViewSize) - 1);
            float near = Ambience.z / max(SceneDepth.Load(int3(q, 0)), 1e-6);
            if (abs(z - near) < 0.015 * z + 0.01) { sum += Reflection.Load(int3(q, 0)).r; count++; }
        }
        return count > 0 ? sum / count : Reflection.Load(int3(Pixel, 0)).r;
    }
#ifdef RAYS
    return Occluded(p + n * 0.012, SunDir, 300) ? 0 : 1;
#else
    return 1;
#endif
}

// The colour the key light has where it falls inside the hall: that of the stained pane on its way there.
float3 KeyTint(float3 p)
{
    if (!InHall(p) || p.z < -3.09) return 1;
    if (Flags & 32) return FaceTint.Load(int3(Pixel, 0)).rgb;
#ifdef RAYS
    if (!FromPasses) return Through(p, SunDir, 60);
#endif
    return KeyColour.Load(int3(SeenPixel(p), 0)).rgb;
}

#ifdef RAYS
// Whether a lamp's light reaches p: a ray to its bulb.
float LampRay(Light L, float3 p, float3 n)
{
    float3 o = p + n * 0.012, q = L.Position - o; float d = length(q);   // (to its middle: a point of the bulb chosen by chance would leave a grain along every shadow's edge)
    return Occluded(o, q / d, max(0.01, d - 0.05)) ? 0 : 1;
}
#endif
#else
float Shadow(float3 p, float3 n) { return ShadowMapped(p, n); }

// The colour the key light has where it falls inside the hall: that of the stained panes it came through (the orsi stand at z -3.1).
float3 KeyTint(float3 p)
{
    if (!InHall(p) || p.z < -3.09) return 1;
    float4 s = mul(HallViewProj, float4(p, 1)); float2 uv = s.xy * float2(0.5, -0.5) + 0.5; float t = 2.5 / 2048;   // the mean of five taps: a pane's edge is as soft as its shadow's
    return (HallTint.SampleLevel(Clamp, uv, 0).rgb + HallTint.SampleLevel(Clamp, uv + float2(t, t), 0).rgb + HallTint.SampleLevel(Clamp, uv + float2(-t, t), 0).rgb
          + HallTint.SampleLevel(Clamp, uv + float2(t, -t), 0).rgb + HallTint.SampleLevel(Clamp, uv - float2(t, t), 0).rgb) * 0.2;
}
#endif

// The sun's glint on a surface (a mirror image of it, p's normal n): found only where it is bright enough to see - the shadow ray is not sent for a term that comes to nothing
// (a pool covers most of the picture, and almost none of it mirrors the sun).
float3 Glint(float3 r, float power, float3 p, float3 n)
{
    float g = SunOn > 0 ? pow(saturate(dot(r, SunDir)), power) : 0;
    [branch] if (g > 0.002) return g * SunColor * SunOn * Shadow(p, n);
    return 0;
}

// How much of a lamp's light reaches a point: what the lamp sees in that direction (its cube of depths, six views of ninety degrees)
// is no nearer than the point itself. Four taps round the direction, turned differently in each pixel, each the mean of four texels:
// a chair's legs, a lantern's cage and the hall's columns lay soft-edged shadows away from their lamps.
float LampShadowMap(Light L, float3 p, float3 n, float2 pixel)
{
    if (L.Shadow <= 0) return 1;
    float3 d = p + n * 0.03 - L.Position; float m = max(abs(d.x), max(abs(d.y), abs(d.z)));
    const float near = 0.03; float far = L.Range, held = max(m - (0.02 + 0.015 * m), near);   // a little nearer: a surface does not shadow itself
    float depth = far / (far - near) * (1 - near / held);
    float3 t = normalize(cross(d, abs(d.y) < 0.8 * m ? float3(0, 1, 0) : float3(1, 0, 0))), b = normalize(cross(d, t));
    float a = Turn(pixel), sum = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float w = a + k * 1.5707963;
        sum += LampShadows.SampleCmpLevelZero(ShadowSampler, float4(d + (t * cos(w) + b * sin(w)) * m * 0.01, L.Shadow - 1), depth);
    }
    return sum / 4;
}

// A lamp's shadow: from its cube - or, with rays, what RaysCS found for this pixel's surface, or a ray of its own for what that pass did not see.
float LampShadow(Light L, float3 p, float3 n, float2 pixel, uint j)
{
#ifdef RT
    if (!(Flags & 8) || L.Range < 2) return 1;   // a picture of the surroundings, seen blurred; a firefly's glow reaches a hand's breadth
    if (j < 32 && Layer >= 0) return (LayerOpen >> j) & 1;
    if (j < 32 && FromPasses)
    {
        if (!SeenRead) { SeenBits = LampSeen.Load(int3(SeenPixel(p), 0)); SeenRead = true; }
        return (SeenBits >> j) & 1;
    }
#ifdef RAYS
    return LampRay(L, p, n);
#else
    return 1;
#endif
#else
    return LampShadowMap(L, p, n, pixel);
#endif
}

// How much of the sky stands open over a point: the share of a ring of taps round it, a metre and a half across, that nothing is
// above. Under the canopy, inside the hall and beneath a tree's crown the sky's light does not arrive - which is most of what tells
// a lit garden from a painted one.
float SkyOpen(float3 p, float3 n, float2 pixel)
{
    if (Grid2.w > 0 && (Flags & 8)) return 1;   // the light volume and the pictures of the surroundings know it already: nothing asks
    float4 s = mul(SkyViewProj, float4(p + n * 0.12 + float3(0, 0.15, 0), 1));
    float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0) || any(uv > 1)) return 1;
    float a = Turn(pixel), sum = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float r = sqrt((k + 0.5) / 8) * 0.011, t = k * 2.399963 + a;
        sum += SkyMap.SampleCmpLevelZero(ShadowSampler, uv + float2(cos(t), sin(t)) * r, s.z);
    }
    return sum / 8;
}

// The occlusion image, smoothed over the four by four pixels round this one (four taps, each the mean of two by two).
float Occluded(float2 pixel)
{
    if (Ambience.x <= 0 || (Flags & 2)) return 1;
    float2 t = 1 / ViewSize;
    return (Occlusion.SampleLevel(Clamp, (pixel + float2(-1.5, -1.5)) * t, 0) + Occlusion.SampleLevel(Clamp, (pixel + float2(0.5, -1.5)) * t, 0)
          + Occlusion.SampleLevel(Clamp, (pixel + float2(-1.5, 0.5)) * t, 0) + Occlusion.SampleLevel(Clamp, (pixel + float2(0.5, 0.5)) * t, 0)) * 0.25;
}

// The light a surface at p facing n gets from everything but the sun and the lamps themselves - the sky where it can be seen, and
// what the sunlit paving, the walls and the lit rooms give back: read from the volume the ray tracer worked out (GardenLightVolume),
// one spacing out from the surface (so the points it is blended from all stand on the surface's own side of its wall), as the mix of
// the three directions the surface faces. Without a volume: the sky's own light where the sky stands open, and a little under a roof.
float3 Bounced(float3 p, float3 n, float open)
{
    if (Grid2.w <= 0) return Ambient(n) * lerp(0.3, 1, open);
    float3 size = Grid2.xyz, f = clamp(((p + n * Grid.w - Grid.xyz) / Grid.w + 0.5) / size, 0.5 / size, 1 - 0.5 / size), w = n * n;
    return w.x * LightVolume.SampleLevel(Clamp, float3((f.x + (n.x < 0 ? 1 : 0)) / 6, f.yz), 0).rgb
         + w.y * LightVolume.SampleLevel(Clamp, float3((f.x + (n.y < 0 ? 3 : 2)) / 6, f.yz), 0).rgb
         + w.z * LightVolume.SampleLevel(Clamp, float3((f.x + (n.z < 0 ? 5 : 4)) / 6, f.yz), 0).rgb;
}

// The lit lamps whose light can reach p: where its square's list starts in LightGrid, and how many it holds. The garden's plan is
// cut into squares two metres across (GardenGpu.Lamps fills the lists each frame), so a pixel asks a dozen lamps, not all sixty.
uint2 LampsAt(float3 p)
{
    int2 c = clamp(int2(floor((p.xz - float2(-16, -36)) * 0.5)), 0, int2(15, 21)); uint i = (c.y * 16 + c.x) * 2;
    return uint2(LightGrid[i], LightGrid[i + 1]);
}

// What a surface at p mirrors along r. A rasteriser cannot follow the ray, so the garden was drawn once all round from the middle of
// the courtyard and from the middle of the hall: the ray is carried to where it leaves the courtyard's (or the hall's) box and the
// picture looked up toward that point, so a window pane shows the wall across the room and the gilt logo the garden, where they are.
// The rougher the surface the blurrier the mip; a matt one takes the light volume's word for that direction instead.
float3 Pictured(float3 p, float3 r, float roughness, float open)
{
    float3 soft = Bounced(p, r, open) * (Grid2.w > 0 ? 1 : 3);
    if (!(Flags & 8)) return lerp(SkyColor(r) * lerp(0.2, 1, open), soft, saturate(roughness * 1.4));
    bool inside = all(p > Round1Low.xyz - 0.2) && all(p < Round1High.xyz + 0.2);
    float3 low = inside ? Round1Low.xyz : Round0Low.xyz, high = inside ? Round1High.xyz : Round0High.xyz, from = inside ? Round1.xyz : Round0.xyz;
    float3 along = float3(abs(r.x) > 1e-5 ? r.x : 1e-5, abs(r.y) > 1e-5 ? r.y : 1e-5, abs(r.z) > 1e-5 ? r.z : 1e-5);
    float3 t = (lerp(low, high, step(0, r)) - p) / along;
    float3 d = p + r * max(min(t.x, min(t.y, t.z)), 0) - from;
    float3 sharp = Unpack(Surroundings.SampleLevel(Clamp, float4(d, inside ? 1 : 0), min(roughness * 9, Round0.w - 1)).rgb);
    return lerp(sharp, soft, smoothstep(0.3, 0.6, roughness));
}

#ifdef RT
#ifdef RAYS
// What a ray from a surface at p (facing n) along r meets, lit as the frame's own surfaces are: by the light bounced round it, the
// key light and the lamps in reach (a shadow ray each), and - in place of a mirror image of its own - the picture of its surroundings.
float3 Reflected(float3 p, float3 n, float3 r)
{
    // Solid glass (a vase, a tumbler) met on the way is looked through, as SeenThrough does for the eye - bent at each surface, a little of the room mirrored on it - so a vase
    // in a mirror is glass there too, not a tinted solid.
    float3 tint = 1, glints = 0, o = p + n * 0.012, d = r;
    [loop] for (uint k = 0; k < 5; k++)
    {
        Hit h; if (!Trace(o, d, 300, MaskVisible, h)) return glints + tint * Sky(d);
        Material m = Materials[h.Material]; float3 v = -d; Surface s = SurfaceAt(h, v);
        if (m.Kind == KGlass && m.Pattern.w > 0)
        {
            bool entering = dot(h.N, d) < 0; float3 nh = entering ? h.N : -h.N; float fr = 0.04 + 0.96 * pow(1 - saturate(dot(nh, v)), 5);
            glints += tint * fr * Pictured(h.P, reflect(d, nh), 0.03, 1) * 0.5; tint *= (1 - fr) * lerp(1, m.Base, 0.35);
            float3 bent = refract(d, nh, entering ? 1 / 1.5 : 1.5); d = any(bent) ? bent : reflect(d, nh); o = h.P + d * 0.004; continue;
        }
        if (m.Kind == KWater) return glints + tint * lerp(m.Base * Bounced(h.P, float3(0, 1, 0), 1) * 0.5, Sky(reflect(d, float3(0, 1, 0))), 0.3);
        if (m.Kind == KGlass) return glints + tint * (m.Base * (Bounced(h.P, s.Normal, 1) + Bounced(h.P, -s.Normal, 1)) * 0.5 + s.Emission + Pictured(h.P, reflect(d, s.Normal), 0.03, 1) * 0.08);
        float3 c = s.Albedo * Bounced(h.P, s.Normal, 1) * (1 - s.Metallic) * s.Occlusion + s.Emission, q = h.P + s.Normal * 0.012;
        if (SunOn > 0 && dot(s.Normal, SunDir) > 0 && !Occluded(q, SunDir, 300)) c += Brdf(s, v, SunDir, SunColor * KeyTint(h.P));
        uint2 lamps = LampsAt(h.P);
        [loop] for (uint j = 0; j < lamps.y; j++)
        {
            uint li = LightGrid[lamps.x + j]; Light L = Lights[li]; float3 l; float dist; float3 e = LightAt(L, h.P, l, dist) * LampLit(L, li);
            if (max(e.r, max(e.g, e.b)) <= 0.01 || dot(s.Normal, l) <= 0) continue;
            if (L.Range >= 2 && Occluded(q, l, max(0.01, dist - 0.05))) continue;
            c += Brdf(s, v, l, e);
        }
        return glints + tint * (c + Pictured(h.P, reflect(d, s.Normal), s.Roughness, 1) * EnvBrdf(s, v));
    }
    return glints + tint * Sky(d);
}

// What the eye sees through solid glass (a vase, crystal), entered at p where the surface faces the eye along n: the ray is bent at every glass
// surface it meets (the wall, the water in it, the wall again - by 1.5 going in and coming out) until it reaches what is not glass, which is lit
// as a mirror image is; light that cannot get out of the glass is mirrored back in, as it is in life.
float3 SeenThrough(float3 p, float3 n, float3 v)
{
    float3 d = refract(-v, n, 1 / 1.5), tint = 1, o = p - n * 0.004;
    if (!any(d)) d = reflect(-v, n);
    [loop] for (uint k = 0; k < 5; k++)
    {
        Hit h; if (!Trace(o, d, 300, MaskVisible, h)) return tint * Sky(d);
        Material m = Materials[h.Material];
        if (m.Kind != KGlass) return tint * Reflected(o, d, d);
        bool entering = dot(h.N, d) < 0; float3 nh = entering ? h.N : -h.N;
        float3 bent = refract(d, nh, entering ? 1 / 1.5 : 1.5);
        tint *= lerp(1, m.Base, 0.35);
        d = any(bent) ? bent : reflect(d, nh);
        o = h.P + d * 0.004;
    }
    return tint * Sky(d);
}
#endif
#endif

#ifdef RT
// What LayerPS listed for the surface at p, and what LayersCS found for it, at this pixel or, at an edge whose centre the surface misses, the next one.
void FindLayer(float3 p, bool solid)
{
    if (!(Flags & 16)) return;
    FromPasses = true;   // nothing below sends a ray from the colour pass: it would hold the pass up for a few pixels' sake
    // (the list fills in whatever order the card shades them: the nearest match, and a pixel with more than the list holds uses none of it, or the same frame would come out different)
    float z = dot(p - Eye, CamForward), best = 0.002 * z + 0.001; int found = -1; int2 at = int2(Pixel);
    [loop] for (int n = 0; n < 5 && found < 0; n++)
    {
        int2 q = clamp(int2(Pixel) + (n == 0 ? int2(0, 0) : Around[n * 2 - 1]), 0, int2(ViewSize) - 1); uint count = LayerCount.Load(int3(q, 0));
        if (count > MaxLayers)
        {   // (past what the list holds: LayersCS found the front surface of solid glass by a ray from the eye, and left how far it is in place of the first's lamps)
            if (solid) { float gap = abs(asfloat(LayerLamps.Load(int4(q, 0, 0))) - z); if (gap < best) { best = gap; found = 0; at = q; } }
            continue;
        }
        [loop] for (uint k = 0; k < count; k++) { float gap = abs(LayerData.Load(int4(q, k, 0)).x - z); if (gap < best) { best = gap; found = k; at = q; } }
    }
    if (found < 0) return;
    float4 seen = LayerSeen.Load(int4(at, found, 0)); Layer = found; LayerColour = seen.rgb; LayerKey = abs(seen.a) - 1; LayerHidden = seen.a < 0; LayerOpen = LayerLamps.Load(int4(at, found, 0));
}
#endif

// What a surface mirrors along r: the picture of its surroundings - or, with ray tracing, for a surface smooth enough to show a
// mirror image (and mirroring enough of it to matter: weight), what the ray along r really meets.
float3 Mirrored(float3 p, float3 n, float3 r, float roughness, float open, float weight)
{
    float3 pictured = Pictured(p, r, roughness, open);
#ifdef RT
    if ((Flags & 8) && !Sign && roughness < 0.3 && weight > 0.02)
    {
        float3 seen;
        if (Layer >= 0) seen = LayerColour;
        else if (FromPasses) { float4 m = MirrorHalf.Load(int3(SeenPixel(p), 0)); seen = m.a > 0 && Transparent == 0 ? m.rgb : pictured; }   // (an edge's sample, where no neighbour asked; glass past what its list holds)
        else
#ifdef RAYS
            seen = Reflected(p, n, r);
#else
            seen = pictured;
#endif
        return lerp(seen, pictured, smoothstep(0.1, 0.3, roughness));
    }
#endif
    return pictured;
}

float3 Lit(Surface s, float3 p, float3 v, float2 pixel)
{
    // the light from all round, as far as the surface's own corners and crevices let it in
    float open = SkyOpen(p, s.Normal, pixel), near = Occluded(pixel) * s.Occlusion;
    float3 c = s.Albedo * Bounced(p, s.Normal, open) * (1 - s.Metallic) * near + s.Emission;
    if (SunOn > 0 && dot(s.Normal, SunDir) > 0)
    {
        float lit = Shadow(p, s.Normal);
        if (lit > 0) c += Brdf(s, v, SunDir, SunColor * KeyTint(p)) * lit * lerp(1, near, 0.45);
    }
    uint2 lamps = LampsAt(p);
    [loop] for (uint j = 0; j < lamps.y; j++)
    {
        uint k = LightGrid[lamps.x + j]; Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, p, l, d);
        if (max(e.r, max(e.g, e.b)) <= 0.004 || dot(s.Normal, l) <= 0) continue;   // (the last of a lamp's reach is under a hundredth of what the eye can tell at night)
        c += Brdf(s, v, l, e * LampLit(L, k)) * LampShadow(L, p, s.Normal, pixel, j) * lerp(1, near, 0.6);   // a lamp's light does not reach into a corner either
    }
    // every surface mirrors its surroundings a little, a metal or a polished floor a lot
    float3 mirrors = EnvBrdf(s, v);
    c += Mirrored(p, s.Normal, reflect(-v, s.Normal), s.Roughness, open, max(mirrors.r, max(mirrors.g, mirrors.b))) * mirrors * near;
    return c;
}

// Which way the texture's u and v run over the surface at this pixel, from how the position and the coordinates change across the screen.
void TangentFrame(float3 p, float2 d1, float2 d2, float3 n, out float3 t, out float3 b)
{
    float3 dp1 = ddx(p), dp2 = ddy(p);
    float3 p2 = cross(dp2, n), p1 = cross(n, dp1); float side = dot(dp1, p2) < 0 ? -1 : 1;   // the mirror image and back faces turn the other way
    t = (p2 * d1.x + p1 * d2.x) * side; b = (p2 * d1.y + p1 * d2.y) * side;
}

// The material at this pixel of a surface facing n.
Surface SurfaceHere(VOut i, Material m, float3 n)
{
    // the texture's coordinates and how they change across the screen: a surface laid by world position changes face, not coordinates, at an edge
    bool world; float2 uv = TexCoords(m, i.Uv, i.World, n, world), d1, d2;
    if (world) { d1 = FaceCoords(ddx(i.World), n) * m.Pattern.z; d2 = FaceCoords(ddy(i.World), n) * m.Pattern.z; }
    else { float2 rep = m.Kind <= KCutout && m.Pattern.x > 0 ? m.Pattern.xy : 1; d1 = ddx(i.Uv) * rep; d2 = ddy(i.Uv) * rep; }
    // What lies under the pool's water is seen through its ripples, never sharply: its texture is taken at the mip its longest
    // stretch across the pixel asks for, so the tiles' joints blur away toward the far end of the pool instead of coming and going
    // from pixel to pixel as the walk moves (stretched filtering keeps them a pixel thin, and has too few taps at that slant).
    if (i.World.y < WaterLevel) { float l1 = length(d1), l2 = length(d2), l = max(l1, l2); d1 *= l / max(l1, 1e-9); d2 *= l / max(l2, 1e-9); }
    float4 texel = m.Texture >= 0 ? Textures.SampleGrad(Aniso, float3(uv, m.Texture), d1, d2) : 1;
    float4 relief = Textures.SampleGrad(Aniso, float3(uv, max(m.NormalTexture, 0)), d1, d2);
    Surface s = MaterialSurface(m, i.World, n, texel);
    if (m.NormalTexture >= 0)
    {
        float3 t, b; TangentFrame(i.World, d1, d2, n, t, b); Relief(s, t, b, relief);
        // Where a pixel holds more than one bump of the relief (the pool's tiles from the stairs, the paving far down the walk), each
        // bump's own glint would come and go as the eye moves. The surface is taken as rougher by as much as its direction changes
        // across the pixel (Tokuyoshi and Kaplanyan 2019): the glints spread into the sheen they add up to.
        float3 nx = ddx(s.Normal), ny = ddy(s.Normal); float a = s.Roughness * s.Roughness;
        s.Roughness = sqrt(sqrt(a * a + min(dot(nx, nx) + dot(ny, ny), 0.18)));
    }
    return s;
}

float4 Opaque(VOut i, bool cutout)
{
#ifdef RT
    Pixel = i.Position.xy; FromPasses = (Flags & 16) != 0;
#endif
    if ((Flags & 2) && i.World.y < WaterLevel - 0.02) discard;   // the mirror image holds only what is above the water
    Material m = Materials[MaterialIndex];
    float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;   // two-sided: leaves and cards are seen from both faces
    Surface s = SurfaceHere(i, m, n);
#ifdef RT
    Sign = (Instances[i.Id].Flags & FLogo) != 0;
#endif
    if (Instances[i.Id].Flags & FLogo) s.Emission += s.Albedo * Logo.w;   // the logo is a sign: it keeps a little of its own gold whatever it mirrors
    float alpha = 1;
    if (cutout)
    {
        if (Flags & 16) { }
        else if (Flags & 4) alpha = saturate((s.Alpha - 0.5) / max(fwidth(s.Alpha), 1e-4) + 0.5);   // alpha to coverage: a crisp edge, smoothed by the samples
        else if (s.Alpha < 0.5) discard;
        // Foliage: no two plants quite the same green, a little less saturated than the card's photograph, darker inside the crown
        // (which the sky barely reaches), and the sun shining through a leaf seen against it.
        float h = Hash1(i.Id * 0.618);
        s.Albedo *= lerp(float3(0.82, 0.86, 0.80), float3(1.08, 1.04, 0.92), h);
        s.Albedo = lerp(dot(s.Albedo, float3(0.3, 0.59, 0.11)), s.Albedo, 0.9);
        float inner = lerp(0.42, 1, i.Out * i.Out);
        float3 c = Lit(s, i.World, v, i.Position.xy) - s.Albedo * Bounced(i.World, s.Normal, 1) * (1 - inner) * 0.6;
        if (SunOn > 0 && dot(n, SunDir) < 0) c += s.Albedo * s.Albedo * SunColor * 0.35 * -dot(n, SunDir) * Shadow(i.World, -n) * inner;
        return float4(Pack(Haze(max(c, 0), i.World)), alpha);
    }
    return float4(Pack(Haze(Lit(s, i.World, v, i.Position.xy), i.World)), alpha);
}

float4 OpaquePS(VOut i) : SV_Target { return Opaque(i, false); }
#ifdef RT
// The frame's solid surfaces, as its depth is laid: what MirrorCS needs to send their rays before the frame.
float4 GBufferPS(VOut i) : SV_Target
{
    Material m = Materials[MaterialIndex]; float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;
    Surface s = SurfaceHere(i, m, n);
    float3 mirrors = EnvBrdf(s, v);
    bool asks = !(Instances[i.Id].Flags & FLogo) && max(mirrors.r, max(mirrors.g, mirrors.b)) > 0.02;
    return float4(s.Normal * 0.5 + 0.5, asks ? s.Roughness : 1);
}

// Lists a surface of glass or water at this pixel for LayersCS (drawn against the frame's depth, before the frame).
[earlydepthstencil]
void LayerPS(VOut i)
{
    float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;
    uint k; InterlockedAdd(LayerCountOut[int2(i.Position.xy)], 1, k);
    if (k < MaxLayers) LayerDataOut[int3(i.Position.xy, k)] = float4(dot(i.World - Eye, CamForward), n.x, n.y, MaterialIndex + (n.z < 0 ? 0.5 : 0));
}
#endif
float4 CutoutPS(VOut i) : SV_Target { return Opaque(i, true); }

// Water and glass: blended over what is behind them.
float4 TransparentPS(VOut i) : SV_Target
{
#ifdef RT
    Pixel = i.Position.xy;
#endif
    if ((Flags & 2) && i.World.y < WaterLevel - 0.02) discard;
    Material m = Materials[MaterialIndex];
    float3 v = normalize(Eye - i.World), n = normalize(i.Normal); if (dot(n, v) < 0) n = -n;
#ifdef RT
    FromPasses = (Flags & 16) != 0;
#endif
    if (m.Kind == KSmoke)
    {   // steam: lit by what is round it and, in a beam of the key light, by that
        float3 c = m.Base * ((Bounced(i.World, n, 1) + Bounced(i.World, -n, 1)) * 1.5 + SunColor * SunOn * KeyTint(i.World) * Shadow(i.World, n) * 0.2);
        return float4(Pack(c), Puff(m, n, v));
    }
#ifdef RT
    Transparent = 1; FindLayer(i.World, m.Kind == KGlass && m.Pattern.w > 0);
#endif
    if (m.Kind == KWater)
    {
        bool level = abs(n.y) > 0.7;
        n = level ? WaterNormal(i.World, Time) : Running(n, i.World);   // a pool's surface ripples; a sheet or a stream of falling water keeps its own slope, jostled as it runs
        float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(n, v)), 5);
        float3 reflected;
        if (!level)
        {
            // falling water: what it mirrors, the daylight caught in it, and the sun's glint; more of it shows than of a still pool
            reflected = (Flags & 8) ? Mirrored(i.World, n, reflect(-v, n), 0.03, 1, 1) : Sky(reflect(-v, n));
            reflected += Glint(reflect(-v, n), 200, i.World, n);
            float3 caught = float3(0.82, 0.92, 0.96) * ((Bounced(i.World, n, 1) + Bounced(i.World, -n, 1)) * 1.5 + SunColor * SunOn * Shadow(i.World, n) * 0.25);
            float a = fresnel + (1 - fresnel) * 0.6;
            return float4(Pack((reflected * fresnel + caught * (1 - fresnel) * 0.6) / a), a);
        }
#ifdef RT
        if (Flags & 8)
        {   // what the water really mirrors, ripples and all (a ray a ripple would send under the surface is laid along it)
            float3 r = reflect(-v, n); if (r.y < 0.03) r = normalize(float3(r.x, 0.03, r.z));
            reflected = Layer >= 0 ? LayerColour : Pictured(i.World, r, 0.03, 1);
        }
        else
#endif
        if ((Flags & 1) && abs(i.World.y - WaterLevel) < 0.2)
        {
            float2 uv = i.Position.xy / ViewSize + n.xz * 0.04;
            reflected = Unpack(Reflection.SampleLevel(Clamp, uv, 0).rgb);
        }
        else reflected = Sky(reflect(-v, n));
        reflected += Glint(reflect(-v, n), 400, i.World, n);   // the sun's glint
        // the pool's tiles show through (blended at 1 - a); the water adds its mirror image and its own teal body, more of it the
        // more water the eye looks through (what the frame's depth says lies behind, along the ray): straight down the tiles are
        // clear, toward the far end of the pool they sink into the water's colour - and their fine grid no longer shimmers there
        float3 body = m.Base * Bounced(i.World, float3(0, 1, 0), 1) * 0.5;
        float density = 0.22;
        if (Ambience.x > 0)
        {
            float behind = Ambience.z / max(SceneDepth.Load(int3(i.Position.xy, 0)), 1e-6), through = max(behind - i.Position.w, 0) * length(Eye - i.World) / i.Position.w;
            density = lerp(0.22, 0.88, 1 - exp(-through * 0.5));
        }
        float a = fresnel + (1 - fresnel) * density;
        return float4(Pack((reflected * fresnel + body * (1 - fresnel) * density) / a), a);
    }
#ifdef RT
    if (m.Kind == KGlass && m.Pattern.w > 0 && (Flags & 8))
    {   // solid glass with rays: where it stands in front of another surface of the same glass the front one is the picture (the others are seen through it)
        if (Layer >= 0) { if (LayerHidden) discard; return float4(Pack(LayerColour), 1); }
        discard;   // (an edge no neighbour listed, or past what the list holds: the samples round it stand in)
    }
#endif
    // glass: the sky's mirror image on it and the sun's glint, over its own colour - a stained pane lets through about half of what
    // is behind it, in its colour; clear glass nearly all
    float fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
    float open = SkyOpen(i.World, n, i.Position.xy);
    float3 mirrored = ((Flags & 8) ? Mirrored(i.World, n, reflect(-v, n), 0.03, open, 1) : Sky(reflect(-v, n)) * lerp(0.25, 1, open)) + Glint(reflect(-v, n), 300, i.World, n);
    float3 own = m.Base * (Bounced(i.World, n, open) + Bounced(i.World, -n, open)) * 0.5 + m.Emission * Glowing(i.World);
#ifdef RT
    const float shine = 1.5;   // with rays a pane stands out a little more in the key light: enough for the lens to lay a faint glow round it
#else
    const float shine = 1;
#endif
    if (SunOn > 0)
    {   // lit from the front, glowing with the sun behind it (the side that faces away from the sun is not asked)
        float front = saturate(dot(n, SunDir)), back = saturate(-dot(n, SunDir)), lit = 0;
        [branch] if (front > 0) lit += 0.25 * front * Shadow(i.World, n);
        [branch] if (back > 0) lit += 0.5 * back * Shadow(i.World, -n);
        own += m.Base * SunColor * shine * lit / Pi;
    }
    uint2 lamps = LampsAt(i.World);
    [loop] for (uint j = 0; j < lamps.y; j++) { uint k = LightGrid[lamps.x + j]; Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, i.World, l, d) * LampLit(L, k); if (any(e > 0)) own += m.Base * e * 0.3 / Pi * LampShadow(L, i.World, n, i.Position.xy, j); }
    float saturation = 1 - min(m.Base.r, min(m.Base.g, m.Base.b)) / max(max(m.Base.r, max(m.Base.g, m.Base.b)), 1e-3);
    float density = m.Alpha < 1 ? m.Alpha : lerp(0.08, 0.6, saturation);
    float a = fresnel + (1 - fresnel) * density;
    return float4(Pack((mirrored * fresnel + own * (1 - fresnel) * density) / a), a);
}

// The sky: a full-screen triangle behind everything (depth 0: infinitely far).
struct SkyOut { float4 Position : SV_Position; float2 Ndc : NDC; };

[RootSignature(RS)]
SkyOut SkyVS(uint vertex : SV_VertexID)
{
    float2 uv = float2((vertex << 1) & 2, vertex & 2);
    SkyOut o; o.Ndc = uv * float2(2, -2) + float2(-1, 1); o.Position = float4(o.Ndc, 0, 1); return o;
}

float4 SkyPS(SkyOut i) : SV_Target
{
    float3 dir = normalize(CamForward + CamRight * i.Ndc.x * TanHalfFovY * Aspect + CamUp * i.Ndc.y * TanHalfFovY);
    return float4(Pack(Sky(dir)), 1);
}

// ——— the lens ———
// The glow: the frame's bright part at half its size, halved again four times (each level the mean of four by four texels of the one
// before, through four bilinear taps), then each level laid over the one above it through a tent of nine taps and added: a bright point
// ends as a soft halo some tens of pixels wide, a thirty-second of the work at each step down.
float2 LensUv(SkyOut i) { return i.Ndc * float2(0.5, -0.5) + 0.5; }
float2 LensTexel() { float w, h; LensA.GetDimensions(w, h); return 1 / float2(w, h); }

float4 GlowFirstPS(SkyOut i) : SV_Target
{
    float2 uv = LensUv(i), t = LensTexel(); float3 c = 0;
    [unroll] for (int k = 0; k < 4; k++) c += Glow(Unpack(LensA.SampleLevel(Clamp, uv + float2(k & 1 ? 1 : -1, k & 2 ? 1 : -1) * t, 0).rgb));
    return float4(c / 4, 1);
}

float4 GlowDownPS(SkyOut i) : SV_Target
{
    float2 uv = LensUv(i), t = LensTexel(); float3 c = 0;
    [unroll] for (int k = 0; k < 4; k++) c += LensA.SampleLevel(Clamp, uv + float2(k & 1 ? 1 : -1, k & 2 ? 1 : -1) * t, 0).rgb;
    return float4(c / 4, 1);
}

// Added to the level it is drawn over (the pipeline blends one and one).
float4 GlowUpPS(SkyOut i) : SV_Target
{
    float2 uv = LensUv(i), t = LensTexel(); float3 c = 0;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++) c += LensA.SampleLevel(Clamp, uv + float2(x, y) * t, 0).rgb * ((2 - abs(x)) * (2 - abs(y)) / 16.0);
    return float4(c, 1);
}

// A face of a picture of the surroundings at half the size: each texel the mean of four of the level before (still Packed: the
// mean weighs them as the eye would). InstanceBase: which face of which picture.
float4 RoundDownPS(SkyOut i) : SV_Target
{
    return RoundFaces.SampleLevel(Clamp, float3(LensUv(i), InstanceBase), 0);
}

float ViewDistance(int2 pixel) { return Ambience.z / max(SceneDepth.Load(int3(clamp(pixel, 0, int2(ViewSize) - 1), 0)), 1e-6); }

// The key light in the hall's air: the dust of a room shows the beams that come in at its windows, each the colour of its pane.
// Along the eye's ray, as far as it runs inside the hall and no farther than what it meets, the light is looked up at a dozen
// points (the hall's shadow map, the stained panes' colours) and what the air there turns toward the eye is added to the frame.
float3 Shafts(SkyOut i, int2 px)
{
    if (SunOn <= 0) return 0;
    float3 dir = normalize(CamForward + CamRight * i.Ndc.x * TanHalfFovY * Aspect + CamUp * i.Ndc.y * TanHalfFovY);
    float3 low = Round1Low.xyz + float3(0, 0, -0.45), high = Round1High.xyz, inv = 1 / float3(abs(dir.x) > 1e-5 ? dir.x : 1e-5, abs(dir.y) > 1e-5 ? dir.y : 1e-5, abs(dir.z) > 1e-5 ? dir.z : 1e-5);
    float3 a = (low - Eye) * inv, b = (high - Eye) * inv, t0 = min(a, b), t1 = max(a, b);
    float from = max(max(t0.x, t0.y), max(t0.z, 0)), to = min(min(t1.x, t1.y), min(t1.z, ViewDistance(px) / dot(dir, CamForward)));
    if (to <= from) return 0;
    int steps = max(14, (int)AirSteps); float step = (to - from) / steps, lit = 0; float3 sum = 0, start = Eye + dir * (from + step * frac(Turn(i.Position.xy) * 0.159155));
    [loop] for (int k = 0; k < steps; k++)
    {
        float3 p = start + dir * (step * k); float4 s = mul(HallViewProj, float4(p, 1)); float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
        float open = HallShadow.SampleCmpLevelZero(ShadowSampler, uv, s.z - 0.0004);
        if (open > 0) sum += open * (p.z > -3.09 ? HallTint.SampleLevel(Clamp, uv, 0).rgb : 1);
    }
    float toward = dot(dir, SunDir), phase = 0.6 + 1.6 * pow(saturate(toward * 0.5 + 0.5), 4);   // dust throws most of it on, toward an eye that looks into the beam
    return sum * step * SunColor * 0.0075 * phase * saturate(SunDir.y * 3.2);   // (a sun on the horizon lights the whole room's air: held back, or the room is all haze)
}

// The courtyard's own air in the key light: the haze over a sunlit garden is lit where the sun reaches it and dark in the shade of the
// walls, the cypresses and the hall, so the light stands in the air as beams between the trees and under the arcade - most of all
// looking toward the sun, the way fine droplets and dust throw light on (Henyey-Greenstein, g 0.55). Along the eye's ray, as far as
// what it meets and no farther than across the courtyard, the courtyard's shadow map is looked up at AirSteps points (the load level's
// number: the work grows with the pixels and the steps, as a game's volumetric light does); the hall's air is Shafts'. Moist air
// lies low: it thins with height above the paving.
float3 Air(SkyOut i, int2 px)
{
    if (SunOn <= 0 || AirSteps == 0) return 0;
    float3 dir = normalize(CamForward + CamRight * i.Ndc.x * TanHalfFovY * Aspect + CamUp * i.Ndc.y * TanHalfFovY);
    float to = min(ViewDistance(px) / dot(dir, CamForward), 70), step = to / AirSteps, start = step * frac(Turn(i.Position.xy) * 0.159155), lit = 0;
    [loop] for (uint k = 0; k < AirSteps; k++)
    {
        float3 p = Eye + dir * (start + step * k);
        if (InHall(p)) continue;
        float4 s = mul(ShadowViewProj, float4(p, 1)); float2 uv = s.xy * float2(0.5, -0.5) + 0.5;
        float open = any(uv < 0) || any(uv > 1) ? 1 : ShadowMap.SampleCmpLevelZero(ShadowSampler, uv, s.z - 0.0005);
        lit += open * exp(-max(p.y, 0) * 0.3);
    }
    const float g = 0.55; float phase = (1 - g * g) / pow(1 + g * g - 2 * g * dot(dir, SunDir), 1.5);   // (times 4 pi: 1 on average over all directions)
    return lit * step * SunColor * 0.0006 * phase * saturate(SunDir.y * 5);   // (a sun on the horizon lights all the air there is: held back)
}

// Smoothing of the frame's edges (not MSAA: one pass of the lens, no extra target). Post.y: 1 light, 2 standard. The edge is found in the picture as it will be shown
// (the tone curve's own compression, so a bright sky does not make every pixel an edge); thin things far off are met with a lower threshold, since they
// are what shimmers. Light blends an edge pixel with the mean of its four neighbours; standard also looks at the corners for the edge's direction and
// blends with the neighbour across it. Edge is how much was blended (the lens does not sharpen those pixels again).
float3 Squash(float3 c) { return c / (1 + c); }
float3 Unsquash(float3 c) { return c / max(1 - c, 0.001); }
float SquashLuma(float3 c) { return dot(Squash(c), float3(0.299, 0.587, 0.114)); }
float3 Smoothed(int2 px, float3 m, out float edge)
{
    edge = 0; if (Post.y < 0.5) return m;
    int2 hi2 = int2(ViewSize) - 1;
    float3 n = Unpack(LensA.Load(int3(clamp(px + int2(0, -1), 0, hi2), 0)).rgb), s = Unpack(LensA.Load(int3(clamp(px + int2(0, 1), 0, hi2), 0)).rgb);
    float3 e = Unpack(LensA.Load(int3(clamp(px + int2(1, 0), 0, hi2), 0)).rgb), w = Unpack(LensA.Load(int3(clamp(px + int2(-1, 0), 0, hi2), 0)).rgb);
    float lm = SquashLuma(m), ln = SquashLuma(n), ls = SquashLuma(s), le = SquashLuma(e), lw = SquashLuma(w);
    float top = max(lm, max(max(ln, ls), max(le, lw))), range = top - min(lm, min(min(ln, ls), min(le, lw)));
    float far = saturate(ViewDistance(px) / 40), thresh = max(0.03, top * 0.1) * lerp(1, 0.5, far);
    if (range < thresh) return m;
    float sub = saturate(abs((ln + ls + le + lw) * 0.25 - lm) / max(range, 1e-4)), amount = lerp(0.35, 0.6, far) * sub * (2 - sub);
    float3 other = (Squash(n) + Squash(s) + Squash(e) + Squash(w)) * 0.25;
    if (Post.y > 1.5)
    {
        float3 nw = Unpack(LensA.Load(int3(clamp(px + int2(-1, -1), 0, hi2), 0)).rgb), ne = Unpack(LensA.Load(int3(clamp(px + int2(1, -1), 0, hi2), 0)).rgb);
        float3 sw = Unpack(LensA.Load(int3(clamp(px + int2(-1, 1), 0, hi2), 0)).rgb), se = Unpack(LensA.Load(int3(clamp(px + int2(1, 1), 0, hi2), 0)).rgb);
        float a = SquashLuma(nw), b = SquashLuma(ne), d = SquashLuma(sw), f = SquashLuma(se);
        float horizontal = abs(a + b - 2 * ln) + 2 * abs(ln + ls - 2 * lm) + abs(d + f - 2 * ls), vertical = abs(a + d - 2 * lw) + 2 * abs(lw + le - 2 * lm) + abs(b + f - 2 * le);
        float3 toward = horizontal >= vertical ? (abs(ln - lm) >= abs(ls - lm) ? n : s) : (abs(lw - lm) >= abs(le - lm) ? w : e);
        other = lerp(other, Squash(toward), 0.7); amount = max(amount, 0.5 * saturate(range / (thresh * 3)));
    }
    edge = amount;
    return Unsquash(lerp(Squash(m), other, amount));
}

// The frame through the lens: each pixel gathers, from a disc of taps as wide as the most blur, the pixels whose own blur reaches
// it (what lies behind a pixel in focus does not spill over it); the glow is added; the tone curve makes the picture.
float4 LensPS(SkyOut i) : SV_Target
{
    int2 px = int2(i.Position.xy);
    float3 c = Unpack(LensA.Load(int3(px, 0)).rgb); int taps = (int)Lens.w; float edge; c = Smoothed(px, c, edge);
    if (taps > 0)
    {
        float z0 = ViewDistance(px), b0 = Blur(z0), a = Turn(i.Position.xy), n = 1;
        [loop] for (int k = 0; k < taps; k++)
        {
            float r = sqrt((k + 0.5) / taps) * Lens.z, t = k * 2.399963 + a;
            int2 q = clamp(px + int2(round(float2(cos(t), sin(t)) * r)), 0, int2(ViewSize) - 1);
            float zq = ViewDistance(q), bq = Blur(zq); if (zq > z0) bq = min(bq, b0 * 2);
            c += lerp(c / n, Unpack(LensA.Load(int3(q, 0)).rgb), smoothstep(r - 0.5, r + 0.5, bq)); n += 1;
        }
        c /= n;
    }
    {
        // what the lens holds in focus it draws crisply: each such pixel is set off a little against the mean of its four neighbours
        // (never by more than half of itself, so a bright edge gets no dark rim)
        float3 own = Unpack(LensA.Load(int3(px, 0)).rgb), round = (Unpack(LensA.Load(int3(px + int2(1, 0), 0)).rgb) + Unpack(LensA.Load(int3(px - int2(1, 0), 0)).rgb) + Unpack(LensA.Load(int3(px + int2(0, 1), 0)).rgb) + Unpack(LensA.Load(int3(px - int2(0, 1), 0)).rgb)) * 0.25;
        c = max(c + clamp(own - round, -0.5 * own, 0.5 * own) * 0.45 * (1 - saturate(Blur(ViewDistance(px)))) * (1 - edge), 0);
    }
    if (Flags & 64)
    {   // with rays the air's light was found before the frame, at half its size: the mean of four taps round the pixel, for its grain
        float2 uv = LensUv(i), t = 1 / ViewSize;
        c += (HallTint.SampleLevel(Clamp, uv + t, 0).rgb + HallTint.SampleLevel(Clamp, uv - t, 0).rgb + HallTint.SampleLevel(Clamp, uv + float2(t.x, -t.y), 0).rgb + HallTint.SampleLevel(Clamp, uv - float2(t.x, -t.y), 0).rgb) * 0.25;
    }
    else c += Shafts(i, px) + Air(i, px);
    c += LensB.SampleLevel(Clamp, LensUv(i), 0).rgb * Post.x;
    return float4(Finished(c, uint2(px)), 1);
}

// ——— ambient occlusion ———
// Where a pixel's surface is, in the camera's own axes (right, up, forward), from the frame's depth.
float3 ViewPosition(int2 pixel)
{
    pixel = clamp(pixel, 0, int2(ViewSize) - 1);
    float d = SceneDepth.Load(int3(pixel, 0)), z = Ambience.z / max(d, 1e-6);
    float2 ndc = (pixel + 0.5) / ViewSize * float2(2, -2) + float2(-1, 1);
    return float3(ndc.x * TanHalfFovY * Aspect, ndc.y * TanHalfFovY, 1) * z;
}

// How much of the half-space over each pixel's surface is taken up by other surfaces within half a metre: corners, the foot of a
// column, the gap under a chair, the joints of the steps. Taps on a spiral round the pixel, turned differently in each pixel; each one
// that rises over the surface's own plane counts by how steeply, less the farther it is. The frame darkens its sky light by this.
float4 AoPS(SkyOut i) : SV_Target
{
    int2 px = int2(i.Position.xy);
    if (SceneDepth.Load(int3(px, 0)) <= 0) return 1;   // the sky
    float3 p = ViewPosition(px), r = ViewPosition(px + int2(1, 0)), l = ViewPosition(px - int2(1, 0)), u = ViewPosition(px - int2(0, 1)), d = ViewPosition(px + int2(0, 1));
    float3 dx = abs(r.z - p.z) < abs(p.z - l.z) ? r - p : p - l, dy = abs(d.z - p.z) < abs(p.z - u.z) ? d - p : p - u;
    float3 n = normalize(cross(dx, dy)); if (n.z > 0) n = -n;
    const float reach = 0.8;
    float span = clamp(reach / p.z * ViewSize.y * 0.5 / TanHalfFovY, 3, ViewSize.y * 0.12);   // the reach in pixels, here
    float a = Turn(i.Position.xy), sum = 0; int taps = (int)Ambience.y;
    [loop] for (int k = 0; k < taps; k++)
    {
        float t = k * 2.399963 + a, s = sqrt((k + 0.5) / taps) * span;
        float3 q = ViewPosition(px + int2(round(float2(cos(t), sin(t)) * s))) - p; float d2 = dot(q, q);
        sum += saturate(dot(n, q) * rsqrt(d2 + 1e-6) - 0.12) * saturate(1 - d2 / (reach * reach));
    }
    float open = saturate(1 - 2.7 * sum / taps); open *= open * (3 - 2 * open) * 0.5 + 0.5;   // corners, feet and joints are dark: the shade there is what gives the stone its weight
    return float4(open, open, open, 1);
}

#ifdef RAYS
// ——— the rays of the frame's own surfaces ———
// For every pixel of the frame's depth, before the frame: how much of the key light's disc its surface sees (KeySeen), and which lamps of its square's list it sees.
[numthreads(8, 8, 1)]
void RaysCS(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (SceneDepth.Load(int3(px, 0)) <= 0) { KeyOut[px] = 1; KeyColourOut[px] = 1; LampOut[px] = 0xffffffff; return; }
    // (the ray starts a finger off the surface toward the eye and the light: a normal worked out from the depth is not to be trusted
    // on a floor seen along its length, and a start under it leaves the floor in shade)
    float3 c = ViewPosition(px), world = Eye + CamRight * c.x + CamUp * c.y + CamForward * c.z, off = normalize(Eye - world) * (0.01 + 0.002 * c.z);
    KeyOut[px] = SunOn > 0 ? KeySeen(world + off + SunDir * 0.01, px + 0.5) : 1;
    KeyColourOut[px] = float4(KeyTint(world), 1);
    uint2 lamps = LampsAt(world); uint open = 0;
    [loop] for (uint j = 0; j < min(lamps.y, 32); j++)
    {
        uint k = LightGrid[lamps.x + j]; Light L = Lights[k]; float3 l; float d; float3 e = LightAt(L, world, l, d);
        if (max(e.r, max(e.g, e.b)) > 0.004 && L.Range >= 2 && Occluded(world + off + l * 0.01, l, max(0.01, d - 0.05))) continue;
        open |= 1u << j;
    }
    LampOut[px] = open;
}

// What the polished solids mirror: rays from the depth and the surfaces laid before the frame.
[numthreads(8, 8, 1)]
void MirrorCS(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy); float depth = SceneDepth.Load(int3(px, 0));
    float3 c = ViewPosition(px), world = Eye + CamRight * c.x + CamUp * c.y + CamForward * c.z;
    float2 ndc = (px + 0.5) / ViewSize * float2(2, -2) + float2(-1, 1); float3 dir = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect + CamUp * ndc.y * TanHalfFovY);
    float4 g = Solids.Load(int3(px, 0));
    if (depth <= 0 || g.a >= 0.3) { MirrorOut[px] = 0; return; }
    float3 n = normalize(g.xyz * 2 - 1);
    MirrorOut[px] = float4(Reflected(world, n, reflect(dir, n)), 0.5);
}

// For a picture of the surroundings, from its depth laid first: the key light's share and colour at each of its pixels.
[numthreads(8, 8, 1)]
void FaceCS(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy); float d = FaceDepth.Load(int3(px, 0));
    if (d <= 0 || SunOn <= 0) { FaceKeyOut[px] = 1; FaceTintOut[px] = 1; return; }
    float z = Ambience.z / d; float2 ndc = (px + 0.5) / ViewSize * float2(2, -2) + float2(-1, 1);
    float3 p = Eye + (CamRight * ndc.x + CamUp * ndc.y + CamForward) * z, off = normalize(Eye - p) * (0.01 + 0.002 * z) + SunDir * 0.01;
    FaceKeyOut[px] = Occluded(p + off, SunDir, 300) ? 0 : 1;
    FaceTintOut[px] = float4(!InHall(p) || p.z < -3.09 ? float3(1, 1, 1) : Through(p, SunDir, 60), 1);
}

// The light standing in the air, as Shafts and Air find it with maps: at each step along the eye's ray a ray to the key light, and through a stained pane its colour.
[numthreads(8, 8, 1)]
void AirCS(uint3 id : SV_DispatchThreadID)
{
    int2 half_ = int2(id.xy), px = half_ * 2; float3 c = 0;
    if (SunOn > 0)
    {
        float2 ndc = (px + 1) / ViewSize * float2(2, -2) + float2(-1, 1); float3 dir = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect + CamUp * ndc.y * TanHalfFovY);
        float reach = ViewDistance(px) / dot(dir, CamForward), jitter = frac(Turn(px + 0.5) * 0.159155);
        float3 low = Round1Low.xyz + float3(0, 0, -0.45), high = Round1High.xyz, inv = 1 / float3(abs(dir.x) > 1e-5 ? dir.x : 1e-5, abs(dir.y) > 1e-5 ? dir.y : 1e-5, abs(dir.z) > 1e-5 ? dir.z : 1e-5);
        float3 a = (low - Eye) * inv, b = (high - Eye) * inv, t0 = min(a, b), t1 = max(a, b);
        float from = max(max(t0.x, t0.y), max(t0.z, 0)), to = min(min(t1.x, t1.y), min(t1.z, reach));
        if (to > from)
        {
            const int steps = 24; float step = (to - from) / steps; float3 sum = 0;
            [loop] for (int k = 0; k < steps; k++)
            {
                float3 p = Eye + dir * (from + step * (jitter + k));
                if (!Occluded(p, SunDir, 60)) sum += p.z > -3.09 ? Through(p, SunDir, 60) : 1;
            }
            float phase = 0.6 + 1.6 * pow(saturate(dot(dir, SunDir) * 0.5 + 0.5), 4);
            c += sum * step * SunColor * 0.0075 * phase * saturate(SunDir.y * 3.2);
        }
        const int outdoor = 24; float far = min(reach, 70), step2 = far / outdoor, lit = 0;
        [loop] for (int k = 0; k < outdoor; k++)
        {
            float3 p = Eye + dir * (step2 * (jitter + k));
            if (!InHall(p) && !Occluded(p, SunDir, 300)) lit += exp(-max(p.y, 0) * 0.3);
        }
        const float g = 0.55; float phase2 = (1 - g * g) / pow(1 + g * g - 2 * g * dot(dir, SunDir), 1.5);
        float3 pane = Through(Eye, dir, far);   // (what stands in the air beyond a stained pane reaches the eye through it, in its colour and at half its strength, not laid over it)
        c += lit * step2 * SunColor * 0.0006 * phase2 * saturate(SunDir.y * 5) * (all(pane >= 1) ? float3(1, 1, 1) : pane * 0.5);
    }
    AirOut[half_] = float4(c, 1);
}

[numthreads(8, 8, 1)]
void LayerClearCS(uint3 id : SV_DispatchThreadID) { LayerCountOut[id.xy] = 0; }

// For each surface of glass and water LayerPS listed: its key light, its lamps and what it mirrors, as TransparentPS would ask for them.
[numthreads(8, 8, 1)]
void LayersCS(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy); uint count = LayerCount.Load(int3(px, 0));
    float2 ndc = (px + 0.5) / ViewSize * float2(2, -2) + float2(-1, 1); float3 dir = normalize(CamForward + CamRight * ndc.x * TanHalfFovY * Aspect + CamUp * ndc.y * TanHalfFovY);
    if (count > MaxLayers)
    {   // more surfaces than the list holds (a tea glass seen through: its walls, the tea): only solid glass's front one is seen, and a ray finds it
        Hit h; float far = 0;
        if (Trace(Eye, dir, 300, MaskVisible, h) && Materials[h.Material].Kind == KGlass && Materials[h.Material].Pattern.w > 0)
        {
            float3 n = dot(h.N, dir) > 0 ? -h.N : h.N; far = dot(h.P - Eye, CamForward);
            LayerKey = SunOn > 0 && !Occluded(h.P + (dot(n, SunDir) >= 0 ? n : -n) * 0.012, SunDir, 300) ? 1 : 0; Layer = 0;
            float fr = 0.04 + 0.96 * pow(1 - saturate(dot(n, -dir)), 5);
            LayerSeenOut[int3(px, 0)] = float4((Reflected(h.P, n, reflect(dir, n)) + Glint(reflect(dir, n), 300, h.P, n)) * fr + SeenThrough(h.P, n, -dir) * (1 - fr), LayerKey + 1);
        }
        LayerLampsOut[int3(px, 0)] = asuint(far);
        return;
    }
    [loop] for (uint k = 0; k < count; k++)
    {
        float4 d = LayerData.Load(int4(px, k, 0)); uint index = (uint)d.w; Material m = Materials[index];
        float3 p = Eye + dir * (d.x / dot(dir, CamForward)), v = -dir, n = float3(d.y, d.z, sqrt(saturate(1 - d.y * d.y - d.z * d.z)) * (frac(d.w) > 0.25 ? -1 : 1));
        float3 off = (dot(n, SunDir) >= 0 ? n : -n) * 0.012;
        LayerKey = SunOn > 0 && !Occluded(p + off, SunDir, 300) ? 1 : 0; Layer = 0;   // (from here on Glint reads it)
        uint open = 0xffffffff;
        if (m.Kind == KGlass && m.Pattern.w <= 0)
        {   // (only a pane takes lamps' light on itself)
            uint2 lamps = LampsAt(p);
            [loop] for (uint j = 0; j < min(lamps.y, 32); j++)
            {
                uint li = LightGrid[lamps.x + j]; Light L = Lights[li]; float3 l; float dist; float3 e = LightAt(L, p, l, dist);
                if (any(e > 0) && L.Range >= 2 && LampRay(L, p, n) < 0.5) open &= ~(1u << j);
            }
        }
        LayerLampsOut[int3(px, k)] = open;
        float3 c = 0; bool hidden = false;
        if (m.Kind == KWater)
        {
            bool level = abs(n.y) > 0.7; float3 w = level ? WaterNormal(p, Time) : Running(n, p), r = reflect(-v, w);
            if (level) { if (r.y < 0.03) r = normalize(float3(r.x, 0.03, r.z)); c = Reflected(p, float3(0, 1, 0), r); }
            else c = Reflected(p, w, r);
        }
        else if (m.Pattern.w > 0)
        {
            Hit ahead; hidden = Trace(Eye, dir, length(p - Eye) - 0.01, MaskVisible, ahead) && Materials[ahead.Material].Kind == KGlass;
            if (!hidden)
            {
                float fr = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                c = (Reflected(p, n, reflect(-v, n)) + Glint(reflect(-v, n), 300, p, n)) * fr + SeenThrough(p, n, v) * (1 - fr);
            }
        }
        else c = Reflected(p, n, reflect(-v, n));
        LayerSeenOut[int3(px, k)] = float4(c, (LayerKey + 1) * (hidden ? -1 : 1));
    }
}
#endif

// That picture smoothed along one axis (InstanceBase: 0 across, 1 down), seven pixels wide, each neighbour counted as far as it lies
// at the pixel's own distance: the last of the rays' grain goes, a shadow does not run over the edge of what it lies on.
float4 ShadeBlurPS(SkyOut i) : SV_Target
{
    int2 px = int2(i.Position.xy), along = InstanceBase ? int2(0, 1) : int2(1, 0);
    float z0 = ViewDistance(px), sum = LensA.Load(int3(px, 0)).r, n = 1;
    [unroll] for (int k = -3; k <= 3; k++)
    {
        if (k == 0) continue;
        int2 q = clamp(px + along * k, 0, int2(ViewSize) - 1);
        float w = exp(-k * k / 4.5) * saturate(1 - abs(ViewDistance(q) - z0) / (0.02 * z0 + 0.01));
        sum += LensA.Load(int3(q, 0)).r * w; n += w;
    }
    return float4(sum / n, 0, 0, 1);
}
