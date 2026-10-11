using Mazesta.Diagnostics.Gpu.Benchmarks; using ComputeSharp; using Vortice.Direct3D12; using Vortice.DXGI;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>The window's swap chain, the readout over it, and the garden renderer that draws into its back buffers.</summary>
internal sealed class SceneView : IDisposable
{
    private readonly IDXGISwapChain3 _swap; private readonly ID3D12Resource[] _back; private readonly bool _tearing; private readonly D3D12Session _s;
    private readonly GardenRaster _renderer;
    public SceneOverlay Overlay { get; }
    public GardenGpu Garden => _renderer.Scene;
    public GardenRaster Renderer => _renderer;
    public string Work { get; }

    public SceneView(D3D12Session s, TestWindow w, int width, int height, bool rayTraced, uint load, SceneModel? custom, string? customProblem, WeatherLevel? weather = null)
    {
        _s = s;
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
        _back = [.. Enumerable.Range(0, s.Frames + 1).Select(i => _swap.GetBuffer<ID3D12Resource>((uint)i))];   // (one more than the frames in flight: a picture being shown is not one being drawn)
        Overlay = new SceneOverlay(s, _back, height);
        var garden = new GardenGpu(s, GardenScene.Embedded, custom, customProblem, weather, s.Frames);
        var raster = new GardenRaster(s, garden, width, height, _back, load, rayTraced); _renderer = raster;
        Work = $"{garden.Triangles / 1e6:F2} M triangles · {garden.Instances.Length:N0} objects · {garden.PointLights.Length} lamps"
            + (rayTraced ? $" · ray-traced shadows ({raster.Level.ShadowTaps} a light) and reflections" : $" · shadows {raster.Level.ShadowSize}")
            + (raster.Samples > 1 ? $" · MSAA {raster.Samples}×" : "") + (raster.Level.ReflectionDivisor > 0 ? $" · pool reflection 1/{raster.Level.ReflectionDivisor}" : "");
    }

    /// <summary>Draws the scene at <paramref name="time"/> into the next back buffer, lays the readout over it and shows it, as fast as the
    /// GPU goes (no v-sync). With one frame in flight it returns when the card has drawn it; with two, as soon as it is queued (the next call waits for the frame before that).
    /// The card's own time for each frame is kept (<see cref="D3D12Session.GpuSeconds"/>), and so is the processor's (<see cref="D3D12Session.LastRecordSeconds"/>).</summary>
    public void Present(float time)
    {
        int index = (int)_swap.CurrentBackBufferIndex;
        Garden.Weather?.Follow(time);   // (stepped beside the frame, not in the card's way)
        _s.Run(l => { _renderer.Draw(l, time, index); Overlay.Draw(_s.List, index, _renderer.OutWidth, _renderer.OutHeight); }, wait: false, timed: true);
        _swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();   // queued behind the frame: shown the moment the card has drawn it, not after the processor has also finished waiting
        if (_s.Frames == 1) _s.Finish(); else _s.NextSlot();
    }

    /// <summary>Waits for every frame still being drawn (the run is over, or a check frame is to be drawn).</summary>
    public void Drain() => _s.Finish();

    private const int Blocks = 16;
    /// <summary>Draws the scene at a fixed moment off screen and returns the picture boxed down to 16 x 16 mean colours. A hash of the exact bits would call a frame wrong for a handful of edge pixels that the frames in
    /// flight leave a few levels apart (the amortised shadow and surroundings pictures are caught at another step); a corrupt frame moves whole blocks, which <see cref="Same"/> sees.</summary>
    public float[] CheckFrame(float time)
    {
        Drain();
        var px = _renderer.Capture(time); int w = _renderer.OutWidth, h = _renderer.OutHeight; var sum = new double[Blocks * Blocks * 3]; var count = new int[Blocks * Blocks];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            uint p = px[y * w + x]; int b = y * Blocks / h * Blocks + x * Blocks / w; count[b]++;
            sum[b * 3] += p & 255; sum[b * 3 + 1] += (p >> 8) & 255; sum[b * 3 + 2] += (p >> 16) & 255;
        }
        var mean = new float[sum.Length]; for (int i = 0; i < mean.Length; i++) mean[i] = (float)(sum[i] / Math.Max(1, count[i / 3]));
        return mean;
    }
    /// <summary>Whether two check frames are the same picture: no block's mean colour is more than 2 of 255 levels apart.</summary>
    public static bool Same(float[] a, float[] b) { for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 2f) return false; return true; }

    public void Dispose() { Garden.Weather?.Stop(); _renderer.Dispose(); Overlay.Dispose(); foreach (var b in _back) b.Dispose(); _swap.Dispose(); }
}
