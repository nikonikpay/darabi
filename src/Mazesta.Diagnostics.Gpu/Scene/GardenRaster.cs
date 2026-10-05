using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Draws the garden with Direct3D 12 rasterisation (GardenRaster.hlsl): a sun shadow map, the pool's mirror image and the frame, into any
/// of the targets it was given (the swap chain's back buffers) or its own off-screen one for the check frames. The load level sets how
/// much work a frame is: shadow map resolution and filter, whether the pool reflects the garden (and at what resolution), and MSAA.
/// </summary>
internal sealed unsafe class GardenRaster : GardenRenderer
{
    [StructLayout(LayoutKind.Sequential)] private struct DrawConstants { public uint InstanceBase, Material, Pad0, Pad1; public Vector3 Centre; public float Pad2; public Vector3 Extent; public float Pad3; }
    public readonly record struct Settings(int ShadowSize, int ShadowTaps, int Samples, int ReflectionDivisor)
    {
        /// <summary>Per load level 1..4: light, medium, heavy, extreme.</summary>
        public static Settings ForLoad(uint load) => load switch
        {
            1 => new(2048, 1, 1, 0), 2 => new(2048, 1, 2, 2), 3 => new(4096, 2, 4, 1), _ => new(4096, 3, 8, 1)
        };
    }

    /// <summary>One pass's constants (GardenFrame, 336 bytes) at a 256-byte-aligned slot.</summary>
    private const ulong Slot = 512;
    private readonly Settings _set; private readonly int _samples;
    private readonly ID3D12RootSignature _root;
    private readonly ID3D12PipelineState _shadow, _sky, _opaque, _cutout, _transparent, _skyR, _opaqueR, _cutoutR, _transparentR;
    private readonly ID3D12Resource _shadowMap, _shadowStatic, _depth, _reflection, _reflectionDepth, _msaa, _constants;
    private readonly ID3D12DescriptorHeap _srv, _rtv, _dsv; private readonly uint _rtvSize, _dsvSize;
    private readonly int _reflW, _reflH;
    private readonly Matrix4x4 _shadowViewProj;
    private bool _shadowBaked;

