using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The readout in the corner of the visual GPU test, laid out like the app's own overlay: a header, a yellow GAME card with the FPS large and
/// AVG / MIN / MS beside it, a GPU card of sensor tiles, and a faint footer. It is drawn by GDI on the CPU into a small bitmap - only when it
/// changes, twice a second - and laid over each frame by one tiny draw, so it costs the GPU nothing measurable. The shader reads a pixel's
/// brightness as its coverage over the dark panel, so a dim fill of a colour reads as a tint of it. It is sized from the render height, so at
/// 4K it reads the same once the frame is scaled down to the window. It is never part of the check frames: those draw the scene alone.
/// </summary>
internal sealed unsafe class SceneOverlay : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Box(uint Left, uint Top, uint Width, uint Height);
    /// <summary>A sensor tile: a label, the value, and its unit (drawn small in the GPU hue).</summary>
    public readonly record struct Tile(string Label, string Value, string Unit);
    /// <summary>What the readout shows; a null number was not measured yet and shows a dash.</summary>
    public sealed record Readout(string Mode, double? Fps, double? Average, double? Minimum, string Card, IReadOnlyList<Tile> Sensors, string Size, string Work, string Status, bool Failing);

    private const uint Game = 0xFDD400, GameEdge = 0x5A4C00, GameTint = 0x1C1800, Hue = 0x5BE37D, HueEdge = 0x1F4A2A, HueTint = 0x0A170D;
    private const uint Label = 0xC3C7CC, Faint = 0x8A9097, White = 0xFFFFFF, Fail = 0xFF4D4D, Rule = 0x2A2D31;

    private readonly D3D12Session _s; private readonly ID3D12Resource[] _targets;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline; private readonly ID3D12DescriptorHeap _rtvHeap; private readonly uint _rtvSize;
    private readonly ID3D12Resource _pixels; private readonly Canvas _c;
    private readonly int _width, _height, _u, _margin;

    public SceneOverlay(D3D12Session s, ID3D12Resource[] targets, int renderHeight)
    {
        _s = s; _targets = targets;
        _u = Math.Max(11, renderHeight / 72); _margin = _u * 4 / 5;
        _width = _u * 25; _height = _u * 21;
        _c = new Canvas(_width, _height);

        byte[] vs = D3D12Session.Shader("SceneOverlayVS");
        _root = s.Own(s.Device.CreateRootSignature(vs));
        var blend = new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha);
        _pipeline = s.Own(s.Device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = _root, VertexShader = vs, PixelShader = D3D12Session.Shader("SceneOverlayPS"), BlendState = blend, DepthStencilState = DepthStencilDescription.None,
            RasterizerState = RasterizerDescription.CullNone, SampleMask = uint.MaxValue, PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [Format.R8G8B8A8_UNorm], SampleDescription = SampleDescription.Default
        }));
        // Read straight from an upload buffer: a few hundred KB, rewritten twice a second while the GPU is idle (every submission is waited for).
        _pixels = s.Buffer((ulong)_width * (ulong)_height * 4, HeapType.Upload, ResourceStates.GenericRead);
        _rtvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, (uint)targets.Length, DescriptorHeapFlags.None, 0)));
        _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        for (int i = 0; i < targets.Length; i++) s.Device.CreateRenderTargetView(targets[i], null, _rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize));
    }

    public void Update(Readout r)
    {
        int u = _u, x0 = _margin, x1 = _width - _margin, y = _margin, pad = u * 2 / 3;
        static string N(double? v, string format) => v is { } d ? d.ToString(format, CultureInfo.InvariantCulture) : "—";
        _c.Clear();
        _c.Fill(0, 0, _width, _height, Rule); _c.Fill(1, 1, _width - 1, _height - 1, 0);   // the hairline frame the app's overlay has

        // Header: the yellow tick, MAZESTA, and the mode on the far side.
        _c.Fill(x0, y + u * 3 / 10, x0 + Math.Max(2, u / 5), y + u, Game);
        _c.Text(x0 + u / 2, y, x1, y + u * 13 / 10, "MAZESTA", 0xA4A8AD, u * 4 / 5, 800);
        _c.Text(x0 + u * 5, y, x1, y + u * 13 / 10, r.Mode, Faint, u * 4 / 5, 400, right: true);
        y += u * 17 / 10;

        // The GAME card: FPS large, AVG / MIN / MS as columns beside it.
        int gh = u * 9 / 2;
        _c.Card(x0, y, x1, y + gh, GameEdge, GameTint, u / 3);
        _c.Pill(x0 + pad, y + pad, "GAME", Game, u * 7 / 10);
        string fps = N(r.Fps, "F0"); int fpsW = _c.Measure(fps, u * 5 / 2, 700);
        _c.Text(x0 + pad, y + u * 3 / 2, x0 + pad + fpsW + u, y + gh - u / 4, fps, White, u * 5 / 2, 700);
        _c.Text(x0 + pad + fpsW + u / 4, y + gh - u * 17 / 10, x1, y + gh, "FPS", Game, u * 7 / 10, 800);
        double? ms = r.Fps is > 0 ? 1000 / r.Fps : null;
        (string Tag, string Value)[] cols = [("AVG", N(r.Average, "F0")), ("MIN", N(r.Minimum, "F0")), ("MS", N(ms, "F2"))];
        int colW = u * 7 / 2, cx = x1 - pad - colW * cols.Length;
        foreach (var (tag, value) in cols)
        {
            _c.Text(cx, y + u * 3 / 2, cx + colW, y + u * 5 / 2, tag, Game, u * 7 / 10, 800);
            _c.Text(cx, y + u * 5 / 2, cx + colW, y + u * 4, value, White, u * 6 / 5, 700);
            cx += colW;
        }
        y += gh + u * 3 / 5;

        // The GPU card: its name, then the sensor tiles two to a row; a sensor the card does not report is left out.
        int rows = Math.Max(1, (r.Sensors.Count + 1) / 2), rowH = u * 2, ch = u * 2 + rows * rowH + u / 3;
        _c.Card(x0, y, x1, y + ch, HueEdge, HueTint, u / 3);
        int pw = _c.Pill(x0 + pad, y + pad, "GPU", Hue, u * 7 / 10);
        _c.Text(x0 + pad + pw + u / 2, y + u / 2, x1 - pad, y + u * 3 / 2, r.Card, Faint, u * 3 / 4, 400, right: true);
        int ty = y + u * 2, half = (x1 - x0 - pad * 2) / 2;
        if (r.Sensors.Count == 0) _c.Text(x0 + pad, ty, x1, ty + rowH, "no GPU sensor readings", Faint, u * 4 / 5, 400, middle: true);
        for (int i = 0; i < r.Sensors.Count; i++)
        {
            var t = r.Sensors[i];
            int tx = x0 + pad + i % 2 * half, row = ty + i / 2 * rowH, tx1 = tx + half - u / 2, unitW = _c.Measure(t.Unit, u * 3 / 5, 700);
            _c.Text(tx, row, tx1, row + rowH, t.Label, Label, u * 4 / 5, 600, middle: true);
            _c.Text(tx, row, tx1 - unitW - u / 5, row + rowH, t.Value, White, u * 6 / 5, 700, right: true, middle: true);
            _c.Text(tx, row + u / 5, tx1, row + rowH, t.Unit, Hue, u * 3 / 5, 700, right: true, middle: true);
            if (i / 2 < rows - 1) _c.Fill(tx, row + rowH - 1, tx1, row + rowH, Rule);
        }
        y += ch + u / 2;

        // Footer: what is drawn and how, then the run's state.
        _c.Text(x0, y, x1, y + u * 6 / 5, $"{r.Size} · {r.Work}", Faint, u * 7 / 10, 400);
        _c.Text(x0, y + u * 6 / 5, x1, y + u * 12 / 5, r.Status, r.Failing ? Fail : Label, u * 7 / 10, r.Failing ? 700 : 400);
        _c.Flush();
        _c.Pixels.CopyTo(_pixels.Map<byte>(0, _width * _height * 4)); _pixels.Unmap(0);
    }

    /// <summary>Lays the readout over back buffer <paramref name="index"/>, which is in the present state before and after.</summary>
    public void Draw(ID3D12GraphicsCommandList4 l, int index, int targetWidth, int targetHeight)
    {
        int left = Math.Max(0, Math.Min(_margin * 2, targetWidth - _width)), top = Math.Max(0, Math.Min(_margin * 2, targetHeight - _height));
        int w = Math.Min(_width, targetWidth - left), h = Math.Min(_height, targetHeight - top);
        var target = _targets[index];
        l.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);
        l.OMSetRenderTargets(_rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(index, _rtvSize));
        l.SetGraphicsRootSignature(_root); l.SetPipelineState(_pipeline); l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        l.RSSetViewport(left, top, w, h); l.RSSetScissorRect(targetWidth, targetHeight);   // the viewport alone bounds the box
        l.SetGraphicsRoot32BitConstants(0, new Box((uint)left, (uint)top, (uint)_width, (uint)_height), 0);
        l.SetGraphicsRootShaderResourceView(1, _pixels.GPUVirtualAddress);
        l.DrawInstanced(3, 1, 0, 0);
        l.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    public void Dispose() => _c.Dispose();
}


