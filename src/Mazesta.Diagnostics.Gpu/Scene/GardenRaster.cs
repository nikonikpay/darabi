using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// Draws the garden with Direct3D 12 rasterisation (GardenRaster.hlsl), through its day (<see cref="GardenDay"/>): the key light's
/// shadow maps - the sun's or the moon's, of the whole courtyard and a finer one of the hall alone, with the colour the hall's
/// stained panes give the light passing them - drawn every frame, for the light moves; a map of where the sky stands open
/// (the scene seen from straight above, drawn once), a shadow cube for each of the lamps (drawn once), the
/// garden all round from the middle of the courtyard and of the hall (what polished surfaces, glass and metal mirror: one face of
/// those pictures is drawn again each frame, so they follow the light), the
/// pool's mirror image, the light bounced round the courtyard (<see cref="GardenLightVolume"/>, worked out beforehand and put together for the moment), the frame's depth and the ambient occlusion worked out
/// from it, the frame in light's own units, and the lens (the glow round what is bright, the blur of what is out of focus, the tone
/// curve) into any of the targets it was given (the swap chain's back buffers) or its own off-screen one for the check frames.
/// The load level sets how much work a frame is: the shadows' filter, the occlusion's taps, whether the pool reflects the garden
/// (and at what resolution), the lens's taps, and MSAA.
/// </summary>
internal sealed unsafe class GardenRaster : GardenRenderer
{
    [StructLayout(LayoutKind.Sequential)] private struct DrawConstants { public uint InstanceBase, Material, Pad0, Pad1; public Vector3 Centre; public float Pad2; public Vector3 Extent; public float Pad3; }
    public readonly record struct Settings(int ShadowSize, int ShadowTaps, int Samples, int ReflectionDivisor, int OcclusionTaps, int LensTaps)
    {
        /// <summary>Per load level 1..4: light, medium, heavy, extreme. The shadow map is as fine at every level: the sun is low, its
        /// shadows long, and all but the moving things' share of it is drawn once.</summary>
        public static Settings ForLoad(uint load) => load switch
        {
            1 => new(4096, 1, 1, 0, 8, 8), 2 => new(4096, 1, 2, 2, 10, 12), 3 => new(4096, 2, 4, 1, 14, 16), _ => new(4096, 3, 8, 1, 20, 28)
        };
    }

