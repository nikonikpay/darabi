using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The readout in the corner of the visual GPU test, laid out like the app's own compact overlay: the frame rate large with AVG / MIN / MS
/// beside it, two small traces (the frame rate and its 1% low), then one line for each part - the graphics card, its memory, the processor, the RAM - and a faint footer. It is drawn by GDI on
/// the CPU into a small bitmap - only when it changes, twice a second - and laid over each frame by one tiny draw, so it costs the GPU nothing
/// measurable. The shader reads a pixel's brightness as its coverage over the dark panel, so a dim fill of a colour reads as a tint of it. It
/// is sized from the render height, so at 4K it reads the same once the frame is scaled down to the window. It can be hidden
/// (<see cref="Visible"/>: the test's option, or the O key in its window). It is never part of the check frames: those draw the scene alone.
/// </summary>
internal sealed unsafe class SceneOverlay : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Box(uint Left, uint Top, uint Width, uint Height);
    /// <summary>One figure of a part's line: the value, and its unit (drawn small in the part's hue).</summary>
    public readonly record struct Figure(string Value, string Unit);
    /// <summary>A part's line: its name in its hue, then its figures. Only what was measured is in it.</summary>
    public sealed record Row(string Name, uint Rgb, IReadOnlyList<Figure> Figures);
    /// <summary>What the readout shows; a null number was not measured yet and shows a dash.</summary>
    public sealed record Readout(string Mode, double? Fps, double? Average, double? Minimum, double? Low, IReadOnlyList<float> FpsTrace, IReadOnlyList<float> LowTrace, IReadOnlyList<Row> Rows, string Size, string Status, bool Failing);

    public const uint Game = 0xFDD400, GpuHue = 0x5BE37D, CpuHue = 0x5AA9FF, RamHue = 0xC08CFF, LowHue = 0xFF8A3D;
    private const uint Label = 0xC3C7CC, Faint = 0x8A9097, White = 0xFFFFFF, Fail = 0xFF4D4D, Rule = 0x2A2D31, Well = 0x101214;
    private const int MaxRows = 6;

    private readonly D3D12Session _s; private readonly ID3D12Resource[] _targets;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline; private readonly ID3D12DescriptorHeap _rtvHeap; private readonly uint _rtvSize;
    private readonly ID3D12Resource _pixels; private readonly Canvas _c; private readonly ulong _slotBytes; private readonly bool[] _fresh;
    private readonly int _width, _height, _u, _margin; private int _used;

    /// <summary>Whether the readout is laid over the frames (hidden, it costs nothing at all).</summary>
    public bool Visible { get; set; } = true;
    /// <summary>The readout's size in pixels of the frame, as last drawn.</summary>
    internal (int Width, int Height) Size => (_width, _used);

    public SceneOverlay(D3D12Session s, ID3D12Resource[] targets, int renderHeight)
    {
        _s = s; _targets = targets;
        _u = Math.Max(10, renderHeight / 90); _margin = _u * 7 / 10;
        _width = _u * 23; _height = Height(MaxRows); _used = _height;
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
        // Read straight from an upload buffer: a few hundred KB, rewritten twice a second. Each frame slot has a copy (the card may still be reading the one before),
        // written when that slot's frame is recorded and the picture has changed since it was last written there.
        _slotBytes = ((ulong)_width * (ulong)_height * 4 + 255) & ~255ul; _fresh = new bool[s.Frames];
        _pixels = s.Buffer(_slotBytes * (ulong)s.Frames, HeapType.Upload, ResourceStates.GenericRead);
        _rtvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, (uint)targets.Length, DescriptorHeapFlags.None, 0)));
        _rtvSize = s.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        for (int i = 0; i < targets.Length; i++) s.Device.CreateRenderTargetView(targets[i], null, _rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(i, _rtvSize));
    }

    private int RowHeight => _u * 29 / 20;

    public void Update(Readout r)
    {
        if (!Visible) return;
        int u = _u, x0 = _margin, x1 = _width - _margin, y = _margin;
        static string N(double? v, string format) => v is { } d ? d.ToString(format, CultureInfo.InvariantCulture) : "—";
        var rows = r.Rows.Take(MaxRows).ToList();
        _used = Height(rows.Count);
        _c.Clear();
        _c.Fill(0, 0, _width, _used, Rule); _c.Fill(1, 1, _width - 1, _used - 1, 0);   // the hairline frame the app's overlay has

        // The frame rate, large, with AVG / 1% LOW / MIN / MS as columns beside it.
        string fps = N(r.Fps, "F0"); int big = u * 12 / 5, fpsW = _c.Measure(fps, big, 700);
        _c.Text(x0, y - u / 5, x0 + fpsW + u, y + u * 14 / 5, fps, White, big, 700);
        _c.Text(x0 + fpsW + u / 4, y + u * 3 / 2, x1, y + u * 14 / 5, "FPS", Game, u * 7 / 10, 800);
        double? ms = r.Fps is > 0 ? 1000 / r.Fps : null;
        (string Tag, string Value, uint Hue)[] cols = [("AVG", N(r.Average, "F0"), Game), ("1% LOW", N(r.Low, "F0"), LowHue), ("MIN", N(r.Minimum, "F0"), Game), ("MS", N(ms, "F1"), Game)];
        int colW = u * 16 / 5, cx = x1 - colW * cols.Length;
        foreach (var (tag, value, hue) in cols)
        {
            _c.Text(cx, y + u / 5, cx + colW, y + u * 6 / 5, tag, hue, u * 3 / 5, 800, right: true);
            _c.Text(cx, y + u * 11 / 10, cx + colW, y + u * 13 / 5, value, White, u * 23 / 20, 700, right: true);
            cx += colW;
        }
        y += u * 3;

        // Two small traces of the last half minute, side by side: the frame rate, and the 1% low (the pace of the slowest frames, which is
        // what a stutter shows in). Both on one scale, so the gap between them reads as how uneven the frames are.
        float top = 1; foreach (float v in r.FpsTrace) top = Math.Max(top, v);
        int gw = (x1 - x0 - u / 2) / 2;
        Trace(x0, y, gw, GraphHeight, r.FpsTrace, top, Game, "FPS"); Trace(x1 - gw, y, gw, GraphHeight, r.LowTrace, top, LowHue, "1% LOW");
        y += GraphHeight + u * 3 / 10;

        // A line for each part: its name in its hue, then what was measured of it, each figure with its unit.
        foreach (var row in rows)
        {
            _c.Fill(x0, y, x1, y + 1, Rule);
            _c.Text(x0, y, x0 + u * 3, y + RowHeight, row.Name, row.Rgb, u * 4 / 5, 800, middle: true);
            int fx = x0 + u * 16 / 5;
            foreach (var f in row.Figures)
            {
                int vw = _c.Measure(f.Value, u, 700), uw = _c.Measure(f.Unit, u * 13 / 20, 700);
                if (fx + vw + uw > x1) break;
                _c.Text(fx, y, fx + vw + 2, y + RowHeight, f.Value, White, u, 700, middle: true);
                _c.Text(fx + vw + u / 8, y + u / 6, x1, y + RowHeight, f.Unit, row.Rgb, u * 13 / 20, 700, middle: true);
                fx += vw + uw + u * 3 / 5;
            }
            y += RowHeight;
        }
        _c.Fill(x0, y, x1, y + 1, Rule); y += u * 3 / 10;

        // One footer line: how it is drawn, then the run's state (red once a check frame has failed).
        string mode = $"{r.Mode} · {r.Size} · "; int mw = Math.Min(_c.Measure(mode, u * 13 / 20, 400), (x1 - x0) * 3 / 5);
        _c.Text(x0, y, x0 + mw, y + FootHeight, mode, Faint, u * 13 / 20, 400);
        _c.Text(x0 + mw, y, x1, y + FootHeight, r.Status, r.Failing ? Fail : Label, u * 13 / 20, r.Failing ? 700 : 400);
        _c.Flush();
        Array.Fill(_fresh, true);   // (copied to each slot's buffer when a frame of that slot is recorded)
    }

    private int GraphHeight => _u * 2;
    private int FootHeight => _u * 6 / 5;
    private int Height(int rows) => _margin * 2 + _u * 3 + GraphHeight + _u * 3 / 10 + rows * RowHeight + _u * 3 / 10 + FootHeight;

    /// <summary>A trace in its own small window: a column for each sample, dim under a bright top, the newest at the right edge.</summary>
    private void Trace(int x, int y, int w, int h, IReadOnlyList<float> values, float top, uint hue, string tag)
    {
        _c.Fill(x, y, x + w, y + h, Rule); _c.Fill(x + 1, y + 1, x + w - 1, y + h - 1, Well);
        int inner = h - 4, columns = FramePace.Kept, cw = Math.Max(1, (w - 4) / columns), left = x + w - 2 - cw * values.Count;
        uint dim = (hue >> 16 & 0xFF) / 4 << 16 | (hue >> 8 & 0xFF) / 4 << 8 | (hue & 0xFF) / 4;
        for (int i = Math.Max(0, values.Count - (w - 4) / cw); i < values.Count; i++)
        {
            int bar = Math.Clamp((int)(values[i] / top * inner), 1, inner), bx = left + i * cw, by = y + h - 2 - bar;
            _c.Fill(bx, by, bx + cw, y + h - 2, dim); _c.Fill(bx, by, bx + cw, by + Math.Max(1, _u / 8), hue);
        }
        _c.Text(x + _u / 4, y + 1, x + w, y + _u * 4 / 5, tag, hue, _u * 11 / 20, 800);
    }

    /// <summary>Lays the readout over back buffer <paramref name="index"/>, which is in the present state before and after.</summary>
    public void Draw(ID3D12GraphicsCommandList4 l, int index, int targetWidth, int targetHeight)
    {
        if (!Visible) return;
        int slot = _s.Slot;
        if (_fresh[slot])
        {
            _fresh[slot] = false; int at = (int)_slotBytes * slot;
            _c.Pixels.CopyTo(_pixels.Map<byte>(0, at + _width * _height * 4)[at..]); _pixels.Unmap(0);
        }
        int left = Math.Max(0, Math.Min(_margin * 2, targetWidth - _width)), top = Math.Max(0, Math.Min(_margin * 2, targetHeight - _used));
        int w = Math.Min(_width, targetWidth - left), h = Math.Min(_used, targetHeight - top);
        var target = _targets[index];
        l.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);
        l.OMSetRenderTargets(_rtvHeap.GetCPUDescriptorHandleForHeapStart().Offset(index, _rtvSize));
        l.SetGraphicsRootSignature(_root); l.SetPipelineState(_pipeline); l.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        l.RSSetViewport(left, top, w, h); l.RSSetScissorRect(targetWidth, targetHeight);   // the viewport alone bounds the box
        l.SetGraphicsRoot32BitConstants(0, new Box((uint)left, (uint)top, (uint)_width, (uint)_height), 0);
        l.SetGraphicsRootShaderResourceView(1, _pixels.GPUVirtualAddress + _slotBytes * (ulong)slot);
        l.DrawInstanced(3, 1, 0, 0);
        l.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    public void Dispose() => _c.Dispose();
}


