// The visual GPU tests' on-screen readout (SceneOverlay): a block of text the CPU draws with GDI into a small bitmap, laid over the frame
// in its own viewport over a translucent panel. The bitmap is BGRA, text drawn on black: its brightness is its coverage.
// Compiled offline by tools/compile-gpu-shaders.ps1.

#define RS "RootConstants(num32BitConstants=4, b0), SRV(t0)"

cbuffer Box : register(b0) { uint Left; uint Top; uint BoxWidth; uint BoxHeight; };
StructuredBuffer<uint> Text : register(t0);

[RootSignature(RS)]
float4 OverlayVS(uint vertex : SV_VertexID) : SV_Position
{
    float2 uv = float2((vertex << 1) & 2, vertex & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 OverlayPS(float4 position : SV_Position) : SV_Target
{
    uint2 p = min(uint2(position.xy) - uint2(Left, Top), uint2(BoxWidth - 1, BoxHeight - 1));
    uint bgra = Text[p.y * BoxWidth + p.x];
    float3 c = float3((bgra >> 16) & 255, (bgra >> 8) & 255, bgra & 255) / 255;
    float coverage = max(c.r, max(c.g, c.b)), alpha = 0.62 + 0.38 * coverage;
    float3 text = c / max(coverage, 1.0 / 255), panel = float3(0.02, 0.02, 0.05);
    return float4((coverage * text + 0.62 * (1 - coverage) * panel) / alpha, alpha);
}
