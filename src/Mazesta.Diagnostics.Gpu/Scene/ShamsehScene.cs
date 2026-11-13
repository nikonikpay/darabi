using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The temperature test's picture (Shaders/Shamseh.hlsl): the Gate of All Nations (a scan, every triangle of it: <see cref="GateModel"/>) standing on a lawn under an aurora
/// that fills the window, swinging a little about facing the camera. The work is grass drawn as layers blended into a float frame (read from the cache,
/// so the cores work and the memory is not overheated): what draws a card to its power limit with the clock held well under its boost, as a fur test does.
/// Drawn into the window's swap chain with no v-sync, the readout laid over it; <see cref="CheckFrame"/> draws a fixed moment off screen for the run's checks.
/// The picture before this one, a velvet star with the same fur, is the git tag pre-gate-thermal-scene.
/// </summary>
internal sealed unsafe class ShamsehScene : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Constants { public float Ry, Rx, Aspect; public uint Layers, FurLayers; public float Spread, FurLength, Time; public Vector2 Size, Pad2; }
    /// <summary>
    /// The sky's layers and the lawn's. The star's, measured on an RTX 3090 at 1080p, 180 s each, all at its 350 W limit: the texture read at 7.6 texels
    /// a pixel into a 16-bit frame ran the card's memory to 100 C, hotter than FurMark here (91.5 C average, 98 C peak); read at one texel a pixel into an
    /// 11-11-10 float frame the work moved from the memory to the cores and their caches, as a fur test's does (86.7 C average, 92 C peak). The lawn keeps that
    /// way of reading; its layer count is set by what the gate's picture measured (see the commit that brought it).
    /// </summary>
    public const int Layers = 24, FurLayers = 160;
    private const float Spread = 1f, FurLength = 0.07f;
    private const Format LightFormat = Format.R11G11B10_Float;
    private const int StrandSize = 2048;
    /// <summary>The lawn under the gate and the aurora behind it are left out for now (the gate alone, as the scan has it); both are still here to switch on.</summary>
    private static readonly bool WithLawn = false, WithSky = false;

    private readonly D3D12Session _s; private readonly IDXGISwapChain3 _swap; private readonly ID3D12Resource[] _back; private readonly bool _tearing;
    private readonly ID3D12Resource _light, _depth, _check, _vertices, _indices;
    private readonly ID3D12DescriptorHeap _views, _rtv, _dsv; private readonly uint _rtvSize;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _solid, _fur, _ground, _lens;
    private readonly uint _gateIndices, _lawnIndices;
    /// <summary>The triangles of the gate (and the lawn's two when it is on): what the evidence line reports.</summary>
    public int Triangles => (int)((_gateIndices + _lawnIndices) / 3);
    public SceneOverlay Overlay { get; }
    public int Width { get; }
    public int Height { get; }

    public ShamsehScene(D3D12Session s, TestWindow w, int width, int height)
    {
        _s = s; Width = width; Height = height;
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory5>(false);
        _tearing = factory.PresentAllowTearing;
        var desc = new SwapChainDescription1
        {
            Width = (uint)width, Height = (uint)height, Format = Format.R8G8B8A8_UNorm, BufferUsage = Usage.RenderTargetOutput, BufferCount = (uint)(s.Frames + 1), Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore, SampleDescription = SampleDescription.Default, Flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None
        };
        using var swap1 = factory.CreateSwapChainForHwnd(s.Queue, w.Handle, desc);
        factory.MakeWindowAssociation(w.Handle, WindowAssociationFlags.IgnoreAltEnter);
        _swap = swap1.QueryInterface<IDXGISwapChain3>();
        _back = [.. Enumerable.Range(0, s.Frames + 1).Select(i => _swap.GetBuffer<ID3D12Resource>((uint)i))];
        Overlay = new SceneOverlay(s, _back, height, heat: true);

        var gate = GateModel.Load(); _gateIndices = (uint)gate.Indices.Length;
        var (vertices, indices) = WithLawn ? Lawn(gate.Vertices, gate.Indices) : (gate.Vertices, gate.Indices); _lawnIndices = (uint)indices.Length - _gateIndices;
        _vertices = s.Upload(vertices, ResourceStates.AllShaderResource); _indices = s.Upload(indices, ResourceStates.IndexBuffer);
        var strands = new byte[StrandSize * StrandSize * 4]; new Random(7).NextBytes(strands);   // the same strands every run: the check frames compare like with like
        var stranded = Texture(Format.R8G8B8A8_UNorm, StrandSize, StrandSize, strands); var colour = Mips(gate.Albedo, gate.Size);

        _light = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(LightFormat, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(LightFormat, Background)));
        _depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0)));
        _check = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget), ResourceStates.CopySource));

        _views = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 3, DescriptorHeapFlags.ShaderVisible)));
        uint viewSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        ID3D12Resource[] read = [stranded, colour, _light];
        for (int i = 0; i < read.Length; i++) s.Device.CreateShaderResourceView(read[i], null, _views.GetCPUDescriptorHandleForHeapStart().Offset(i, viewSize));
        _rtv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, (uint)_back.Length + 2)));
        _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        ID3D12Resource[] drawn = [_light, .. _back, _check];
        for (int i = 0; i < drawn.Length; i++) s.Device.CreateRenderTargetView(drawn[i], null, Rtv(i));
        _dsv = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1)));
        s.Device.CreateDepthStencilView(_depth, null, _dsv.GetCPUDescriptorHandleForHeapStart());

        byte[] solidVs = D3D12Session.Shader("ShamsehSolidVS");
        _root = s.Own(s.Device.CreateRootSignature(solidVs));
        var layered = new BlendDescription(Blend.One, Blend.InverseSourceAlpha);   // each layer over the ones beneath it (its colour is already times its cover)
        var under = new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual);
        ID3D12PipelineState Pipeline(byte[] vs, string ps, BlendDescription blend, DepthStencilDescription depth, Format target) => s.Own(s.Device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = _root, VertexShader = vs, PixelShader = D3D12Session.Shader(ps), PrimitiveTopologyType = PrimitiveTopologyType.Triangle, BlendState = blend, RasterizerState = RasterizerDescription.CullNone,
            SampleMask = uint.MaxValue, DepthStencilState = depth, DepthStencilFormat = depth.DepthEnable ? Format.D32_Float : Format.Unknown, RenderTargetFormats = [target], SampleDescription = SampleDescription.Default
        }));
        _solid = Pipeline(solidVs, "ShamsehSolidPS", BlendDescription.Opaque, new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual), LightFormat);
        _ground = Pipeline(D3D12Session.Shader("ShamsehGroundVS"), "ShamsehGroundPS", layered, under, LightFormat);
        _fur = Pipeline(D3D12Session.Shader("ShamsehFurVS"), "ShamsehFurPS", layered, under, LightFormat);
        _lens = Pipeline(D3D12Session.Shader("ShamsehLensVS"), "ShamsehLensPS", BlendDescription.Opaque, DepthStencilDescription.None, Format.R8G8B8A8_UNorm);
    }

    private static readonly Color4 Background = new(0.01f, 0.02f, 0.03f, 1);
    private CpuDescriptorHandle Rtv(int i) => _rtv.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize);

    /// <summary>A four-bytes-a-texel texture with its whole mip chain (each level the mean of two by two of the one before): the grass reads it by its footprint, so far blades are cache-friendly.</summary>
    private ID3D12Resource Texture(Format format, int w, int h, byte[] data)
    {
        var levels = new List<(int W, int H, byte[] Data)> { (w, h, data) };
        while (levels[^1].W > 1 || levels[^1].H > 1)
        {
            var (pw, ph, p) = levels[^1]; int nw = Math.Max(1, pw / 2), nh = Math.Max(1, ph / 2); var n = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++) for (int x = 0; x < nw; x++) for (int c = 0; c < 4; c++)
                n[(y * nw + x) * 4 + c] = (byte)((p[(Math.Min(ph - 1, 2 * y) * pw + Math.Min(pw - 1, 2 * x)) * 4 + c] + p[(Math.Min(ph - 1, 2 * y) * pw + Math.Min(pw - 1, 2 * x + 1)) * 4 + c]
                    + p[(Math.Min(ph - 1, 2 * y + 1) * pw + Math.Min(pw - 1, 2 * x)) * 4 + c] + p[(Math.Min(ph - 1, 2 * y + 1) * pw + Math.Min(pw - 1, 2 * x + 1)) * 4 + c] + 2) / 4);
            levels.Add((nw, nh, n));
        }
        var t = _s.Own(_s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(format, (uint)w, (uint)h, 1, (ushort)levels.Count), ResourceStates.CopyDest));
        var places = new List<(ulong Offset, uint Pitch)>(); ulong total = 0;
        foreach (var lv in levels) { uint pitch = ((uint)lv.W * 4 + 255) & ~255u; total = (total + 511) & ~511ul; places.Add((total, pitch)); total += pitch * (ulong)lv.H; }
        using var staging = _s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(total), ResourceStates.GenericRead);
        var to = staging.Map<byte>(0, (int)total);
        for (int m = 0; m < levels.Count; m++) for (int y = 0; y < levels[m].H; y++) levels[m].Data.AsSpan(y * levels[m].W * 4, levels[m].W * 4).CopyTo(to[(int)(places[m].Offset + (ulong)y * places[m].Pitch)..]);
        staging.Unmap(0);
        _s.Run(l =>
        {
            for (int m = 0; m < levels.Count; m++)
                l.CopyTextureRegion(new TextureCopyLocation(t, (uint)m), 0, 0, 0, new TextureCopyLocation(staging, new PlacedSubresourceFootPrint { Offset = places[m].Offset, Footprint = new SubresourceFootPrint(format, (uint)levels[m].W, (uint)levels[m].H, 1, places[m].Pitch) }));
            l.ResourceBarrierTransition(t, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        });
        return t;
    }

    /// <summary>The gate's vertices and indices with the lawn after them: a big square of soil at y = 0 (material 1, uv in blades' units), the grass's ground.</summary>
    private static (float[] Vertices, uint[] Indices) Lawn(float[] vertices, uint[] indices)
    {
        uint first = (uint)(vertices.Length / 9); const float E = 40, U = 0.4f;
        float[] quad = [-E, 0, -E, 0, 1, 0, -E * U, -E * U, 1, E, 0, -E, 0, 1, 0, E * U, -E * U, 1, E, 0, E, 0, 1, 0, E * U, E * U, 1, -E, 0, E, 0, 1, 0, -E * U, E * U, 1];
        return ([.. vertices, .. quad], [.. indices, first, first + 1, first + 2, first, first + 2, first + 3]);
    }

    /// <summary>A picture's mip chain (BC3, <paramref name="size"/> down to 4, largest first) as a texture: staged at 512-byte boundaries, rows 256-byte aligned, as buffer-to-texture copies require.</summary>
    private ID3D12Resource Mips(byte[][] levels, int size)
    {
        var t = _s.Own(_s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.BC3_UNorm_SRgb, (uint)size, (uint)size, 1, (ushort)levels.Length), ResourceStates.CopyDest));
        var places = new List<(ulong Offset, uint Pitch, int Rows, int Width)>(); ulong total = 0;
        for (int m = 0, w = size; m < levels.Length; m++, w = Math.Max(4, w / 2))
        {
            int blocks = Math.Max(1, w / 4); uint pitch = (uint)((blocks * 16 + 255) & ~255);
            total = (total + 511) & ~511ul; places.Add((total, pitch, blocks, w)); total += pitch * (ulong)blocks;
        }
        using var staging = _s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(total), ResourceStates.GenericRead);
        var to = staging.Map<byte>(0, (int)total);
        for (int m = 0; m < places.Count; m++)
            for (int r = 0; r < places[m].Rows; r++) levels[m].AsSpan(r * places[m].Rows * 16, places[m].Rows * 16).CopyTo(to[(int)(places[m].Offset + (ulong)r * places[m].Pitch)..]);
        staging.Unmap(0);
        _s.Run(l =>
        {
            for (int m = 0; m < places.Count; m++)
            {
                var p = places[m]; var footprint = new PlacedSubresourceFootPrint { Offset = p.Offset, Footprint = new SubresourceFootPrint(Format.BC3_UNorm_SRgb, (uint)p.Width, (uint)p.Width, 1, p.Pitch) };
                l.CopyTextureRegion(new TextureCopyLocation(t, (uint)m), 0, 0, 0, new TextureCopyLocation(staging, footprint));
            }
            l.ResourceBarrierTransition(t, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        });
        return t;
    }

    // The star swings a little about facing the camera, never turning edge on: its fur is most of the frame's work, and turning it right round
    // made that work swing with the area it covered (RTX 3090: 190 FPS face on, 250 edge on). Over this swing (12-23 degrees) the frame rate moves by about 1%.
    private Constants At(float time) => new()
    {
        Ry = 0.3f + 0.1f * MathF.Sin(time * 0.35f), Rx = 0.06f * MathF.Sin(time * 0.23f), Aspect = Width / (float)Height, Layers = Layers, FurLayers = FurLayers, Spread = Spread, FurLength = FurLength, Time = time, Size = new(Width, Height)
    };

    /// <summary>The frame at <paramref name="time"/> into render target view <paramref name="rtv"/> (<paramref name="target"/>, which is in <paramref name="state"/> before and after).</summary>
    private void Draw(ID3D12GraphicsCommandList4 l, float time, int rtv, ID3D12Resource target, ResourceStates state)
    {
        var k = At(time); var dsv = _dsv.GetCPUDescriptorHandleForHeapStart();
        l.ResourceBarrierTransition(_light, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        l.OMSetRenderTargets(Rtv(0), dsv);
        l.ClearRenderTargetView(Rtv(0), Background); l.ClearDepthStencilView(dsv, ClearFlags.Depth, 1, 0);
        l.RSSetViewport(new Viewport(Width, Height)); l.RSSetScissorRect(Width, Height);
        l.SetGraphicsRootSignature(_root); l.SetDescriptorHeaps(1, [_views]);
        l.SetGraphicsRoot32BitConstants(0, k, 0); l.SetGraphicsRootShaderResourceView(1, _vertices.GPUVirtualAddress); l.SetGraphicsRootDescriptorTable(2, _views.GetGPUDescriptorHandleForHeapStart());
        l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList); l.IASetIndexBuffer(new IndexBufferView(_indices.GPUVirtualAddress, (_gateIndices + _lawnIndices) * 4, Format.R32_UInt));
        l.SetPipelineState(_solid); l.DrawIndexedInstanced(_gateIndices + _lawnIndices, 1, 0, 0, 0);   // the gate, and the soil under the grass
        if (WithSky) { l.SetPipelineState(_ground); l.DrawInstanced(3, Layers, 0, 0); }   // the sky behind them: the depth test keeps the aurora off them
        if (WithLawn) { l.SetPipelineState(_fur); l.DrawIndexedInstanced(_lawnIndices, FurLayers, _gateIndices, 0, 0); }   // the grass, its layers in instance order: the inner ones first
        l.ResourceBarrierTransition(_light, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        l.ResourceBarrierTransition(target, state, ResourceStates.RenderTarget);
        l.OMSetRenderTargets(Rtv(rtv)); l.SetPipelineState(_lens); l.DrawInstanced(3, 1, 0, 0);
        l.ResourceBarrierTransition(target, ResourceStates.RenderTarget, state);
    }

    /// <summary>Draws the frame at <paramref name="time"/> and shows it as fast as the card goes (no v-sync), the readout over it.</summary>
    public void Present(float time)
    {
        int index = (int)_swap.CurrentBackBufferIndex;
        _s.Run(l => { Draw(l, time, 1 + index, _back[index], ResourceStates.Present); Overlay.Draw(l, index, Width, Height); }, wait: false);
        _swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();
        if (_s.Frames == 1) _s.Finish(); else _s.NextSlot();
    }

    /// <summary>The frame at a fixed moment drawn off screen and read back, as its 16 x 16 mean colours (<see cref="SceneView.Means"/>): the same every time on a card that computes rightly.</summary>
    public float[] CheckFrame(float time)
    {
        _s.Finish();
        uint pitch = ((uint)Width * 4 + 255) & ~255u;
        var raw = _s.Read((int)(pitch / 4 * Height), (l, readback) =>
        {
            Draw(l, time, 1 + _back.Length, _check, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(_check, 0));
        });
        var px = new uint[Width * Height];
        for (int y = 0; y < Height; y++) raw.AsSpan((int)(y * pitch / 4), Width).CopyTo(px.AsSpan(y * Width));
        return SceneView.Means(px, Width, Height);
    }

    /// <summary>Waits for the card first: freeing what it still draws with is a fault the driver answers by resetting the card (see <see cref="SceneView.Dispose"/>).</summary>
    public void Dispose()
    {
        try { _s.Finish(); } catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or GpuLostException) { }
        Overlay.Dispose(); foreach (var b in _back) b.Dispose(); _swap.Dispose();
    }
}

