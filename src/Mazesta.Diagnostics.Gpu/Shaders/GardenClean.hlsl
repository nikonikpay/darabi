// Edge-adaptive smoothing of the frame's stair-steps and grain, run on the lens's 8-bit picture just before NVIDIA's sharpener (GardenNis): a sharpener
// raises every jagged edge and speck as much as it raises detail, so the edges are made smooth first (the idea of FXAA: find where the luma jumps, then
// blend along the edge and across it by how much the pixel differs from its neighbours); flat areas and strong, clean detail are left as they are.
Texture2D<float4> src : register(t0);
RWTexture2D<unorm float4> dst : register(u0);

#define RS "DescriptorTable(SRV(t0), UAV(u0))"

float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

[RootSignature(RS)]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint w, h; src.GetDimensions(w, h);
    if (id.x >= w || id.y >= h) return;
    int2 p = int2(id.xy), hi = int2(w - 1, h - 1);
    float3 c = src.Load(int3(p, 0)).rgb;
    float3 n = src.Load(int3(clamp(p + int2(0, -1), 0, hi), 0)).rgb, s = src.Load(int3(clamp(p + int2(0, 1), 0, hi), 0)).rgb;
    float3 e = src.Load(int3(clamp(p + int2(1, 0), 0, hi), 0)).rgb, wv = src.Load(int3(clamp(p + int2(-1, 0), 0, hi), 0)).rgb;
    float lc = Luma(c), ln = Luma(n), ls = Luma(s), le = Luma(e), lw = Luma(wv);
    float hiL = max(lc, max(max(ln, ls), max(le, lw))), loL = min(lc, min(min(ln, ls), min(le, lw)));
    float range = hiL - loL;
    if (range < max(0.0312, hiL * 0.125)) { dst[p] = float4(c, 1); return; }   // flat: nothing to smooth

    float lnw = Luma(src.Load(int3(clamp(p + int2(-1, -1), 0, hi), 0)).rgb), lne = Luma(src.Load(int3(clamp(p + int2(1, -1), 0, hi), 0)).rgb);
    float lsw = Luma(src.Load(int3(clamp(p + int2(-1, 1), 0, hi), 0)).rgb), lse = Luma(src.Load(int3(clamp(p + int2(1, 1), 0, hi), 0)).rgb);
    // how far the pixel is from the average of what is round it: an isolated speck or a stair-step corner is far, the middle of a clean edge is not
    float avg = (2 * (ln + ls + le + lw) + lnw + lne + lsw + lse) / 12;
    float sub = smoothstep(0, 1, saturate(abs(avg - lc) / range)); sub = sub * sub * 0.75;
    float edgeH = abs(-2 * lw + lnw + lsw) + 2 * abs(-2 * lc + ln + ls) + abs(-2 * le + lne + lse);
    float edgeV = abs(-2 * ln + lnw + lne) + 2 * abs(-2 * lc + lw + le) + abs(-2 * ls + lsw + lse);
    bool horizontal = edgeH >= edgeV;
    // along the edge the stair-steps are averaged out; across it the pixel moves toward the side it differs from more
    float3 along = horizontal ? 0.25 * wv + 0.5 * c + 0.25 * e : 0.25 * n + 0.5 * c + 0.25 * s;
    float3 across = horizontal ? (abs(ln - lc) >= abs(ls - lc) ? n : s) : (abs(lw - lc) >= abs(le - lc) ? wv : e);
    float3 smooth = 0.5 * along + 0.25 * across + 0.25 * c;
    dst[p] = float4(lerp(c, smooth, saturate(sub + 0.25)), 1);
}
