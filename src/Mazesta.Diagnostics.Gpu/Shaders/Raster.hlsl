// Direct3D 12 rasterisation benchmark (GpuRasterBenchmark). Compiled offline by tools/compile-gpu-shaders.ps1;
// the .cso files next to this one are what the app embeds.
//
// Scene pass: a grid of lit, indexed icospheres, one instance per sphere, placed from SV_InstanceID so no
// per-frame upload is needed. Vertices are pulled from a structured buffer (no input layout).
// Fill pass: full-screen triangles blended on top of each other, one layer per instance.

#define RS "RootConstants(num32BitConstants=4, b0), SRV(t0, visibility=SHADER_VISIBILITY_VERTEX)"

cbuffer Frame : register(b0) { float Time; uint Columns; float Aspect; uint Layers; };
StructuredBuffer<float3> Vertices : register(t0);

struct SceneVertex { float4 Position : SV_Position; float3 Normal : NORMAL; float3 World : WORLD; float Tint : TINT; };

static const float3 Eye = float3(0, 7, -9);
static const float3 Target = float3(0, -1, 14);

float4 Project(float3 world)
{
    float3 forward = normalize(Target - Eye), right = normalize(cross(float3(0, 1, 0), forward)), up = cross(forward, right);
    float3 d = world - Eye; float3 v = float3(dot(d, right), dot(d, up), dot(d, forward));
    const float f = 1.7320508, zn = 0.5, zf = 200;   // 60 degree vertical field of view
    return float4(v.x * f / Aspect, v.y * f, v.z * zf / (zf - zn) - zn * zf / (zf - zn), v.z);
}

[RootSignature(RS)]
SceneVertex SceneVS(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    float3 local = Vertices[vertex];
    float column = instance % Columns, row = instance / Columns;
    float3 center = float3((column - Columns * 0.5) * 1.1, 0.6 * sin(Time * 1.7 + instance * 0.37), row * 1.1 - 4);
    SceneVertex o; o.World = center + local * 0.45; o.Normal = local; o.Position = Project(o.World); o.Tint = frac(instance * 0.618034);
    return o;
}

float4 ScenePS(SceneVertex i) : SV_Target
{
    float3 n = normalize(i.Normal), view = normalize(Eye - i.World), color = 0.04;
    [unroll] for (int l = 0; l < 4; l++)
    {
        float3 light = float3(sin(Time + l * 1.57) * 20, 10 + l * 3, cos(Time + l * 1.57) * 20 + 20) - i.World;
        float falloff = 400 / (dot(light, light) + 1); light = normalize(light);
        float diffuse = saturate(dot(n, light)), specular = pow(saturate(dot(n, normalize(light + view))), 48);
        color += falloff * (diffuse * lerp(float3(0.9, 0.35, 0.2), float3(0.2, 0.5, 0.95), i.Tint) + specular);
    }
    return float4(color / (1 + color), 1);
}

[RootSignature(RS)]
float4 FillVS(uint vertex : SV_VertexID) : SV_Position
{
    float2 uv = float2((vertex << 1) & 2, vertex & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 FillPS(float4 position : SV_Position) : SV_Target
{
    return float4(frac(position.xy * 0.001 + Time), 0.5, 0.08);
}
