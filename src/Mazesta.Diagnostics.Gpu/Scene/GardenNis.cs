using System.Runtime.InteropServices;
using Mazesta.Diagnostics.Gpu.Benchmarks; using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>What the frame is finished with, as the options name it: nothing; NVIDIA Image Scaling's sharpener at the picture's own size; or its scaler, the garden
/// drawn at a share of the window's size and scaled up to it (the NIS SDK's own ratios: Quality 77 %, Balanced 67 %, Performance 59 %).</summary>
internal readonly record struct NisMode(string Name, float Scale)
{
    public static readonly NisMode Off = new("off", 1), Sharpen = new("sharpen", 1);
    public static readonly NisMode[] All = [Off, Sharpen, new("quality", 0.77f), new("balanced", 0.67f), new("performance", 0.59f)];
    public static NisMode Parse(string? name) => All.FirstOrDefault(m => m.Name == name, Sharpen);
    public bool Active => Name != "off";
    public bool Scales => Scale < 1;
    /// <summary>The size the garden is drawn at for a window of <paramref name="width"/> x <paramref name="height"/> (even, as the passes halve it).</summary>
    public (int Width, int Height) Render(int width, int height) => Scales ? (Math.Max(64, (int)MathF.Round(width * Scale / 2) * 2), Math.Max(36, (int)MathF.Round(height * Scale / 2) * 2)) : (width, height);
}

/// <summary>
/// NVIDIA Image Scaling (the SDK's NIS_Scaler.h, MIT licence, in Shaders/NIS; GardenNis.hlsl wraps it) as the last step of a garden frame: a compute pass that reads the lens's
/// picture and writes the window-sized one - NVScaler (a six-tap scaling filter with four directional filters and adaptive sharpening, smooth edges and crisp detail) when the
/// garden was drawn smaller than the window, NVSharpen (adaptive directional sharpening alone) when it was drawn at the window's size. The constants are the SDK's
/// (NVScalerUpdateConfig), the filter banks its coefficient tables (<see cref="NisCoefficients"/>).
/// </summary>
internal sealed unsafe class GardenNis : IDisposable
{
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline; private readonly ID3D12DescriptorHeap _heap;
    private readonly ID3D12Resource _config, _output, _input;
    private readonly int _outWidth, _outHeight; private readonly bool _scaler;
    private readonly uint _groupsX, _groupsY;

