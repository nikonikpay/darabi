using System.Numerics; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The temperature test's picture (Shaders/Shamseh.hlsl): an eight-point star in turquoise velvet, a gold medallion engraved "مازستا / MAZESTA"
/// at its middle, turning in front of a velvet ground that fills the window. The work is fur drawn as layers blended into a 16-bit frame, which
/// keeps the card's memory as busy as its cores: what draws a card to its power limit with the clock held well under its boost, as a fur test does.
/// Drawn into the window's swap chain with no v-sync, the readout laid over it; <see cref="CheckFrame"/> draws a fixed moment off screen for the run's checks.
/// </summary>
internal sealed unsafe class ShamsehScene : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Constants { public float Ry, Rx, Aspect; public uint Layers, FurLayers; public float Spread, FurLength, Time; public Vector2 Size, Pad2; }
    /// <summary>The ground's layers and the star's: 48 over the screen measured as much as 128 (RTX 3090, 1080p).</summary>
    public const int Layers = 48, FurLayers = 32;
    /// <summary>Texels of the strands' texture a pixel takes: 7.6 held the clock lowest (1270 MHz at 350 W); 1.9 (cached) gave 1660, 15 gave 1650.</summary>
    private const float Spread = 7.6f, FurLength = 0.09f;
    private const int StrandSize = 2048, WordSize = 512;

    private readonly D3D12Session _s; private readonly IDXGISwapChain3 _swap; private readonly ID3D12Resource[] _back; private readonly bool _tearing;
    private readonly ID3D12Resource _light, _depth, _check, _vertices, _indices;
    private readonly ID3D12DescriptorHeap _views, _rtv, _dsv; private readonly uint _rtvSize;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _solid, _fur, _ground, _lens;
    private readonly uint _starIndices, _allIndices;
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

        var (vertices, indices, starIndices) = ShamsehMesh.Build(); _starIndices = starIndices; _allIndices = (uint)indices.Length;
        _vertices = s.Upload(vertices, ResourceStates.AllShaderResource); _indices = s.Upload(indices, ResourceStates.IndexBuffer);
        var strands = new byte[StrandSize * StrandSize * 4]; new Random(7).NextBytes(strands);   // the same strands every run: the check frames compare like with like
        var stranded = Texture(Format.R8G8B8A8_UNorm, StrandSize, StrandSize, strands); var words = Texture(Format.B8G8R8A8_UNorm, WordSize, WordSize, Engraving());

        _light = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource, new ClearValue(Format.R16G16B16A16_Float, Background)));
        _depth = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.D32_Float, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0)));
        _check = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowRenderTarget), ResourceStates.CopySource));

        _views = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 3, DescriptorHeapFlags.ShaderVisible)));
        uint viewSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        ID3D12Resource[] read = [stranded, words, _light];
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
        _solid = Pipeline(solidVs, "ShamsehSolidPS", BlendDescription.Opaque, new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual), Format.R16G16B16A16_Float);
        _ground = Pipeline(D3D12Session.Shader("ShamsehGroundVS"), "ShamsehGroundPS", layered, under, Format.R16G16B16A16_Float);
        _fur = Pipeline(D3D12Session.Shader("ShamsehFurVS"), "ShamsehFurPS", layered, under, Format.R16G16B16A16_Float);
        _lens = Pipeline(D3D12Session.Shader("ShamsehLensVS"), "ShamsehLensPS", BlendDescription.Opaque, DepthStencilDescription.None, Format.R8G8B8A8_UNorm);
    }

    private static readonly Color4 Background = new(0.01f, 0.02f, 0.03f, 1);
    private CpuDescriptorHandle Rtv(int i) => _rtv.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize);

    private ID3D12Resource Texture(Format format, int w, int h, byte[] data)
    {
        var t = _s.Own(_s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(format, (uint)w, (uint)h, 1, 1), ResourceStates.CopyDest));
        uint pitch = ((uint)w * 4 + 255) & ~255u;
        using var staging = _s.Device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(pitch * (ulong)h), ResourceStates.GenericRead);
        var to = staging.Map<byte>(0, (int)(pitch * h)); for (int y = 0; y < h; y++) data.AsSpan(y * w * 4, w * 4).CopyTo(to[(int)(y * pitch)..]); staging.Unmap(0);
        _s.Run(l =>
        {
            l.CopyTextureRegion(new TextureCopyLocation(t, 0), 0, 0, 0, new TextureCopyLocation(staging, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(format, (uint)w, (uint)h, 1, pitch) }));
            l.ResourceBarrierTransition(t, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        });
        return t;
    }

    /// <summary>The medallion's words, white on black, softened a little so their cut edges shade smoothly: a double ring, مازستا, MAZESTA.</summary>
    private static byte[] Engraving()
    {
        using var c = new Canvas(WordSize, WordSize, "Tahoma");
        const uint white = 0xFFFFFF; int m = WordSize / 2;
        c.Card(m - 236, m - 236, m + 236, m + 236, white, white, 236); c.Card(m - 226, m - 226, m + 226, m + 226, 0, 0, 226);
        c.Card(m - 216, m - 216, m + 216, m + 216, white, white, 216); c.Card(m - 211, m - 211, m + 211, m + 211, 0, 0, 211);
        c.Text(0, 140, WordSize, 290, "مازستا", white, 120, 700, centre: true, middle: true);
        c.Text(0, 290, WordSize, 370, "MAZESTA", white, 74, 700, centre: true, middle: true);
        c.Flush();
        var px = c.Pixels.ToArray(); var soft = new byte[px.Length];
        for (int y = 0; y < WordSize; y++)
            for (int x = 0; x < WordSize; x++)
            {
                int sum = 0, n = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) { int xx = x + dx, yy = y + dy; if ((uint)xx < WordSize && (uint)yy < WordSize) { sum += px[(yy * WordSize + xx) * 4 + 2]; n++; } }
                byte v = (byte)(sum / n); int at = (y * WordSize + x) * 4; soft[at] = soft[at + 1] = soft[at + 2] = v; soft[at + 3] = 255;
            }
        return soft;
    }

    private Constants At(float time) => new()
    {
        Ry = time * 0.3f, Rx = 0.12f * MathF.Sin(time * 0.23f), Aspect = Width / (float)Height, Layers = Layers, FurLayers = FurLayers, Spread = Spread, FurLength = FurLength, Time = time, Size = new(Width, Height)
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
        l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList); l.IASetIndexBuffer(new IndexBufferView(_indices.GPUVirtualAddress, _allIndices * 4, Format.R32_UInt));
        l.SetPipelineState(_solid); l.DrawIndexedInstanced(_allIndices, 1, 0, 0, 0);
        l.SetPipelineState(_ground); l.DrawInstanced(3, Layers, 0, 0);   // behind the star: the depth test keeps the ground off it
        l.SetPipelineState(_fur); l.DrawIndexedInstanced(_starIndices, FurLayers, 0, 0, 0);   // the layers in instance order: the inner ones first
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
/// The star's geometry, made here: the eight-point star of two squares (tips every 45 degrees), domed, thicker at the middle, its faces and rim
/// in velvet (material 0); then the gold - the medallion's faces (material 1, which carry the words), its edge, a rim round each face and a
/// piping round the star's outline (material 2). Each vertex is position, normal, uv, material: nine floats. The star's indices come first, so the fur draws them alone.
/// </summary>
internal static class ShamsehMesh
{
    public static (float[] Vertices, uint[] Indices, uint StarIndices) Build()
    {
        var v = new List<float>(); var ix = new List<uint>();
        uint Add(Vector3 p, Vector3 n, Vector2 uv, int m) { v.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z, uv.X, uv.Y, m]); return (uint)(v.Count / 9 - 1); }
        void Quad(uint a, uint b, uint c, uint d) => ix.AddRange([a, b, c, a, c, d]);

        const int Seg = 8, Rings = 16; float tip = 0.6f * MathF.Sqrt(2), inner = 0.6f / MathF.Cos(MathF.PI / 8);
        var outline = new List<Vector2>();
        for (int k = 0; k < 16; k++)
        {
            float a0 = k * MathF.PI / 8, a1 = (k + 1) * MathF.PI / 8, r0 = k % 2 == 1 ? inner : tip, r1 = (k + 1) % 2 == 1 ? inner : tip;
            Vector2 p0 = new(MathF.Cos(a0) * r0, MathF.Sin(a0) * r0), p1 = new(MathF.Cos(a1) * r1, MathF.Sin(a1) * r1);
            for (int s = 0; s < Seg; s++) outline.Add(Vector2.Lerp(p0, p1, s / (float)Seg));
        }
        int n = outline.Count; static float Thick(float r) => 0.19f - 0.07f * r;
        foreach (int side in new[] { 1, -1 })
        {
            var rows = new uint[Rings + 1, n];
            for (int k = 0; k <= Rings; k++)
                for (int j = 0; j < n; j++)
                {
                    var p = outline[j] * (k / (float)Rings); float r = p.Length();
                    var slope = r > 1e-5f ? p / r * 0.07f : Vector2.Zero;   // the dome's slope gives the face its normal
                    rows[k, j] = Add(new(p, side * Thick(r)), Vector3.Normalize(new(slope.X, slope.Y, side)), new(p.X * 0.2f + 0.5f, p.Y * 0.2f + (side > 0 ? 0.5f : 0.1f)), 0);
                }
            for (int k = 0; k < Rings; k++)
                for (int j = 0; j < n; j++)
                {
                    uint a = rows[k, j], b = rows[k, (j + 1) % n], c = rows[k + 1, (j + 1) % n], d = rows[k + 1, j];
                    if (side > 0) Quad(a, d, c, b); else Quad(a, b, c, d);
                }
        }
        {   // the rim, face to face, its normal outward
            float along = 0; var rows = new uint[n + 1, 5];
            for (int j = 0; j <= n; j++)
            {
                Vector2 p = outline[j % n], next = outline[(j + 1) % n], prev = outline[(j - 1 + n) % n], t = next - prev; var normal = Vector3.Normalize(new(t.Y, -t.X, 0)); float h = Thick(p.Length());
                if (j > 0) along += Vector2.Distance(p, outline[j - 1]);
                for (int z = 0; z <= 4; z++) { float zz = -h + 2 * h * z / 4; rows[j, z] = Add(new(p, zz), normal, new(along * 0.2f, zz * 0.2f + 0.8f), 0); }
            }
            for (int j = 0; j < n; j++) for (int z = 0; z < 4; z++) Quad(rows[j, z], rows[j + 1, z], rows[j + 1, z + 1], rows[j, z + 1]);
        }
        uint starIndices = (uint)ix.Count;

        const int Round = 128; const float Radius = 0.35f, Face = 0.23f;
        foreach (int side in new[] { 1, -1 })
        {
            uint centre = Add(new(0, 0, side * Face), new(0, 0, side), Vector2.Zero, 1); var ring = new uint[Round];
            for (int j = 0; j < Round; j++) { float a = j * MathF.Tau / Round; ring[j] = Add(new(MathF.Cos(a) * Radius, MathF.Sin(a) * Radius, side * Face), new(0, 0, side), Vector2.Zero, 1); }
            for (int j = 0; j < Round; j++) ix.AddRange(side > 0 ? [centre, ring[j], ring[(j + 1) % Round]] : [centre, ring[(j + 1) % Round], ring[j]]);
        }
        {
            var rows = new uint[Round + 1, 2];
            for (int j = 0; j <= Round; j++) { float a = j * MathF.Tau / Round; Vector3 d = new(MathF.Cos(a), MathF.Sin(a), 0); rows[j, 0] = Add(d * Radius + new Vector3(0, 0, Face), d, Vector2.Zero, 2); rows[j, 1] = Add(d * Radius - new Vector3(0, 0, Face), d, Vector2.Zero, 2); }
            for (int j = 0; j < Round; j++) Quad(rows[j, 1], rows[j + 1, 1], rows[j + 1, 0], rows[j, 0]);
        }
        void Tube(IReadOnlyList<Vector3> path, float radius, int sides)
        {
            var rows = new uint[path.Count, sides];
            for (int j = 0; j < path.Count; j++)
            {
                Vector3 p = path[j], t = Vector3.Normalize(path[(j + 1) % path.Count] - path[(j - 1 + path.Count) % path.Count]), up = MathF.Abs(t.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
                Vector3 b = Vector3.Normalize(Vector3.Cross(t, up)), u = Vector3.Cross(b, t);
                for (int s = 0; s < sides; s++) { float a = s * MathF.Tau / sides; var d = b * MathF.Cos(a) + u * MathF.Sin(a); rows[j, s] = Add(p + d * radius, d, Vector2.Zero, 2); }
            }
            for (int j = 0; j < path.Count; j++) for (int s = 0; s < sides; s++) Quad(rows[j, s], rows[j, (s + 1) % sides], rows[(j + 1) % path.Count, (s + 1) % sides], rows[(j + 1) % path.Count, s]);
        }
        Vector3[] Circle(float z) => [.. Enumerable.Range(0, Round).Select(j => new Vector3(MathF.Cos(j * MathF.Tau / Round) * Radius, MathF.Sin(j * MathF.Tau / Round) * Radius, z))];
        Tube(Circle(0.21f), 0.045f, 16); Tube(Circle(-0.21f), 0.045f, 16);
        Tube([.. outline.Select(p => new Vector3(p, 0))], 0.03f, 12);
        return ([.. v], [.. ix], starIndices);
    }
}
