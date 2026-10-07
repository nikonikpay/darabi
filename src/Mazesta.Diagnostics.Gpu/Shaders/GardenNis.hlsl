// NVIDIA Image Scaling (NIS_Scaler.h, MIT licence, in ./NIS) as the last step of a garden frame: the lens's picture (display-referred 8-bit colour) is scaled to the window's
// size with NVScaler (directional scaling and adaptive sharpening), or - at the picture's own size - only sharpened with NVSharpen.
// Compiled twice by tools/compile-gpu-shaders.ps1: NIS_SCALER=1 (GardenNisScaler.cso) and NIS_SCALER=0 (GardenNisSharpen.cso).
#define NIS_HLSL 1
#define NIS_THREAD_GROUP_SIZE 256
#define NIS_USE_HALF_PRECISION 0

cbuffer Config : register(b0)
{
    float kDetectRatio; float kDetectThres; float kMinContrastRatio; float kRatioNorm;
    float kContrastBoost; float kEps; float kSharpStartY; float kSharpScaleY;
    float kSharpStrengthMin; float kSharpStrengthScale; float kSharpLimitMin; float kSharpLimitScale;
    float kScaleX; float kScaleY; float kDstNormX; float kDstNormY;
    float kSrcNormX; float kSrcNormY;
    uint kInputViewportOriginX; uint kInputViewportOriginY; uint kInputViewportWidth; uint kInputViewportHeight;
    uint kOutputViewportOriginX; uint kOutputViewportOriginY; uint kOutputViewportWidth; uint kOutputViewportHeight;
    float reserved0; float reserved1;
};

SamplerState samplerLinearClamp : register(s0);
Texture2D in_texture : register(t0);
Texture2D coef_scaler : register(t1);
Texture2D coef_usm : register(t2);
RWTexture2D<unorm float4> out_texture : register(u0);

#include "NIS/NIS_Scaler.h"

#define RS "CBV(b0), DescriptorTable(SRV(t0, numDescriptors=3), UAV(u0)), StaticSampler(s0, filter=FILTER_MIN_MAG_LINEAR_MIP_POINT, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP, addressW=TEXTURE_ADDRESS_CLAMP)"

[RootSignature(RS)]
[numthreads(NIS_THREAD_GROUP_SIZE, 1, 1)]
void main(uint3 blockIdx : SV_GroupID, uint3 threadIdx : SV_GroupThreadID)
{
#if NIS_SCALER
    NVScaler(blockIdx.xy, threadIdx.x);
#else
    NVSharpen(blockIdx.xy, threadIdx.x);
#endif
}