    /// <param name="input">The lens's picture (RGBA8, <paramref name="inWidth"/> x <paramref name="inHeight"/>), left in the shader-readable state between frames.</param>
    public GardenNis(D3D12Session s, ID3D12Resource input, int inWidth, int inHeight, int outWidth, int outHeight, float sharpness = 0.5f)
    {
        _input = input; _outWidth = outWidth; _outHeight = outHeight; _scaler = inWidth != outWidth || inHeight != outHeight;
        byte[] shader = D3D12Session.Shader(_scaler ? "GardenNisScaler" : "GardenNisSharpen");
        _root = s.Own(s.Device.CreateRootSignature(shader));
        _pipeline = s.Own(s.Device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = shader }));
        // the SDK's block of output pixels a thread group makes: 32 x 24 for the scaler, 32 x 32 for the sharpener
        _groupsX = (uint)((outWidth + 31) / 32); _groupsY = (uint)((outHeight + (_scaler ? 24 : 32) - 1) / (_scaler ? 24 : 32));
        _output = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)outWidth, (uint)outHeight, 1, 1, flags: ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess));
        _config = s.Buffer(256, HeapType.Upload, ResourceStates.GenericRead);
        WriteConfig(sharpness, inWidth, inHeight);
        var coefScale = Coefficients(s, NisCoefficients.Scale); var coefUsm = Coefficients(s, NisCoefficients.Usm);
        _heap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 4, DescriptorHeapFlags.ShaderVisible, 0)));
        uint size = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        CpuDescriptorHandle At(int i) => _heap.GetCPUDescriptorHandleForHeapStart().Offset(i, size);
        s.Device.CreateShaderResourceView(input, null, At(0)); s.Device.CreateShaderResourceView(coefScale, null, At(1)); s.Device.CreateShaderResourceView(coefUsm, null, At(2));
        s.Device.CreateUnorderedAccessView(_output, null, null, At(3));
    }

    /// <summary>The SDK's NVScalerUpdateConfig (and NVSharpenUpdateConfig, which is the same with the window the picture's own size), written once: 18 floats, 8 integers, 2 spare.</summary>
    private void WriteConfig(float sharpness, int inWidth, int inHeight)
    {
        sharpness = Math.Clamp(sharpness, 0, 1); float slider = sharpness - 0.5f;
        float maxScale = slider >= 0 ? 1.25f : 1.75f, minScale = slider >= 0 ? 1.25f : 1.0f, limitScale = slider >= 0 ? 1.25f : 1.0f;
        float detectRatio = 2 * 1127f / 1024f, detectThres = 64f / 1024f, minContrast = 2f, maxContrast = 10f, startY = 0.45f, endY = 0.9f;
        float strengthMin = MathF.Max(0, 0.4f + slider * minScale * 1.2f), strengthMax = 1.6f + slider * maxScale * 1.8f;
        float limitMin = MathF.Max(0.1f, 0.14f + slider * limitScale * 0.32f), limitMax = 0.5f + slider * limitScale * 0.6f;
        var map = _config.Map<byte>(0, 256); map.Clear();
        var f = MemoryMarshal.Cast<byte, float>(map); var u = MemoryMarshal.Cast<byte, uint>(map);
        f[0] = detectRatio; f[1] = detectThres; f[2] = minContrast; f[3] = 1 / (maxContrast - minContrast);
        f[4] = 1; f[5] = 1f / 255; f[6] = startY; f[7] = 1 / (endY - startY);
        f[8] = strengthMin; f[9] = strengthMax - strengthMin; f[10] = limitMin; f[11] = limitMax - limitMin;
        f[12] = inWidth / (float)_outWidth; f[13] = inHeight / (float)_outHeight; f[14] = 1f / _outWidth; f[15] = 1f / _outHeight; f[16] = 1f / inWidth; f[17] = 1f / inHeight;
        u[18] = 0; u[19] = 0; u[20] = (uint)inWidth; u[21] = (uint)inHeight; u[22] = 0; u[23] = 0; u[24] = (uint)_outWidth; u[25] = (uint)_outHeight;
        _config.Unmap(0);
    }

    /// <summary>A filter bank as the shader reads it: two float4 texels (the eight taps) for each of the 64 phases.</summary>
    private static ID3D12Resource Coefficients(D3D12Session s, float[,] taps)
    {
        var texture = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32G32B32A32_Float, 2, NisCoefficients.Phases, 1, 1), ResourceStates.CopyDest));
        using var staging = s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(256 * NisCoefficients.Phases), ResourceStates.GenericRead);
        var dst = staging.Map<byte>(0, 256 * NisCoefficients.Phases);
        for (int phase = 0; phase < NisCoefficients.Phases; phase++)
            for (int k = 0; k < NisCoefficients.Taps; k++) MemoryMarshal.Write(dst[(phase * 256 + k * 4)..], taps[phase, k]);
        staging.Unmap(0);
        s.Run(l =>
        {
            l.CopyTextureRegion(new TextureCopyLocation(texture, 0), 0, 0, 0, new TextureCopyLocation(staging, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R32G32B32A32_Float, 2, NisCoefficients.Phases, 1, 256) }));
            l.ResourceBarrierTransition(texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
        });
        return texture;
    }

    /// <summary>Scales and sharpens the lens's picture (readable, as it was left) and copies the result into <paramref name="target"/> (in the present state, and left in it).</summary>
    public void Record(ID3D12GraphicsCommandList4 l, ID3D12Resource target)
    {
        l.SetComputeRootSignature(_root); l.SetPipelineState(_pipeline); l.SetDescriptorHeaps(_heap);
        l.SetComputeRootConstantBufferView(0, _config.GPUVirtualAddress); l.SetComputeRootDescriptorTable(1, _heap.GetGPUDescriptorHandleForHeapStart());
        const ResourceStates Read = ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource;   // (a compute shader reads what a pixel shader reads, in the state of both)
        l.ResourceBarrierTransition(_input, ResourceStates.PixelShaderResource, Read);
        l.Dispatch(_groupsX, _groupsY, 1);
        l.ResourceBarrierTransition(_input, Read, ResourceStates.PixelShaderResource);
        l.ResourceBarrierTransition(_output, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        l.ResourceBarrierTransition(target, ResourceStates.Common, ResourceStates.CopyDest);
        l.CopyResource(target, _output);
        l.ResourceBarrierTransition(target, ResourceStates.CopyDest, ResourceStates.Common);
        l.ResourceBarrierTransition(_output, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
    }

    public void Dispose() { }
}