    /// <summary>One pass's constants (GardenFrame) at a 256-byte-aligned slot: the frame, its mirror image, its depth alone, (once)
    /// the view from above, the still scene's passes, and the key light's view of the hall; then the lamps' and the surroundings' views.</summary>
    private const ulong Slot = GardenFrame.Bytes; private const int Slots = 6, SkySize = 2048, GlowLevels = 5, LampSize = 512, RoundSize = 256, RoundMips = 5, TintSize = 2048;
    /// <summary>Where the still garden is drawn all round from, once, for what surfaces mirror, and the box each picture stands for
    /// (a ray is carried to the box's side before the picture is looked up): the courtyard, from over the pool between the fountain and
    /// the gate; the hall's rooms, from their middle.</summary>
    internal static readonly (Vector3 From, Vector3 Low, Vector3 High)[] Rounds =
    [
        (new(0, 2.4f, -23f), new(-13.8f, 0, -33.3f), new(13.8f, 9, -3.1f)),
        (new(0, 3.2f, 2f), new(-9.9f, 1.1f, -2.6f), new(9.9f, 6.3f, 6.85f)),
    ];
    /// <summary>The frame's light before the lens: ten bits a colour (GardenRaster.hlsl's Pack). The glow: floats.</summary>
    private const Format Light = Format.R10G10B10A2_UNorm, GlowFormat = Format.R11G11B10_Float;
    private readonly Settings _set; private readonly int _samples;
    private readonly ID3D12RootSignature _root;
    private readonly ID3D12PipelineState _shadow, _sky, _opaque, _cutout, _transparent, _skyR, _opaqueR, _cutoutR, _transparentR, _depthOnly, _occlude, _glowFirst, _glowDown, _glowUp, _lens, _lampShadow, _roundDown, _tint, _before;
    private readonly ID3D12Resource _shadowMap, _hallShadow, _hallTint, _skyMap, _depth, _reflection, _reflectionDepth, _msaa, _constants, _sceneDepth, _occlusion, _light, _lamps, _volume, _round, _roundDepth;
    private readonly ID3D12Resource? _volumeStaging; private readonly float[] _mixed = []; private readonly uint _volumePitch;
    /// <summary>The stained panes' draws, and for each face of the pictures of the surroundings the still instances it can see, in runs of neighbours.</summary>
    private readonly (GardenGpu.Draw Draw, GardenGpu.Part Part)[] _stained; private readonly (GardenGpu.Draw Draw, uint First, uint Count)[][] _roundRuns;
    private int _roundNext; private bool _roundsDrawn;
    /// <summary>What casts shadows: each such draw with its instances' bounds as they were placed (a ball's centre and radius).</summary>
    private readonly (GardenGpu.Draw Draw, (Vector3 Centre, float Radius)[] Bounds)[] _casters;
    private readonly ID3D12Resource[] _glow = new ID3D12Resource[GlowLevels]; private readonly uint _srvSize;
    private readonly ID3D12DescriptorHeap _srv, _rtv, _dsv; private readonly uint _rtvSize, _dsvSize;
    private readonly int _reflW, _reflH;
    private readonly Matrix4x4 _skyViewProj;
    private bool _onceDrawn;
    /// <summary>How many frames have been shown one after another, and the key light's views its shadow maps were last drawn through.</summary>
    private int _shown; private Matrix4x4 _keyView, _hallView;
    /// <summary>The pool's water on the plan (x and z, from and to): its mirror image is drawn only where the frame shows it.</summary>
    private readonly Vector4 _pool;

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
        // the frame's depth is reversed (GardenFrame.Near): nearer is greater, the sky lies at 0
        var nearer = new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.GreaterEqual);
        var readOnly = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.GreaterEqual);
        var skyDepth = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.GreaterEqual);
        ID3D12PipelineState Make(byte[] v, byte[]? p, BlendDescription b, DepthStencilDescription d, int samples, Format[] rt, bool input = true, RasterizerDescription? raster = null, Format depth = Format.D32_Float) =>
            s.Own(s.Device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                RootSignature = _root, VertexShader = v, PixelShader = p ?? [], BlendState = b, DepthStencilState = d, DepthStencilFormat = depth,
                RasterizerState = raster ?? RasterizerDescription.CullNone, SampleMask = uint.MaxValue, PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetFormats = rt, SampleDescription = new SampleDescription((uint)samples, 0), InputLayout = input ? new InputLayoutDescription(layout) : default
            }));
        var shadowRaster = new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthBias = 800, SlopeScaledDepthBias = 2.0f, DepthClipEnable = true };
        _shadow = Make(shadowVs, shadowPs, BlendDescription.Opaque, DepthStencilDescription.Default, 1, [], raster: shadowRaster);
        var lampRaster = new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthBias = 2, SlopeScaledDepthBias = 1.5f, DepthClipEnable = true };
        _lampShadow = Make(shadowVs, shadowPs, BlendDescription.Opaque, DepthStencilDescription.Default, 1, [], raster: lampRaster, depth: Format.D16_UNorm);
        // the stained panes seen from the key light: each multiplies what is behind it by its colour
        _tint = Make(shadowVs, D3D12Session.Shader("GardenTintPS"), new BlendDescription(Blend.Zero, Blend.SourceColor, Blend.Zero, Blend.One), DepthStencilDescription.None, 1, [Format.R8G8B8A8_UNorm]);
        Format[] color = [Light];
        _sky = Make(skyVs, skyPs, BlendDescription.Opaque, skyDepth, _samples, color, input: false);
        _opaque = Make(vs, opaquePs, BlendDescription.Opaque, nearer, _samples, color);
        _cutout = Make(vs, cutoutPs, _samples > 1 ? a2c : BlendDescription.Opaque, nearer, _samples, color);
        _transparent = Make(vs, transparentPs, blend, readOnly, _samples, color);
        if (_set.ReflectionDivisor > 0)
        {
            _skyR = Make(skyVs, skyPs, BlendDescription.Opaque, skyDepth, 1, color, input: false);
            _opaqueR = Make(vs, opaquePs, BlendDescription.Opaque, nearer, 1, color);
            _cutoutR = Make(vs, cutoutPs, BlendDescription.Opaque, nearer, 1, color);
            _transparentR = Make(vs, transparentPs, blend, readOnly, 1, color);
        }
        else _skyR = _opaqueR = _cutoutR = _transparentR = _sky;
        _depthOnly = Make(shadowVs, shadowPs, BlendDescription.Opaque, nearer, 1, []);
        _before = Make(vs, null, BlendDescription.Opaque, nearer, _samples, []);   // the frame's solid surfaces as depth alone, through the frame's own vertex shader: the same depth to the bit
        _occlude = Make(skyVs, D3D12Session.Shader("GardenAoPS"), BlendDescription.Opaque, DepthStencilDescription.None, 1, [Format.R8_UNorm], input: false);
        _glowFirst = Make(skyVs, D3D12Session.Shader("GardenGlowFirstPS"), BlendDescription.Opaque, DepthStencilDescription.None, 1, [GlowFormat], input: false);
        _glowDown = Make(skyVs, D3D12Session.Shader("GardenGlowDownPS"), BlendDescription.Opaque, DepthStencilDescription.None, 1, [GlowFormat], input: false);
        _glowUp = Make(skyVs, D3D12Session.Shader("GardenGlowUpPS"), new BlendDescription(Blend.One, Blend.One), DepthStencilDescription.None, 1, [GlowFormat], input: false);
        _lens = Make(skyVs, D3D12Session.Shader("GardenLensPS"), BlendDescription.Opaque, DepthStencilDescription.None, 1, [Format.R8G8B8A8_UNorm], input: false);
        _roundDown = Make(skyVs, D3D12Session.Shader("GardenRoundDownPS"), BlendDescription.Opaque, DepthStencilDescription.None, 1, color, input: false);
        if (_set.ReflectionDivisor == 0)
        {   // the pictures of the surroundings are drawn at one sample too
            _skyR = Make(skyVs, skyPs, BlendDescription.Opaque, skyDepth, 1, color, input: false);
            _opaqueR = Make(vs, opaquePs, BlendDescription.Opaque, nearer, 1, color);
            _cutoutR = Make(vs, cutoutPs, BlendDescription.Opaque, nearer, 1, color);
            _transparentR = Make(vs, transparentPs, blend, readOnly, 1, color);
        }

        // targets: the shadow map, the reflection (a 4x4 stand-in when the level has none), the MSAA frame and its depth
        _shadowMap = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, (uint)_set.ShadowSize, (uint)_set.ShadowSize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.PixelShaderResource, new ClearValue(Format.D32_Float, 1f, 0)));
        // The hall's own shadow map, as large over a third of the width, and the colour of the stained panes in the light's way.
        _hallShadow = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, (uint)_set.ShadowSize, (uint)_set.ShadowSize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.PixelShaderResource, new ClearValue(Format.D32_Float, 1f, 0)));
        _hallTint = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, TintSize, TintSize, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(1, 1, 1, 1))));
        // What stands over each point of the courtyard: the still scene's depth from straight above, drawn on the first frame.
        _skyMap = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, SkySize, SkySize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1f, 0)));
        // The frame's depth at one sample a pixel, and the ambient occlusion worked out from it.
        _sceneDepth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R32_Typeless, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.PixelShaderResource, new ClearValue(Format.D32_Float, 0f, 0)));
        _occlusion = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Format.R8_UNorm, new Color4(1, 1, 1, 1))));
        _reflW = _set.ReflectionDivisor > 0 ? Math.Max(4, width / _set.ReflectionDivisor) : 4; _reflH = _set.ReflectionDivisor > 0 ? Math.Max(4, height / _set.ReflectionDivisor) : 4;
        _reflection = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Light, (uint)_reflW, (uint)_reflH, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Light, new Color4(0, 0, 0, 1))));
        _reflectionDepth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)_reflW, (uint)_reflH, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 0f, 0)));
        _depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)width, (uint)height, 1, 1, sampleCount: (uint)_samples, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 0f, 0)));
        _msaa = _samples > 1
            ? s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Light, (uint)width, (uint)height, 1, 1, sampleCount: (uint)_samples, flags: ResourceFlags.AllowRenderTarget),
                ResourceStates.RenderTarget, new ClearValue(Light, new Color4(0, 0, 0, 1))))
            : _depth;   // unused at one sample
        // The frame's light at one sample a pixel (what MSAA resolves into), and its glow: half the frame's size, and four halvings of that.
        _light = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Light, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Light, new Color4(0, 0, 0, 1))));
        for (int k = 0; k < GlowLevels; k++)
            _glow[k] = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(GlowFormat, (uint)GlowSize(k).W, (uint)GlowSize(k).H, 1, 1, flags: ResourceFlags.AllowRenderTarget),
                ResourceStates.PixelShaderResource, new ClearValue(GlowFormat, new Color4(0, 0, 0, 1))));
        // What each shadowed lamp sees round it: a cube of depths apiece (six square views), the still scene, drawn on the first frame.
        int cubes = Math.Max(1, g.ShadowLamps);
        _lamps = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R16_Typeless, LampSize, LampSize, (ushort)(cubes * 6), 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D16_UNorm, 1f, 0)));
        // The garden all round from each of Rounds: six faces a picture, each halved RoundMips - 1 times.
        _round = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Light, RoundSize, RoundSize, (ushort)(Rounds.Length * 6), RoundMips, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Light, new Color4(0, 0, 0, 1))));
        _roundDepth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, RoundSize, RoundSize, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 0f, 0)));
        _roundSlot = Slots + cubes * 6;
        _constants = s.Buffer(Slot * (ulong)(_roundSlot + Rounds.Length * 6), HeapType.Upload, ResourceStates.GenericRead);

        // The light bounced round the courtyard, as a texture of six blocks along x (one point of nothing when the scene has none):
        // put together for each frame's moment of the day (GardenLightVolume.Mix) and sent up through a buffer of its own.
        var bounced = g.LightVolume; int vx = (bounced?.X ?? 1) * 6, vy = bounced?.Y ?? 1, vz = bounced?.Z ?? 1;
        _volume = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture3D(Format.R32G32B32A32_Float, (uint)vx, (uint)vy, (ushort)vz, 1), ResourceStates.PixelShaderResource));
        _volumePitch = (uint)((vx * 16 + 255) & ~255);
        if (bounced is not null)
        {
            _mixed = new float[bounced.TexelFloats];
            _volumeStaging = s.Own(s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(_volumePitch * (ulong)(vy * vz)), ResourceStates.GenericRead));
        }

        // the shader's views: the scene's seven (t3..t9), then the lens's - the frame's light, the glow's levels, and the last level again
        // (a lens pass sees two views in a row: what it reads, and what follows it)
        _srv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, RoundView + RoundMips, DescriptorHeapFlags.ShaderVisible, 0)));
        uint srvSize = _srvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        CpuDescriptorHandle Srv(int i) => _srv.GetCPUDescriptorHandleForHeapStart().Offset(i, srvSize);   // Offset moves the handle it is called on: start afresh each time
        s.Device.CreateShaderResourceView(g.Textures, g.TextureView, Srv(0));
        s.Device.CreateShaderResourceView(_shadowMap, new ShaderResourceViewDescription { Format = Format.R32_Float, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D, Shader4ComponentMapping = ShaderComponentMapping.Default, Texture2D = new Texture2DShaderResourceView { MipLevels = 1 } }, Srv(1));
        s.Device.CreateShaderResourceView(_reflection, null, Srv(2));
        s.Device.CreateShaderResourceView(g.Backdrop, null, Srv(3));
        var depthView = new ShaderResourceViewDescription { Format = Format.R32_Float, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D, Shader4ComponentMapping = ShaderComponentMapping.Default, Texture2D = new Texture2DShaderResourceView { MipLevels = 1 } };
        s.Device.CreateShaderResourceView(_skyMap, depthView, Srv(4));
        s.Device.CreateShaderResourceView(_sceneDepth, depthView, Srv(5));
        s.Device.CreateShaderResourceView(_occlusion, null, Srv(6));
        s.Device.CreateShaderResourceView(_light, null, Srv(LensViews));
        for (int k = 0; k <= GlowLevels; k++) s.Device.CreateShaderResourceView(_glow[Math.Min(k, GlowLevels - 1)], null, Srv(LensViews + 1 + k));
        s.Device.CreateShaderResourceView(_lamps, new ShaderResourceViewDescription { Format = Format.R16_UNorm, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.TextureCubeArray, Shader4ComponentMapping = ShaderComponentMapping.Default,
            TextureCubeArray = new TextureCubeArrayShaderResourceView { MipLevels = 1, NumCubes = (uint)cubes } }, Srv(LampView));
        s.Device.CreateShaderResourceView(_volume, null, Srv(LampView + 1));
        s.Device.CreateShaderResourceView(_round, new ShaderResourceViewDescription { Format = Light, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.TextureCubeArray, Shader4ComponentMapping = ShaderComponentMapping.Default,
            TextureCubeArray = new TextureCubeArrayShaderResourceView { MipLevels = RoundMips, NumCubes = (uint)Rounds.Length } }, Srv(LampView + 2));
        for (int m = 0; m < RoundMips; m++)   // each mip's faces as plain pictures, for making the next mip (and one more view after the last: a lens pass sees two in a row)
            s.Device.CreateShaderResourceView(_round, new ShaderResourceViewDescription { Format = Light, ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DArray, Shader4ComponentMapping = ShaderComponentMapping.Default,
                Texture2DArray = new Texture2DArrayShaderResourceView { MostDetailedMip = (uint)Math.Min(m, RoundMips - 2), MipLevels = 1, ArraySize = (uint)(Rounds.Length * 6) } }, Srv(RoundView + m));
        s.Device.CreateShaderResourceView(_hallShadow, depthView, Srv(LampView + 3));
        s.Device.CreateShaderResourceView(_hallTint, null, Srv(LampView + 4));

        _rtv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, (uint)(Targets.Length + 5 + GlowLevels + Rounds.Length * 6 * RoundMips), DescriptorHeapFlags.None, 0)));
        _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        for (int i = 0; i < Targets.Length; i++) s.Device.CreateRenderTargetView(Targets[i], null, Rtv(i));
        s.Device.CreateRenderTargetView(_reflection, null, Rtv(Targets.Length));
        if (_samples > 1) s.Device.CreateRenderTargetView(_msaa, null, Rtv(Targets.Length + 1));
        s.Device.CreateRenderTargetView(_occlusion, null, Rtv(Targets.Length + 2));
        s.Device.CreateRenderTargetView(_light, null, Rtv(Targets.Length + 3));
        for (int k = 0; k < GlowLevels; k++) s.Device.CreateRenderTargetView(_glow[k], null, Rtv(Targets.Length + 4 + k));
        for (int face = 0; face < Rounds.Length * 6; face++)
            for (int m = 0; m < RoundMips; m++)
                s.Device.CreateRenderTargetView(_round, new RenderTargetViewDescription { Format = Light, ViewDimension = RenderTargetViewDimension.Texture2DArray, Texture2DArray = new Texture2DArrayRenderTargetView { MipSlice = (uint)m, FirstArraySlice = (uint)face, ArraySize = 1 } }, RoundRtv(face, m));
        s.Device.CreateRenderTargetView(_hallTint, null, TintRtv);
        _dsv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, (uint)(7 + cubes * 6), DescriptorHeapFlags.None, 0)));
        _dsvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        s.Device.CreateDepthStencilView(_depth, null, Dsv(0));
        s.Device.CreateDepthStencilView(_shadowMap, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(1));
        s.Device.CreateDepthStencilView(_reflectionDepth, null, Dsv(2));
        s.Device.CreateDepthStencilView(_hallShadow, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(3));
        s.Device.CreateDepthStencilView(_skyMap, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(4));
        s.Device.CreateDepthStencilView(_sceneDepth, new DepthStencilViewDescription { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, Dsv(5));
        s.Device.CreateDepthStencilView(_roundDepth, null, Dsv(6 + cubes * 6));
        for (int k = 0; k < cubes * 6; k++)
            s.Device.CreateDepthStencilView(_lamps, new DepthStencilViewDescription { Format = Format.D16_UNorm, ViewDimension = DepthStencilViewDimension.Texture2DArray, Texture2DArray = new Texture2DArrayDepthStencilView { FirstArraySlice = (uint)k, ArraySize = 1 } }, Dsv(6 + k));

        _skyViewProj = SkyMatrix();
        var water = g.Draws.Where(d => !d.Moving && d.Parts.Any(p => p.Kind == GardenMaterialKind.Water)).SelectMany(d => Enumerable.Range((int)d.FirstInstance, (int)d.InstanceCount).Select(i => g.Bounds(d, i))).ToArray();   // (every still water's ball on the plan: the pool's, and what runs into it)
        _pool = water.Length == 0 ? default : new(water.Min(b => b.Centre.X - b.Radius), water.Min(b => b.Centre.Z - b.Radius), water.Max(b => b.Centre.X + b.Radius), water.Max(b => b.Centre.Z + b.Radius));
        _casters = [.. g.Draws.Where(g.CastsShadow).Select(d => (d, Enumerable.Range((int)d.FirstInstance, (int)d.InstanceCount).Select(i => g.Bounds(d, i)).ToArray()))];
        _stained = [.. g.Draws.SelectMany(d => d.Parts.Where(p => g.Materials[p.Material].LightTint > 0).Select(p => (d, p)))];
        // what each face of the pictures of the surroundings can see of the still scene: an instance inside its pyramid, or touching it
        _roundRuns = new (GardenGpu.Draw, uint, uint)[Rounds.Length * 6][];
        for (int k = 0; k < _roundRuns.Length; k++)
        {
            var from = Rounds[k / 6].From; int axis = k % 6 / 2; float sign = k % 2 == 0 ? 1 : -1; var runs = new List<(GardenGpu.Draw, uint, uint)>();
            bool Seen((Vector3 Centre, float Radius) b)
            {
                var c = b.Centre - from; float along = (axis == 0 ? c.X : axis == 1 ? c.Y : c.Z) * sign + b.Radius * 1.4143f;
                return axis switch { 0 => along >= MathF.Abs(c.Y) && along >= MathF.Abs(c.Z), 1 => along >= MathF.Abs(c.X) && along >= MathF.Abs(c.Z), _ => along >= MathF.Abs(c.X) && along >= MathF.Abs(c.Y) };
            }
            foreach (var d in g.Draws.Where(d => !d.Moving))
                for (uint i = 0; i < d.InstanceCount;)
                {
                    if (!Seen(g.Bounds(d, (int)(d.FirstInstance + i)))) { i++; continue; }
                    uint n = 1; while (i + n < d.InstanceCount && Seen(g.Bounds(d, (int)(d.FirstInstance + i + n)))) n++;
                    runs.Add((d, d.FirstInstance + i, n)); i += n;
                }
            _roundRuns[k] = [.. runs];
        }
    }

    public int Samples => _samples;
    private const int LensViews = 7, LampView = LensViews + GlowLevels + 2, RoundView = LampView + 5;
    /// <summary>The root signature's first table of views (the scene's); the lens's and the lamps' follow it.</summary>
    private const int Views = 12;
    private readonly int _roundSlot;
    private CpuDescriptorHandle RoundRtv(int face, int mip) => Rtv(Targets.Length + 4 + GlowLevels + face * RoundMips + mip);
    private CpuDescriptorHandle TintRtv => Rtv(Targets.Length + 4 + GlowLevels + Rounds.Length * 6 * RoundMips);

    /// <summary>The still garden all round from each of <see cref="Rounds"/>, into those of its pictures' twelve faces that
    /// <paramref name="faces"/> names (the cube's order and turns, as <see cref="LampFace"/> has them), then each face's smaller mips.
    /// Drawn as the frame is, in its moment's light, but for what moves, what is worked out from the frame's own depth, and the
    /// mirror images themselves. A frame on its own draws all twelve; of the frames shown one after another each draws one, in turn.</summary>
    private void Surroundings(ID3D12GraphicsCommandList4 l, GardenFrame frame, IEnumerable<int> faces)
    {
        var map = _constants.Map<byte>(0, (int)Slot * (_roundSlot + Rounds.Length * 6));
        var depth = Dsv(6 + Math.Max(1, G.ShadowLamps) * 6);
        foreach (int k in faces)
        {
            var from = Rounds[k / 6].From; int face = k % 6;
            var (to, up) = face switch
            {
                0 => (Vector3.UnitX, Vector3.UnitY), 1 => (-Vector3.UnitX, Vector3.UnitY), 2 => (Vector3.UnitY, -Vector3.UnitZ),
                3 => (-Vector3.UnitY, Vector3.UnitZ), 4 => (Vector3.UnitZ, Vector3.UnitY), _ => (-Vector3.UnitZ, Vector3.UnitY)
            };
            var pass = frame;
            pass.ViewProj = Matrix4x4.CreateLookAtLeftHanded(from, from + to, up) * new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, GardenFrame.Near, 0);
            pass.Eye = from; pass.CamForward = to; pass.CamUp = up; pass.CamRight = Vector3.Cross(up, to); pass.TanHalfFovY = 1; pass.Aspect = 1; pass.ViewSize = new(RoundSize, RoundSize);
            pass.Flags = 0; pass.Ambience.X = 0;
            MemoryMarshal.Write(map[((int)Slot * (_roundSlot + k))..], in pass);
            l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot * (ulong)(_roundSlot + k));
            l.ResourceBarrierTransition(_round, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget, Sub(k, 0));
            l.RSSetViewport(0, 0, RoundSize, RoundSize); l.RSSetScissorRect(RoundSize, RoundSize);
            l.ClearDepthStencilView(depth, ClearFlags.Depth, 0, 0); l.OMSetRenderTargets(RoundRtv(k, 0), depth);
            l.SetPipelineState(_skyR); l.DrawInstanced(3, 1, 0, 0);
            l.SetPipelineState(_opaqueR); Runs(l, _roundRuns[k], kind => kind is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Emissive);
            l.SetPipelineState(_cutoutR); Runs(l, _roundRuns[k], kind => kind is GardenMaterialKind.Cutout);
            l.SetPipelineState(_transparentR); Runs(l, _roundRuns[k], kind => kind is GardenMaterialKind.Glass or GardenMaterialKind.Water);
            l.SetPipelineState(_roundDown);
            for (int m = 1; m < RoundMips; m++)
            {
                // level m - 1 is read while level m is drawn
                l.ResourceBarrierTransition(_round, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource, Sub(k, m - 1));
                l.ResourceBarrierTransition(_round, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget, Sub(k, m));
                l.SetGraphicsRootDescriptorTable(Views + 1, _srv.GetGPUDescriptorHandleForHeapStart().Offset(RoundView + m - 1, _srvSize));
                l.RSSetViewport(0, 0, RoundSize >> m, RoundSize >> m); l.RSSetScissorRect(RoundSize >> m, RoundSize >> m);
                l.OMSetRenderTargets(RoundRtv(k, m), null);
                l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = (uint)k }, 0);
                l.DrawInstanced(3, 1, 0, 0);
            }
            l.ResourceBarrierTransition(_round, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource, Sub(k, RoundMips - 1));
        }
        _constants.Unmap(0);
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
    }
    private static uint Sub(int face, int mip) => (uint)(face * RoundMips + mip);

    private static void Runs(ID3D12GraphicsCommandList4 l, (GardenGpu.Draw Draw, uint First, uint Count)[] runs, Func<GardenMaterialKind, bool> kinds)
    {
        foreach (var (d, first, count) in runs)
            foreach (var p in d.Parts)
            {
                if (!kinds(p.Kind)) continue;
                l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = first, Material = p.Material, Centre = d.Centre, Extent = d.Extent }, 0);
                l.DrawIndexedInstanced(p.IndexCount, count, p.IndexStart, d.BaseVertex, 0);
            }
    }

    /// <summary>The six views of a lamp's shadow cube, in the order Direct3D keeps a cube's faces (+x, -x, +y, -y, +z, -z): ninety
    /// degrees each, from <see cref="GardenGpu.LampNear"/> to the lamp's range.</summary>
    internal static Matrix4x4 LampFace(Vector3 at, float range, int face)
    {
        var (to, up) = face switch
        {
            0 => (Vector3.UnitX, Vector3.UnitY), 1 => (-Vector3.UnitX, Vector3.UnitY), 2 => (Vector3.UnitY, -Vector3.UnitZ),
            3 => (-Vector3.UnitY, Vector3.UnitZ), 4 => (Vector3.UnitZ, Vector3.UnitY), _ => (-Vector3.UnitZ, Vector3.UnitY)
        };
        return Matrix4x4.CreateLookAtLeftHanded(at, at + to, up) * Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(MathF.PI / 2, 1, GardenGpu.LampNear, range);
    }

    /// <summary>The still scene's depth into every shadowed lamp's cube: for each face, the instances within the lamp's range that
    /// the face can see, drawn in runs of neighbours.</summary>
    private void LampShadows(ID3D12GraphicsCommandList4 l, GardenFrame frame)
    {
        var casters = G.Draws.Where(d => G.CastsShadow(d) && !d.Moving && (G.Instances[d.FirstInstance].Flags & GardenScene.FittingFlag) == 0).Select(d => (Draw: d, Bounds: Enumerable.Range((int)d.FirstInstance, (int)d.InstanceCount).Select(i => G.Bounds(d, i)).ToArray())).ToArray();
        var map = _constants.Map<byte>(0, (int)Slot * (Slots + G.ShadowLamps * 6));
        l.SetPipelineState(_lampShadow); l.RSSetViewport(0, 0, LampSize, LampSize); l.RSSetScissorRect(LampSize, LampSize);
        foreach (var lamp in G.PointLights.Where(p => p.Shadow > 0))
            for (int face = 0; face < 6; face++)
            {
                int slot = Slots + ((int)lamp.Shadow - 1) * 6 + face; var pass = frame; pass.ShadowViewProj = LampFace(lamp.Position, lamp.Range, face);
                MemoryMarshal.Write(map[((int)Slot * slot)..], in pass);
                l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot * (ulong)slot);
                l.ClearDepthStencilView(Dsv(6 + slot - Slots), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets([], Dsv(6 + slot - Slots));
                int axis = face / 2; float sign = face % 2 == 0 ? 1 : -1;
                bool Seen((Vector3 Centre, float Radius) b)
                {
                    var c = b.Centre - lamp.Position; if (c.Length() - b.Radius > lamp.Range) return false;
                    float along = (axis == 0 ? c.X : axis == 1 ? c.Y : c.Z) * sign + b.Radius * 1.4143f;   // inside the face's pyramid, or touching it
                    return axis switch { 0 => along >= MathF.Abs(c.Y) && along >= MathF.Abs(c.Z), 1 => along >= MathF.Abs(c.X) && along >= MathF.Abs(c.Z), _ => along >= MathF.Abs(c.X) && along >= MathF.Abs(c.Y) };
                }
                foreach (var (d, bounds) in casters)
                    for (int k = 0; k < bounds.Length;)
                    {
                        if (!Seen(bounds[k])) { k++; continue; }
                        int n = 1; while (k + n < bounds.Length && Seen(bounds[k + n])) n++;
                        foreach (var p in d.Parts)
                        {
                            if (p.Kind is not (GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout)) continue;
                            l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = d.FirstInstance + (uint)k, Material = p.Material, Centre = d.Centre, Extent = d.Extent }, 0);
                            l.DrawIndexedInstanced(p.IndexCount, (uint)n, p.IndexStart, d.BaseVertex, 0);
                        }
                        k += n;
                    }
            }
        _constants.Unmap(0);
        l.ResourceBarrierTransition(_lamps, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
    }
    private (int W, int H) GlowSize(int level) => (Math.Max(1, Width >> (level + 1)), Math.Max(1, Height >> (level + 1)));
    private GpuDescriptorHandle LensView(int i) => _srv.GetGPUDescriptorHandleForHeapStart().Offset(LensViews + i, _srvSize);

    /// <summary>For the render checks: the pool's mirror image of the last frame drawn, rows of its own size, as RGBA8 to look at
    /// (the square root of the squeezed light is close to what a screen shows).</summary>
    internal (uint[] Pixels, int Width, int Height) ReflectionImage()
    {
        uint pitch = ((uint)_reflW * 4 + 255) & ~255u;
        var raw = S.Read((int)(pitch / 4 * _reflH), (l, readback) =>
        {
            l.ResourceBarrierTransition(_reflection, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Light, (uint)_reflW, (uint)_reflH, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(_reflection, 0));
            l.ResourceBarrierTransition(_reflection, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
        });
        var px = new uint[_reflW * _reflH];
        for (int y = 0; y < _reflH; y++)
            for (int x = 0; x < _reflW; x++) { uint p = raw[(int)(y * pitch / 4) + x]; px[y * _reflW + x] = (p >> 2 & 255) | (p >> 12 & 255) << 8 | (p >> 22 & 255) << 16 | 0xFF000000; }
        return (px, _reflW, _reflH);
    }
    public Settings Level => _set;

    private CpuDescriptorHandle Rtv(int i) => _rtv.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize);
    private CpuDescriptorHandle Dsv(int i) => _dsv.GetCPUDescriptorHandleForHeapStart().Offset(i, _dsvSize);

    private static int SupportedSamples(D3D12Session s, int wanted)
    {
        for (int n = wanted; n > 1; n /= 2)
        {
            var q = new FeatureDataMultisampleQualityLevels { Format = Light, SampleCount = (uint)n };
            if (s.Device.CheckFeatureSupport(Vortice.Direct3D12.Feature.MultisampleQualityLevels, ref q) && q.NumQualityLevels > 0) return n;
        }
        return 1;
    }

    /// <summary>An orthographic view from the sun over the whole courtyard (walls, hall and windcatchers included) and the trees that
    /// stand round it, whose shadows the low sun lays across the walls.</summary>
    internal static Matrix4x4 SunShadowMatrix(Vector3 toSun)
    {
        if (toSun.LengthSquared() < 1e-6f) toSun = Vector3.UnitY;
        var centre = new Vector3(0, 4, -14); float radius = 33f;   // the courtyard: x ±14, z -34..7.5, up to the wind-catchers at 11; the trees to 8 m beyond
        var eye = centre + Vector3.Normalize(toSun) * 80;
        var up = MathF.Abs(toSun.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        return Matrix4x4.CreateLookAtLeftHanded(eye, centre, up) * Matrix4x4.CreateOrthographicLeftHanded(radius * 2, radius * 2, 1f, 160f);
    }

    /// <summary>The same courtyard seen from straight above: what the sky map is drawn through.</summary>
    internal static Matrix4x4 SkyMatrix()
    {
        var centre = new Vector3(0, 0, -14); float radius = 33f;
        return Matrix4x4.CreateLookAtLeftHanded(centre + Vector3.UnitY * 60, centre, Vector3.UnitZ) * Matrix4x4.CreateOrthographicLeftHanded(radius * 2, radius * 2, 1f, 90f);
    }

    /// <summary>The key light's view of the hall alone - its rooms, its front wall and door - from the same side as
    /// <see cref="SunShadowMatrix"/>, a third as wide: the hall's own, finer shadow map, and the stained panes' colours, are drawn through it.</summary>
    internal static Matrix4x4 HallShadowMatrix(Vector3 toKey)
    {
        if (toKey.LengthSquared() < 1e-6f) toKey = Vector3.UnitY;
        var (_, low, high) = Rounds[1]; var centre = (low + high) / 2 + new Vector3(0, 0, -0.2f);
        var up = MathF.Abs(toKey.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        return Matrix4x4.CreateLookAtLeftHanded(centre + Vector3.Normalize(toKey) * 70, centre, up) * Matrix4x4.CreateOrthographicLeftHanded(HallRadius * 2, HallRadius * 2, 1f, 90f);
    }
    /// <summary>Half the width of that view: the hall's box and half a metre round it, a little more toward the terrace.</summary>
    internal const float HallRadius = 12.3f;

    protected override void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target, bool live)
    {
        var day = G.Day(time); G.Update(time);
        var frame = GardenFrame.For(G, time, Width, Height);
        // The key light crosses the sky slowly: of the frames shown one after another its shadow maps are drawn anew only every few
        // (the frames between are lit through the maps as they were last drawn) - the courtyard's every other frame, or every sixth
        // while the walk is in the hall; the hall's own every frame from the terrace on, every sixth from down the garden. A frame
        // on its own, and the first shown after one, draws them all.
        var (eye, _) = GardenCamera.At(time); bool indoors = eye.Z > -4.6f, fresh = !live || _shown == 0; _shown = live ? _shown + 1 : 0;
        bool drawKey = fresh || _shown % (indoors ? 6 : 2) == 0, drawHall = fresh || eye.Z > -11f || _shown % 6 == 0;
        if (drawKey) _keyView = SunShadowMatrix(day.Key);
        if (drawHall) _hallView = HallShadowMatrix(day.Key);
        frame.ShadowViewProj = _keyView; frame.HallViewProj = _hallView; frame.ShadowTexel = 1f / _set.ShadowSize; frame.ShadowTaps = (uint)_set.ShadowTaps;
        frame.SkyViewProj = _skyViewProj; frame.Ambience = new(1, _set.OcclusionTaps, GardenFrame.Near, 0); frame.Lens.W = _set.LensTaps;
        if (G.LightVolume is { } volume) { frame.Grid = new(volume.Origin, volume.Spacing); frame.Grid2 = new(volume.X, volume.Y, volume.Z, 1); }
        frame.Round0 = new(Rounds[0].From, RoundMips); frame.Round0Low = new(Rounds[0].Low, 0); frame.Round0High = new(Rounds[0].High, 0);
        frame.Round1 = new(Rounds[1].From, RoundMips);
        var first = frame; first.Post.Z = 0;   // the passes of the still scene see no mirror images (the sky stands in) and no wind: the plants stand as they were placed
        if (_set.ReflectionDivisor > 0) frame.Flags |= 1;
        if (_samples > 1) frame.Flags |= 4;
        frame.Flags |= 8;
        var mirrored = frame.Mirrored(); mirrored.Flags &= ~4u; mirrored.ViewSize = new(_reflW, _reflH);
        // the depth-only passes draw through ShadowViewProj: the camera's own view for the frame's depth, the view from above for the sky map, the hall's own for its shadow map
        var depthOnly = frame; depthOnly.ShadowViewProj = frame.ViewProj; var above = first; above.ShadowViewProj = _skyViewProj; var hall = frame; hall.ShadowViewProj = frame.HallViewProj;
        var map = _constants.Map<byte>(0, (int)Slot * Slots);
        MemoryMarshal.Write(map, in frame); MemoryMarshal.Write(map[(int)Slot..], in mirrored); MemoryMarshal.Write(map[((int)Slot * 2)..], in depthOnly); MemoryMarshal.Write(map[((int)Slot * 3)..], in above);
        MemoryMarshal.Write(map[((int)Slot * 4)..], in first); MemoryMarshal.Write(map[((int)Slot * 5)..], in hall);
        _constants.Unmap(0);

        // the light bounced round the garden, as this moment of the day has it (put together anew every fourth frame shown)
        if (G.LightVolume is { } bounced && _volumeStaging is not null && (fresh || _shown % 4 == 1))
        {
            bounced.Mix(_mixed, day.SkyLight, day.Lamps, GardenGpu.HallLit(day), day.Moon ? Vector3.Zero : day.KeyColor, day.Sun);
            int rows = bounced.Y * bounced.Z, across = bounced.X * 6 * 4; var dst = _volumeStaging.Map<byte>(0, (int)_volumePitch * rows);
            for (int row = 0; row < rows; row++) MemoryMarshal.AsBytes(_mixed.AsSpan(row * across, across)).CopyTo(dst[(row * (int)_volumePitch)..]);
            _volumeStaging.Unmap(0);
            l.ResourceBarrierTransition(_volume, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
            var footprint = new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R32G32B32A32_Float, (uint)(bounced.X * 6), (uint)bounced.Y, (uint)bounced.Z, _volumePitch) };
            l.CopyTextureRegion(new TextureCopyLocation(_volume, 0), 0, 0, 0, new TextureCopyLocation(_volumeStaging, footprint));
            l.ResourceBarrierTransition(_volume, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        }

        l.SetGraphicsRootSignature(_root); l.SetDescriptorHeaps(_srv);
        l.SetGraphicsRootShaderResourceView(2, G.InstanceBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(3, G.MaterialBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(4, G.LightBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(5, G.MoverBuffer.GPUVirtualAddress);
        l.SetGraphicsRootShaderResourceView(6, G.LightGridBuffer.GPUVirtualAddress);
        l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        l.IASetVertexBuffers(0, new VertexBufferView(G.VertexBuffer.GPUVirtualAddress, (uint)G.VertexBuffer.Description.Width, 16));
        l.IASetIndexBuffer(new IndexBufferView(G.IndexBuffer.GPUVirtualAddress, (uint)G.IndexBuffer.Description.Width, Format.R32_UInt));
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        // the views are bound from the start: the depth passes read the leaves' textures through them (and nothing they are drawing into)
        l.SetGraphicsRootDescriptorTable(Views, _srv.GetGPUDescriptorHandleForHeapStart());
        l.SetGraphicsRootDescriptorTable(Views + 2, _srv.GetGPUDescriptorHandleForHeapStart().Offset(LampView, _srvSize));

        if (!_onceDrawn)
        {   // what is drawn once: where the sky stands open over the still scene (its depth from straight above), and the lamps' shadow cubes
            static bool Still(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout;
            l.SetPipelineState(_shadow); l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot * 3);
            l.RSSetViewport(0, 0, SkySize, SkySize); l.RSSetScissorRect(SkySize, SkySize);
            l.ClearDepthStencilView(Dsv(4), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets([], Dsv(4));
            Geometry(l, Still, draw => G.CastsShadow(draw) && !draw.Moving);
            l.ResourceBarrierTransition(_skyMap, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
            LampShadows(l, first);
            _onceDrawn = true; l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        }
        // 1. the key light's shadow maps - the light moves, so they are drawn anew: the courtyard's, the hall's own, and through the
        // hall's view the stained panes, each laying its colour over what is behind it
        static bool Casts(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout;
        l.RSSetViewport(0, 0, _set.ShadowSize, _set.ShadowSize); l.RSSetScissorRect(_set.ShadowSize, _set.ShadowSize);
        l.SetPipelineState(_shadow);
        if (drawKey)
        {
            l.ResourceBarrierTransition(_shadowMap, ResourceStates.PixelShaderResource, ResourceStates.DepthWrite);
            l.ClearDepthStencilView(Dsv(1), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets([], Dsv(1));
            Geometry(l, Casts, G.CastsShadow);
            l.ResourceBarrierTransition(_shadowMap, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
        }
        if (drawHall) HallMaps(l, frame);
        // the garden all round, for what surfaces mirror: every face for a frame on its own (and for the first shown after one),
        // else the next face in turn - twelve frames on, the pictures are of the light as it is by then
        Surroundings(l, first, live && _roundsDrawn ? [_roundNext++ % (Rounds.Length * 6)] : Enumerable.Range(0, Rounds.Length * 6)); _roundsDrawn = live;
        DrawFrame(l, target, frame);
    }

    /// <summary>The hall's own shadow map and, through the same view, the stained panes, each laying its colour over what is behind it.</summary>
    private void HallMaps(ID3D12GraphicsCommandList4 l, GardenFrame frame)
    {
        static bool Casts(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout;
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot * 5);
        l.ResourceBarrierTransition(_hallShadow, ResourceStates.PixelShaderResource, ResourceStates.DepthWrite);
        l.ClearDepthStencilView(Dsv(3), ClearFlags.Depth, 1, 0); l.OMSetRenderTargets([], Dsv(3));
        // of all that casts shadows, only what stands in the light's way to the hall: inside its view's square, or touching it
        foreach (var (d, bounds) in _casters)
        {
            bool Reaches((Vector3 Centre, float Radius) b)
            {
                var c = Vector4.Transform(new Vector4(b.Centre, 1), frame.HallViewProj); float r = b.Radius / HallRadius;
                return MathF.Abs(c.X) <= 1 + r && MathF.Abs(c.Y) <= 1 + r;
            }
            for (int k = 0; k < bounds.Length;)
            {
                if (!d.Moving && !Reaches(bounds[k])) { k++; continue; }
                int n = 1; while (k + n < bounds.Length && (d.Moving || Reaches(bounds[k + n]))) n++;
                foreach (var p in d.Parts)
                {
                    if (!Casts(p.Kind)) continue;
                    l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = d.FirstInstance + (uint)k, Material = p.Material, Centre = d.Centre, Extent = d.Extent }, 0);
                    l.DrawIndexedInstanced(p.IndexCount, (uint)n, p.IndexStart, d.BaseVertex, 0);
                }
                k += n;
            }
        }
        l.ResourceBarrierTransition(_hallShadow, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
        l.ResourceBarrierTransition(_hallTint, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        l.ClearRenderTargetView(TintRtv, new Color4(1, 1, 1, 1)); l.OMSetRenderTargets(TintRtv, null);
        l.RSSetViewport(0, 0, TintSize, TintSize); l.RSSetScissorRect(TintSize, TintSize); l.SetPipelineState(_tint);
        foreach (var (d, p) in _stained)
        {
            l.SetGraphicsRoot32BitConstants(0, new DrawConstants { InstanceBase = d.FirstInstance, Material = p.Material, Centre = d.Centre, Extent = d.Extent }, 0);
            l.DrawIndexedInstanced(p.IndexCount, d.InstanceCount, p.IndexStart, d.BaseVertex, 0);
        }
        l.ResourceBarrierTransition(_hallTint, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        l.RSSetViewport(0, 0, _set.ShadowSize, _set.ShadowSize); l.RSSetScissorRect(_set.ShadowSize, _set.ShadowSize);
    }

    /// <summary>The frame itself, once its maps are drawn: the pool's mirror image, the depth and the occlusion, the light, the lens.</summary>
    private void DrawFrame(ID3D12GraphicsCommandList4 l, int target, GardenFrame frame)
    {
        static bool Casts(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Cutout;
        static bool Solid(GardenMaterialKind k) => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Emissive;
        // 2. the pool's mirror image: only the part of the picture the pool's water takes up (and a margin for its ripples), none when it is out of sight
        if (_set.ReflectionDivisor > 0 && PoolOnScreen(frame) is { } seen)
        {
            l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot);
            l.ResourceBarrierTransition(_reflection, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            l.ClearDepthStencilView(Dsv(2), ClearFlags.Depth, 0, 0); l.ClearRenderTargetView(Rtv(Targets.Length), new Color4(0, 0, 0, 1)); l.OMSetRenderTargets(Rtv(Targets.Length), Dsv(2));   // (cleared whole: what is left outside the part drawn must not be an earlier frame's)
            l.RSSetViewport(0, 0, _reflW, _reflH); l.RSSetScissorRect(seen);
            Pass(l, _skyR, _opaqueR, _cutoutR, _transparentR, skipWater: true);
            l.ResourceBarrierTransition(_reflection, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        }

        // 3. the frame's depth (solid geometry and leaves), and from it the ambient occlusion
        l.RSSetViewport(0, 0, Width, Height); l.RSSetScissorRect(Width, Height);
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress + Slot * 2);
        l.ResourceBarrierTransition(_sceneDepth, ResourceStates.PixelShaderResource, ResourceStates.DepthWrite);
        l.ClearDepthStencilView(Dsv(5), ClearFlags.Depth, 0, 0); l.OMSetRenderTargets([], Dsv(5));
        l.SetPipelineState(_depthOnly); Geometry(l, Casts);
        l.ResourceBarrierTransition(_sceneDepth, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
        l.SetGraphicsRootConstantBufferView(1, _constants.GPUVirtualAddress);
        l.ResourceBarrierTransition(_occlusion, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        l.OMSetRenderTargets(Rtv(Targets.Length + 2), null);
        l.SetPipelineState(_occlude); l.DrawInstanced(3, 1, 0, 0);
        l.ResourceBarrierTransition(_occlusion, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);

        // 4. the frame's light
        var rtv = _samples > 1 ? Rtv(Targets.Length + 1) : Rtv(Targets.Length + 3);
        if (_samples == 1) l.ResourceBarrierTransition(_light, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        // first the solid surfaces' depth alone, so that of the surfaces one behind another only the nearest is lit
        l.ClearDepthStencilView(Dsv(0), ClearFlags.Depth, 0, 0); l.OMSetRenderTargets([], Dsv(0));
        l.SetPipelineState(_before); Geometry(l, Solid);
        l.OMSetRenderTargets(rtv, Dsv(0));
        Pass(l, _sky, _opaque, _cutout, _transparent, skipWater: false);
        if (_samples > 1)
        {
            l.ResourceBarrierTransition(_msaa, ResourceStates.RenderTarget, ResourceStates.ResolveSource);
            l.ResourceBarrierTransition(_light, ResourceStates.PixelShaderResource, ResourceStates.ResolveDest);
            l.ResolveSubresource(_light, 0, _msaa, 0, Light);
            l.ResourceBarrierTransition(_light, ResourceStates.ResolveDest, ResourceStates.PixelShaderResource);
            l.ResourceBarrierTransition(_msaa, ResourceStates.ResolveSource, ResourceStates.RenderTarget);
        }
        else l.ResourceBarrierTransition(_light, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);

        // 5. the lens: the glow down its levels and summed back up, then the picture
        void Full(ID3D12PipelineState pipeline, ID3D12Resource to, CpuDescriptorHandle view, int w, int h, int reads)
        {
            l.ResourceBarrierTransition(to, to == Targets[target] ? ResourceStates.Present : ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            l.OMSetRenderTargets(view, null); l.RSSetViewport(0, 0, w, h); l.RSSetScissorRect(w, h);
            l.SetGraphicsRootDescriptorTable(Views + 1, LensView(reads)); l.SetPipelineState(pipeline); l.DrawInstanced(3, 1, 0, 0);
            l.ResourceBarrierTransition(to, ResourceStates.RenderTarget, to == Targets[target] ? ResourceStates.Present : ResourceStates.PixelShaderResource);
        }
        for (int k = 0; k < GlowLevels; k++) Full(k == 0 ? _glowFirst : _glowDown, _glow[k], Rtv(Targets.Length + 4 + k), GlowSize(k).W, GlowSize(k).H, k);
        for (int k = GlowLevels - 2; k >= 0; k--) Full(_glowUp, _glow[k], Rtv(Targets.Length + 4 + k), GlowSize(k).W, GlowSize(k).H, k + 2);
        Full(_lens, Targets[target], Rtv(target), Width, Height, 0);
    }

    /// <summary>The part of the mirror image's picture the pool's water can be seen in (null: none of it). With a corner of the pool
    /// behind the eye, the whole picture.</summary>
    private Vortice.RawRect? PoolOnScreen(GardenFrame frame)
    {
        float x0 = 1, y0 = 1, x1 = -1, y1 = -1; int behind = 0;
        foreach (var corner in (Vector3[])[new(_pool.X, frame.WaterLevel, _pool.Y), new(_pool.Z, frame.WaterLevel, _pool.Y), new(_pool.X, frame.WaterLevel, _pool.W), new(_pool.Z, frame.WaterLevel, _pool.W)])
        {
            var c = Vector4.Transform(new Vector4(corner, 1), frame.ViewProj);
            if (c.W < 0.05f) { behind++; continue; }
            x0 = MathF.Min(x0, c.X / c.W); x1 = MathF.Max(x1, c.X / c.W); y0 = MathF.Min(y0, c.Y / c.W); y1 = MathF.Max(y1, c.Y / c.W);
        }
        if (behind == 4) return null;
        if (behind > 0) return new(0, 0, _reflW, _reflH);
        x0 = MathF.Max(x0 - 0.12f, -1); x1 = MathF.Min(x1 + 0.12f, 1); y0 = MathF.Max(y0 - 0.12f, -1); y1 = MathF.Min(y1 + 0.12f, 1);
        if (x0 >= x1 || y0 >= y1) return null;
        return new((int)((x0 * 0.5f + 0.5f) * _reflW), (int)((0.5f - y1 * 0.5f) * _reflH), (int)MathF.Ceiling((x1 * 0.5f + 0.5f) * _reflW), (int)MathF.Ceiling((0.5f - y0 * 0.5f) * _reflH));
    }

    private void Pass(ID3D12GraphicsCommandList4 l, ID3D12PipelineState sky, ID3D12PipelineState opaque, ID3D12PipelineState cutout, ID3D12PipelineState transparent, bool skipWater, bool still = false)
    {
        Func<GardenGpu.Draw, bool>? which = still ? d => !d.Moving : null;
        l.SetPipelineState(sky); l.DrawInstanced(3, 1, 0, 0);
        l.SetPipelineState(opaque); Geometry(l, k => k is GardenMaterialKind.Flat or GardenMaterialKind.Brick or GardenMaterialKind.Emissive, which);
        l.SetPipelineState(cutout); Geometry(l, k => k is GardenMaterialKind.Cutout, which);
        l.SetPipelineState(transparent); Geometry(l, k => k is GardenMaterialKind.Glass or GardenMaterialKind.Smoke || (k is GardenMaterialKind.Water && !skipWater), which);
    }

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

    /// <summary>Draws the frame at <paramref name="time"/> into target <paramref name="target"/>, as one of the frames being shown
    /// one after another (the ray tracer adds each one's light to what it has gathered from those before).</summary>
    public void Draw(ID3D12GraphicsCommandList4 l, float time, int target) => DrawScene(l, time, target, live: true);
    /// <summary><paramref name="live"/>: the frame follows the one drawn before it, and may build on it. A frame that does not is a
    /// pure function of its time, and leaves nothing behind for the next.</summary>
    protected abstract void DrawScene(ID3D12GraphicsCommandList4 l, float time, int target, bool live);

    /// <summary>The frame at <paramref name="time"/>, drawn off screen and read back: RGBA8 pixels, rows of <see cref="Width"/>.
    /// On its own (the check frames: the same time is the same bits, whatever was drawn before), or with <paramref name="live"/>
    /// as the next of the frames being shown.</summary>
    public uint[] Capture(float time, bool live = false)
    {
        int own = Targets.Length - 1; uint pitch = ((uint)Width * 4 + 255) & ~255u;
        var raw = S.Read((int)(pitch / 4 * Height), (l, readback) =>
        {
            DrawScene(l, time, own, live);
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