/// <summary>A GDI surface over a 32-bit top-down bitmap: text in grey antialiasing (not ClearType, so there are no colour fringes to mistake
/// for coverage), plain and rounded fills. Fonts are made once per size and weight.</summary>
internal sealed unsafe class Canvas : IDisposable
{
    private readonly IntPtr _dc, _bitmap; private readonly uint* _bits; private readonly int _width, _height;
    private readonly Dictionary<(int, int), IntPtr> _fonts = [];

    public Canvas(int width, int height)
    {
        _width = width; _height = height;
        _dc = CreateCompatibleDC(IntPtr.Zero);
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };   // negative height: top-down rows
        _bitmap = CreateDIBSection(_dc, ref info, 0, out var bits, IntPtr.Zero, 0); _bits = (uint*)bits;
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("The test readout bitmap could not be created.");
        SelectObject(_dc, _bitmap);
        SetBkMode(_dc, 1 /* TRANSPARENT */);
    }

    /// <summary>The bitmap as BGRA bytes, rows top to bottom.</summary>
    public ReadOnlySpan<byte> Pixels => new(_bits, _width * _height * 4);

    public void Clear() => new Span<uint>(_bits, _width * _height).Clear();
    public void Flush() => GdiFlush();

    public void Fill(int l, int t, int r, int b, uint rgb)
    {
        var brush = CreateSolidBrush(Ref(rgb)); var rect = new Rect { L = l, T = t, R = r, B = b };
        FillRect(_dc, ref rect, brush); DeleteObject(brush);
    }

    /// <summary>A rounded card: a tinted fill inside a one-pixel edge.</summary>
    public void Card(int l, int t, int r, int b, uint edge, uint tint, int radius)
    {
        var pen = CreatePen(0, 1, Ref(edge)); var brush = CreateSolidBrush(Ref(tint));
        var oldPen = SelectObject(_dc, pen); var oldBrush = SelectObject(_dc, brush);
        RoundRect(_dc, l, t, r, b, radius * 2, radius * 2);
        SelectObject(_dc, oldPen); SelectObject(_dc, oldBrush); DeleteObject(pen); DeleteObject(brush);
    }

    /// <summary>A solid tag with dark text on the colour, as the app's overlay tags; returns its width.</summary>
    public int Pill(int x, int y, string text, uint rgb, int px)
    {
        int w = Measure(text, px, 900) + px, h = px * 3 / 2;
        var brush = CreateSolidBrush(Ref(rgb)); var oldBrush = SelectObject(_dc, brush); var oldPen = SelectObject(_dc, GetStockObject(8 /* NULL_PEN */));
        RoundRect(_dc, x, y, x + w, y + h, px / 2, px / 2);
        SelectObject(_dc, oldBrush); SelectObject(_dc, oldPen); DeleteObject(brush);
        Text(x, y, x + w, y + h, text, 0x0E1012, px, 900, centre: true, middle: true);
        return w;
    }

    public void Text(int l, int t, int r, int b, string text, uint rgb, int px, int weight, bool right = false, bool middle = false, bool centre = false)
    {
        SelectObject(_dc, Font(px, weight)); SetTextColor(_dc, Ref(rgb));
        var rect = new Rect { L = l, T = t, R = r, B = b };
        uint flags = 0x20 /* SINGLELINE */ | 0x800 /* NOPREFIX */ | 0x8000 /* END_ELLIPSIS */ | (right ? 2u : centre ? 1u : 0u) | (middle ? 4u /* VCENTER */ : 0u);
        DrawText(_dc, text, text.Length, ref rect, flags);
    }

    public int Measure(string text, int px, int weight)
    {
        SelectObject(_dc, Font(px, weight));
        return GetTextExtentPoint32(_dc, text, text.Length, out var size) ? size.Cx : 0;
    }

    // The app overlay's face; GDI's font mapper falls back to a sans face on a system without it.
    private IntPtr Font(int px, int weight) => _fonts.TryGetValue((px, weight), out var f) ? f
        : _fonts[(px, weight)] = CreateFont(-px, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, "Bahnschrift");
    private static uint Ref(uint rgb) => (rgb >> 16 & 0xFF) | (rgb & 0xFF00) | (rgb & 0xFF) << 16;   // COLORREF is 0x00BBGGRR

    public void Dispose() { foreach (var f in _fonts.Values) DeleteObject(f); DeleteObject(_bitmap); DeleteDC(_dc); }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant, Colors; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct Extent { public int Cx, Cy; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW")] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(IntPtr dc, int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")] private static extern bool GetTextExtentPoint32(IntPtr dc, string text, int length, out Extent size);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")] private static extern int DrawText(IntPtr dc, string text, int length, ref Rect rect, uint format);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