/// <summary>The pace of the last frames: each frame's time goes in, and twice a second the frame rate and the 1% low of the last couple of
/// thousand frames (the rate of the slowest hundredth of them, as benchmarks report it) are kept for the readout's traces. Before a hundred
/// frames have been drawn there is no 1% low: one frame is not a hundredth of anything.</summary>
internal sealed class FramePace
{
    public const int Kept = 60;
    private readonly float[] _times = new float[2048]; private int _count, _next; private float[] _sorted = [];
    private readonly List<float> _fps = [], _low = [];
    public IReadOnlyList<float> Fps => _fps; public IReadOnlyList<float> Lows => _low;
    /// <summary>The 1% low as last sampled, in frames a second.</summary>
    public double? Low { get; private set; }

    public void Frame(double seconds) { _times[_next] = (float)seconds; _next = (_next + 1) % _times.Length; if (_count < _times.Length) _count++; }

    /// <summary>Called when the readout is refreshed, with the frame rate of the half second just passed.</summary>
    public void Sample(double fps)
    {
        if (_count >= 100)
        {
            if (_sorted.Length != _count) _sorted = new float[_count];
            Array.Copy(_times, _sorted, _count); Array.Sort(_sorted);
            int worst = Math.Max(1, _count / 100); double sum = 0;
            for (int i = _count - worst; i < _count; i++) sum += _sorted[i];
            Low = sum > 0 ? worst / sum : null;
        }
        Push(_fps, (float)fps); if (Low is { } low) Push(_low, (float)low);
    }
    private static void Push(List<float> list, float value) { list.Add(value); if (list.Count > Kept) list.RemoveAt(0); }
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