/// <summary>
/// The gate, read from the embedded Scene/gate.mzgate that tools/gate makes from the owner's scan (Mazesta-Art/gate.blend): every one of the scan's
/// two million triangles and its colour picture (4096 square JPEG, the scan's 8192 halved), the numbers stored in fewer bytes. gzip of "MZG2", then
/// (uint32 each) vertex count, index count, picture size, picture length, the bounding box (min xyz, extent xyz: float32), then plane by plane over the
/// vertices (delta-coded modulo 2^16): x, y, z as uint16 across the box, the normal as two int16 (octahedral), u and v as uint16; then the indices as
/// zigzag varint deltas, then the picture. The gate stands 1.8 tall on y = 0, its front to +z. The picture comes back as BC3 mips.
/// </summary>
internal sealed class GateModel
{
    public float[] Vertices { get; private init; } = []; public uint[] Indices { get; private init; } = [];
    public byte[][] Albedo { get; private init; } = []; public int Size { get; private init; }

    public static GateModel Load()
    {
        using var raw = typeof(GateModel).Assembly.GetManifestResourceStream("Mazesta.Diagnostics.Gpu.Scene.gate.mzgate") ?? throw new InvalidDataException("The gate model is not in this build.");
        using var z = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress); using var r = new BinaryReader(z);
        if (r.ReadUInt32() != 0x3247_5A4D) throw new InvalidDataException("The gate model is not of a kind this build reads.");   // "MZG2"
        int nv = r.ReadInt32(), ni = r.ReadInt32(), size = r.ReadInt32(), la = r.ReadInt32();
        if (nv <= 0 || ni <= 0 || ni % 3 != 0 || size < 4 || (size & (size - 1)) != 0 || la <= 0) throw new InvalidDataException("The gate model is damaged.");
        Span<float> box = stackalloc float[6]; for (int i = 0; i < 6; i++) box[i] = r.ReadSingle();
        var planes = new ushort[7][]; for (int k = 0; k < 7; k++) { planes[k] = new ushort[nv]; Read(r, System.Runtime.InteropServices.MemoryMarshal.AsBytes(planes[k].AsSpan())); ushort run = 0; for (int i = 0; i < nv; i++) planes[k][i] = run += planes[k][i]; }
        var vertices = new float[nv * 9];
        for (int i = 0; i < nv; i++)
        {
            float ox = (short)planes[3][i] / 32767f, oy = (short)planes[4][i] / 32767f, oz = 1 - MathF.Abs(ox) - MathF.Abs(oy);   // octahedral back to a unit vector
            if (oz < 0) { float fx = (1 - MathF.Abs(oy)) * (ox >= 0 ? 1 : -1), fy = (1 - MathF.Abs(ox)) * (oy >= 0 ? 1 : -1); ox = fx; oy = fy; }
            float len = MathF.Sqrt(ox * ox + oy * oy + oz * oz); int at = i * 9;
            vertices[at] = box[0] + planes[0][i] / 65535f * box[3]; vertices[at + 1] = box[1] + planes[1][i] / 65535f * box[4]; vertices[at + 2] = box[2] + planes[2][i] / 65535f * box[5];
            vertices[at + 3] = ox / len; vertices[at + 4] = oy / len; vertices[at + 5] = oz / len; vertices[at + 6] = planes[5][i] / 65535f; vertices[at + 7] = planes[6][i] / 65535f;
        }
        var indices = new uint[ni]; long prev = 0;
        for (int i = 0; i < ni; i++)
        {
            ulong zz = 0; int shift = 0; while (true) { byte b = r.ReadByte(); zz |= (ulong)(b & 0x7F) << shift; if (b < 0x80) break; shift += 7; if (shift > 63) throw new InvalidDataException("The gate model is damaged."); }
            prev += (long)(zz >> 1) ^ -(long)(zz & 1); if ((ulong)prev >= (ulong)nv) throw new InvalidDataException("The gate model is damaged."); indices[i] = (uint)prev;
        }
        return new GateModel { Vertices = vertices, Indices = indices, Albedo = Chain(r.ReadBytes(la), size), Size = size };
    }

    private static void Read(BinaryReader r, Span<byte> into) { int got = 0; while (got < into.Length) { int k = r.Read(into[got..]); if (k <= 0) throw new EndOfStreamException(); got += k; } }

    private static byte[][] Chain(byte[] jpeg, int size)
    {
        var rgb = Jpeg.Decode(jpeg, out int w, out int h); if (w != size || h != size) throw new InvalidDataException($"A picture of {w} x {h} in a model of {size}.");
        var rgba = new byte[size * size * 4]; for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3) { rgba[i] = rgb[j]; rgba[i + 1] = rgb[j + 1]; rgba[i + 2] = rgb[j + 2]; rgba[i + 3] = 255; }
        return Bc3.MipChain(rgba, size, false, false);
    }
}
