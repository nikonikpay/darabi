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
            Width = (uint)width, Height = (uint)height, Format = Format.R8G8B8A8_UNorm, BufferUsage = Usage.RenderTargetOutput, BufferCount = 2, Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore, SampleDescription = SampleDescription.Default, Flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None
        };
        using var swap1 = factory.CreateSwapChainForHwnd(s.Queue, w.Handle, desc);
        factory.MakeWindowAssociation(w.Handle, WindowAssociationFlags.IgnoreAltEnter);
        _swap = swap1.QueryInterface<IDXGISwapChain3>();
        _back = [_swap.GetBuffer<ID3D12Resource>(0), _swap.GetBuffer<ID3D12Resource>(1)];
        Overlay = new SceneOverlay(s, _back, height);
        var garden = new GardenGpu(s, GardenScene.Embedded, custom, customProblem, weather);
        var raster = new GardenRaster(s, garden, width, height, _back, load, rayTraced); _renderer = raster;
        Work = $"{garden.Triangles / 1e6:F2} M triangles · {garden.Instances.Length:N0} objects · {garden.PointLights.Length} lamps"
            + (rayTraced ? $" · ray-traced shadows ({raster.Level.ShadowTaps} a light) and reflections" : $" · shadows {raster.Level.ShadowSize}")
            + (raster.Samples > 1 ? $" · MSAA {raster.Samples}×" : "") + (raster.Level.ReflectionDivisor > 0 ? $" · pool reflection 1/{raster.Level.ReflectionDivisor}" : "");
    }

    /// <summary>Draws the scene at <paramref name="time"/> into the next back buffer, lays the readout over it and shows it, as fast as the
    /// GPU goes (no v-sync).</summary>
    public void Present(float time)
    {
        int index = (int)_swap.CurrentBackBufferIndex;
        _s.Run(l => { _renderer.Draw(l, time, index); Overlay.Draw(_s.List, index, _renderer.OutWidth, _renderer.OutHeight); }, wait: false);
        _swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();   // queued behind the frame: shown the moment the card has drawn it, not after the processor has also finished waiting
        Overlap(time);
    }

    /// <summary>The benchmark's two halves of <see cref="Present"/>: the scene drawn into the next back buffer on its own (the part that is timed),
    /// then the readout laid over it and the picture shown (not timed).</summary>
    public void DrawFrame(float time) { _index = (int)_swap.CurrentBackBufferIndex; _s.Run(l => _renderer.Draw(l, time, _index), wait: false); Overlap(time); }
    /// <summary>While the card draws the frame, the processor steps the weather for the next one (a guess: as far on as this one came after the last),
    /// so the card is not left idle for the simulation; then waits for the card.</summary>
    private void Overlap(float time)
    {
        Garden.Weather?.Prefetch(time + Math.Clamp(time - _last, 0.001f, 0.1f)); _last = time; _s.Finish();
    }
    private float _last;
    public void ShowFrame()
    {
        _s.Run(l => Overlay.Draw(l, _index, _renderer.OutWidth, _renderer.OutHeight));
        _swap.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();
    }
    private int _index;

    /// <summary>Draws the scene at a fixed moment off screen and returns a hash of the exact bits of the picture.</summary>
    public ulong CheckFrame(float time)
    {
        ulong h = 1469598103934665603UL; foreach (uint p in _renderer.Capture(time)) h = (h ^ p) * 1099511628211UL; return h;
    }

    public void Dispose() { _renderer.Dispose(); Overlay.Dispose(); foreach (var b in _back) b.Dispose(); _swap.Dispose(); }
}