    public GardenRaster(D3D12Session s, GardenGpu g, int width, int height, ID3D12Resource[] targets, uint load) : base(s, g, width, height, targets)
    {
        _set = Settings.ForLoad(load);
        _samples = SupportedSamples(s, _set.Samples);
        byte[] vs = D3D12Session.Shader("GardenMainVS");
        _root = s.Own(s.Device.CreateRootSignature(vs));
        InputElementDescription[] layout = [new("POSITION", 0, Format.R16G16B16A16_SNorm, 0, 0), new("NORMAL", 0, Format.R8G8B8A8_SNorm, 8, 0), new("TEXCOORD", 0, Format.R16G16_Float, 12, 0)];
        byte[] shadowVs = D3D12Session.Shader("GardenShadowVS"), shadowPs = D3D12Session.Shader("GardenShadowPS"), skyVs = D3D12Session.Shader("GardenSkyVS"), skyPs = D3D12Session.Shader("GardenSkyPS");
        byte[] opaquePs = D3D12Session.Shader("GardenOpaquePS"), cutoutPs = D3D12Session.Shader("GardenCutoutPS"), transparentPs = D3D12Session.Shader("GardenTransparentPS");
        var blend = new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
        var a2c = BlendDescription.Opaque; a2c.AlphaToCoverageEnable = true;
        var readOnly = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual);
        var skyDepth = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual);
        ID3D12PipelineState Make(byte[] v, byte[]? p, BlendDescription b, DepthStencilDescription d, int samples, Format[] rt, bool input = true, RasterizerDescription? raster = null) =>
            s.Own(s.Device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = _root, VertexShader = v, PixelShader = p ?? [], BlendState = b, DepthStencilState = d, DepthStencilFormat = Format.D32_Float,
                RasterizerState = raster ?? RasterizerDescription.CullNone, SampleMask = uint.MaxValue, PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = rt, SampleDescription = new SampleDescription((uint)samples, 0), InputLayout = input ? new InputLayoutDescription(layout) : default
            }));
        var shadowRaster = new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthBias = 800, SlopeScaledDepthBias = 2.0f, DepthClipEnable = true };
        _shadow = Make(shadowVs, shadowPs, BlendDescription.Opaque, DepthStencilDescription.Default, 1, [], raster: shadowRaster);
        Format[] color = [Format.R8G8B8A8_UNorm];
        _sky = Make(skyVs, skyPs, BlendDescription.Opaque, skyDepth, _samples, color, input: false);
        _opaque = Make(vs, opaquePs, BlendDescription.Opaque, DepthStencilDescription.Default, _samples, color);
        _cutout = Make(vs, cutoutPs, _samples > 1 ? a2c : BlendDescription.Opaque, DepthStencilDescription.Default, _samples, color);
        _transparent = Make(vs, transparentPs, blend, readOnly, _samples, color);
        if (_set.ReflectionDivisor > 0)
        {
            _skyR = Make(skyVs, skyPs, BlendDescription.Opaque, skyDepth, 1, color, input: false);
            _opaqueR = Make(vs, opaquePs, BlendDescription.Opaque, DepthStencilDescription.Default, 1, color);
            _cutoutR = Make(vs, cutoutPs, BlendDescription.Opaque, DepthStencilDescription.Default, 1, color);
            _transparentR = Make(vs, transparentPs, blend, readOnly, 1, color);
        }
        else _skyR = _opaqueR = _cutoutR = _transparentR = _sky;

        // targets: the shadow map, the reflection (a 4x4 stand-in when the level has none), the MSAA frame and its depth
        _shadowMap = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, (uint)_set.ShadowSize, (uint)_set.ShadowSize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.PixelShaderResource, new ClearValue(Format.D32_Float, 1f, 0)));
        // The sun and everything but the logo stand still: their shadow depth is drawn once, and each frame starts from a copy of it.
        _shadowStatic = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, (uint)_set.ShadowSize, (uint)_set.ShadowSize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
        _reflW = _set.ReflectionDivisor > 0 ? Math.Max(4, width / _set.ReflectionDivisor) : 4; _reflH = _set.ReflectionDivisor > 0 ? Math.Max(4, height / _set.ReflectionDivisor) : 4;
        _reflection = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)_reflW, (uint)_reflH, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 1))));
        _reflectionDepth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)_reflW, (uint)_reflH, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
        _depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)width, (uint)height, 1, 1, sampleCount: (uint)_samples, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
        _msaa = _samples > 1
            ? s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, sampleCount: (uint)_samples, flags: ResourceFlags.AllowRenderTarget),
                ResourceStates.RenderTarget, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 1))))
            : _depth;   // unused at one sample
        _constants = s.Buffer(Slot * 2, HeapType.Upload, ResourceStates.GenericRead);

        _srv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 4, DescriptorHeapFlags.ShaderVisible, 0)));
        uint srvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        CpuDescriptorHandle Srv(int i) => _srv.GetCPUDescriptorHandleForHeapStart().Offset(i, srvSize);   // Offset moves the handle it is called on: start afresh each time
        s.Device.CreateShaderResourceView(g.Textures, g.TextureView, Srv(0));
        s.Device.CreateShaderResourceView(_shadowMap, new ShaderResourceViewDescription { Format = Format.R32_Float, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D, Shader4ComponentMapping = ShaderComponentMapping.Default, Texture2D = new Texture2DShaderResourceView { MipLevels = 1 } }, Srv(1));
        s.Device.CreateShaderResourceView(_reflection, null, Srv(2));
        s.Device.CreateShaderResourceView(g.Backdrop, null, Srv(3));

        _rtv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, (uint)Targets.Length + 2, DescriptorHeapFlags.None, 0)));
        _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        for (int i = 0; i < Targets.Length; i++) s.Device.CreateRenderTargetView(Targets[i], null, Rtv(i));
        s.Device.CreateRenderTargetView(_reflection, null, Rtv(Targets.Length));
        if (_samples > 1) s.Device.CreateRenderTargetView(_msaa, null, Rtv(Targets.Length + 1));
        _dsv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 4, DescriptorHeapFlags.None, 0)));
        _dsvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        s.Device.CreateDepthStencilView(_depth, null, Dsv(0));
        s.Device.CreateDepthStencilView(_shadowMap, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(1));
        s.Device.CreateDepthStencilView(_reflectionDepth, null, Dsv(2));
        s.Device.CreateDepthStencilView(_shadowStatic, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(3));

        _shadowViewProj = SunShadowMatrix(g.SunDirection);
    }

    public int Samples => _samples;

    /// <summary>For the render checks: the pool's mirror image of the last frame drawn, RGBA8 rows (its own size).</summary>
    internal (uint[] Pixels, int Width, int Height) ReflectionImage()
    {
        uint pitch = ((uint)_reflW * 4 + 255) & ~255u;
        var raw = S.Read((int)(pitch / 4 * _reflH), (l, readback) =>
        {
            l.ResourceBarrierTransition(_reflection, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)_reflW, (uint)_reflH, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(_reflection, 0));
            l.ResourceBarrierTransition(_reflection, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
        });
        var px = new uint[_reflW * _reflH];
        for (int y = 0; y < _reflH; y++) raw.AsSpan((int)(y * pitch / 4), _reflW).CopyTo(px.AsSpan(y * _reflW));
        return (px, _reflW, _reflH);
    }
    public Settings Level => _set;

    private CpuDescriptorHandle Rtv(int i) => _rtv.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize);
    private CpuDescriptorHandle Dsv(int i) => _dsv.GetCPUDescriptorHandleForHeapStart().Offset(i, _dsvSize);

    private static int SupportedSamples(D3D12Session s, int wanted)
    {
        for (int n = wanted; n > 1; n /= 2)
        {
            var q = new FeatureDataMultisampleQualityLevels { Format = Format.R8G8B8A8_UNorm, SampleCount = (uint)n };
            if (s.Device.CheckFeatureSupport(Vortice.Direct3D12.Feature.MultisampleQualityLevels, ref q) && q.NumQualityLevels > 0) return n;
        }
        return 1;
    }

    /// <summary>An orthographic view from the sun over the whole courtyard (walls, hall and windcatchers included).</summary>
    internal static Matrix4x4 SunShadowMatrix(Vector3 toSun)
    {
        if (toSun.LengthSquared() < 1e-6f) toSun = Vector3.UnitY;
        var centre = new Vector3(0, 4, -14); float radius = 27f;   // the courtyard: x ±14, z -34..7.5, up to the wind-catchers at 11
        var eye = centre + Vector3.Normalize(toSun) * 80;
        var up = MathF.Abs(toSun.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        return Matrix4x4.CreateLookAtLeftHanded(eye, centre, up) * Matrix4x4.CreateOrthographicLeftHanded(radius * 2, radius * 2, 1f, 160f);
    }

    protected override void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target)
    {
        var frame = GardenFrame.For(G, time, Width, Height);
        frame.ShadowViewProj = _shadowViewProj; frame.ShadowTexel = 1f / _set.ShadowSize; frame.ShadowTaps = (uint)_set.ShadowTaps;
        if (_set.ReflectionDivisor > 0) frame.Flags |= 1;
        if (_samples > 1) frame.Flags |= 4;
        var mirrored = frame.Mirrored(); mirrored.Flags &= ~4u; mirrored.ViewSize = new(_reflW, _reflH);
        var map = _constants.Map<byte>(0, (int)Slot * 2);
        MemoryMarshal.Write(map, in frame); MemoryMarshal.Write(map[(int)Slot..], in mirrored);
        _constants.Unmap(0);

        l.SetGraphicsRootSignature(_root); l.SetDescriptorHeaps(_srv);
        l.SetGraphicsRootShaderResourceView(2, G.InstanceBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(3, G.MaterialBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(4, G.LightBuffer.GPUVirtualAddress);
        l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        l.IASetVertexBuffers(0, new VertexBufferView(G.VertexBuffer.GPUVirtualAddress, (uint)G.VertexBuffer.Description.Width, 16));
        l.IASetIndexBuffer(new IndexBufferView(G.IndexBuffer.GPUVirtualAddress, (uint)G.IndexBuffer.Description.Width, Format.R32_UInt));
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);

        // 1. the sun's shadow map: the still scene's depth (drawn on the first frame only), then the moving logo over it
        static bool Casts(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout;
        l.RSSetViewport(0, 0, _set.ShadowSize, _set.ShadowSize); l.RSSetScissorRect(_set.ShadowSize, _set.ShadowSize);
        l.SetPipelineState(_shadow);
        if (!_shadowBaked)
        {
            l.ClearDepthStencilView(Dsv(3), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets([], Dsv(3));
            Geometry(l, Casts, draw => G.CastsShadow(draw) && !Moves(draw));
            l.ResourceBarrierTransition(_shadowStatic, ResourceStates.DepthWrite, ResourceStates.CopySource);
            _shadowBaked = true;
        }
        l.ResourceBarrierTransition(_shadowMap, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
        l.CopyResource(_shadowMap, _shadowStatic);
        l.ResourceBarrierTransition(_shadowMap, ResourceStates.CopyDest, ResourceStates.DepthWrite);
        l.OMSetRenderTargets([], Dsv(1));
        Geometry(l, Casts, draw => G.CastsShadow(draw) && Moves(draw));
        l.ResourceBarrierTransition(_shadowMap, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
        l.SetGraphicsRootDescriptorTable(5, _srv.GetGPUDescriptorHandleForHeapStart());

        // 2. the pool's mirror image
        if (_set.ReflectionDivisor > 0)
        {
            l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot);
            l.ResourceBarrierTransition(_reflection, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            l.ClearDepthStencilView(Dsv(2), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets(Rtv(Targets.Length), Dsv(2));
            l.RSSetViewport(0, 0, _reflW, _reflH); l.RSSetScissorRect(_reflW, _reflH);
            Pass(l, _skyR, _opaqueR, _cutoutR, _transparentR, skipWater: true);
            l.ResourceBarrierTransition(_reflection, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        }

        // 3. the frame
        var dest = Targets[target];
        var rtv = _samples > 1 ? Rtv(Targets.Length + 1) : Rtv(target);
        if (_samples == 1) l.ResourceBarrierTransition(dest, ResourceStates.Present, ResourceStates.RenderTarget);
        l.ClearDepthStencilView(Dsv(0), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets(rtv, Dsv(0));
        l.RSSetViewport(0, 0, Width, Height); l.RSSetScissorRect(Width, Height);
        Pass(l, _sky, _opaque, _cutout, _transparent, skipWater: false);
        if (_samples > 1)
        {
            l.ResourceBarrierTransition(_msaa, ResourceStates.RenderTarget, ResourceStates.ResolveSource);
            l.ResourceBarrierTransition(dest, ResourceStates.Present, ResourceStates.ResolveDest);
            l.ResolveSubresource(dest, 0, _msaa, 0, Format.R8G8B8A8_UNorm);
            l.ResourceBarrierTransition(dest, ResourceStates.ResolveDest, ResourceStates.Present);
            l.ResourceBarrierTransition(_msaa, ResourceStates.ResolveSource, ResourceStates.RenderTarget);
        }
        else l.ResourceBarrierTransition(dest, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    private void Pass(ID3D12GraphicsCommandList4 l, ID3D12PipelineState sky, ID3D12PipelineState opaque, ID3D12PipelineState cutout, ID3D12PipelineState transparent, bool skipWater)
    {
        l.SetPipelineState(sky); l.DrawInstanced(3, 1, 0, 0);
        l.SetPipelineState(opaque); Geometry(l, k => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Emissive);
        l.SetPipelineState(cutout); Geometry(l, k => k is GardenMaterialKind.Cutout);
        l.SetPipelineState(transparent); Geometry(l, k => k is GardenMaterialKind.Glass || (k is GardenMaterialKind.Water && !skipWater));
    }

    private bool Moves(GardenGpu.Draw d) => G.LogoInstance >= d.FirstInstance && G.LogoInstance < d.FirstInstance + d.InstanceCount;

    private void Geometry(ID3D12GraphicsCommandList4 l, Func<GardenMaterialKind, bool> kinds, Func<GardenGpu.Draw, bool>? which = null)
    {
        foreach (var d in G.Draws)
        {
            if (which is not null && !which(d)) continue;
            foreach (var p in d.Parts)
            {
                if (!kinds(p.Kind)) continue;
                l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = d.FirstInstance, Material = p.Material, Centre = d.Centre, Extent = d.Extent }, 0);
                l.DrawIndexedInstanced(p.IndexCount, d.InstanceCount, p.IndexStart, d.BaseVertex, 0);
            }
        }
    }
}

/// <summary>What both garden renderers share: the scene on the GPU, the size, the targets they draw into (in the present/common state
/// before and after), and an off-screen target of their own whose picture <see cref="Capture"/> reads back.</summary>
internal abstract class GardenRenderer : IDisposable
{
    protected readonly D3D12Session S; protected readonly GardenGpu G;
    public int Width { get; } public int Height { get; }
    /// <summary>The given targets, then the off-screen one.</summary>
    protected ID3D12Resource[] Targets { get; }
    public GardenGpu Scene => G;

    protected GardenRenderer(D3D12Session s, GardenGpu g, int width, int height, ID3D12Resource[] targets)
    {
        S = s; G = g; Width = width; Height = height;
        var own = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.Common, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 0, 0, 1))));
        Targets = [.. targets, own];
    }

    /// <summary>The renderer's own off-screen target, for drawing with nothing to show it on (the benchmark).</summary>
    public int OwnTarget => Targets.Length - 1;

    /// <summary>Draws the frame at <paramref name="time"/> into target <paramref name="target"/>.</summary>
    public void Draw(ID3D12GraphicsCommandList4 l, float time, int target) => DrawScene(l, time, target);
    protected abstract void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target);

    /// <summary>The frame at <paramref name="time"/>, drawn off screen and read back: RGBA8 pixels, rows of <see cref="Width"/>.</summary>
    public uint[] Capture(float time)
    {
        int own = Targets.Length - 1; uint pitch = ((uint)Width * 4 + 255) & ~255u;
        var raw = S.Read((int)(pitch / 4 * Height), (l, readback) =>
        {
            DrawScene(l, time, own);
            l.ResourceBarrierTransition(Targets[own], ResourceStates.Common, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(Targets[own], 0));
            l.ResourceBarrierTransition(Targets[own], ResourceStates.CopySource, ResourceStates.Common);
        });
        var pixels = new uint[Width * Height];
        for (int y = 0; y < Height; y++) raw.AsSpan((int)(y * pitch / 4), Width).CopyTo(pixels.AsSpan(y * Width));
        return pixels;
    }

    public virtual void Dispose() { }
}
