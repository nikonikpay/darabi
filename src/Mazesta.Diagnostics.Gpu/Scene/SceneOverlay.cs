using System.Runtime.InteropServices; using Mazesta.Diagnostics.Gpu.Benchmarks;
using Vortice.Direct3D; using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Scene;

/// <summary>
/// The readout in the corner of the visual GPU test (FPS, the card, its sensors, time left). The text is drawn by GDI
/// on the CPU into a small bitmap - only when it changes, twice a second - and laid over each frame by one tiny draw, so it costs the GPU
/// nothing measurable. It is sized from the render height, so at 4K it reads the same once the frame is scaled down to the window.
/// It is never part of the check frames: those draw the scene alone, and the readout changes every update.
/// </summary>
internal sealed unsafe class SceneOverlay : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Box(uint Left, uint Top, uint Width, uint Height);
    public readonly record struct Line(string Text, uint Rgb = 0xFFFFFF);
    public const int Lines = 7;

    private readonly D3D12Session _s; private readonly ID3D12Resource[] _targets;
    private readonly ID3D12RootSignature _root; private readonly ID3D12PipelineState _pipeline; private readonly ID3D12DescriptorHeap _rtvHeap; private readonly uint _rtvSize;
    private readonly ID3D12Resource _pixels; private readonly TextBitmap _text;
    private readonly int _width, _height, _lineHeight, _margin;

    public SceneOverlay(D3D12Session s, ID3D12Resource[] targets, int renderHeight)
    {
        _s = s; _targets = targets;
        int px = Math.Max(14, renderHeight / 48);
        _lineHeight = px * 4 / 3; _margin = px / 2; _width = px * 32; _height = _lineHeight * Lines + _margin * 2;
        _text = new TextBitmap(_width, _height, px);

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

    /// <summary>Redraws the text; the first line is the heading.</summary>
    public void Update(IReadOnlyList<Line> lines)
    {
        _text.Draw(lines.Take(Lines).Select((line, i) => (line.Text, line.Rgb, _margin, _margin + i * _lineHeight - (i == 0 ? _lineHeight / 8 : 0), i == 0)));
        _text.Pixels.CopyTo(_pixels.Map<byte>(0, _width * _height * 4)); _pixels.Unmap(0);
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

    public void Dispose() => _text.Dispose();
}


/// <summary>Lines of text drawn by GDI into a 32-bit top-down bitmap, white-on-black style: each pixel's brightness is the text's coverage
/// (grey antialiasing, not ClearType, so there are no colour fringes to mistake for coverage).</summary>
internal sealed unsafe class TextBitmap : IDisposable
{
    private readonly IntPtr _dc, _bitmap, _font, _bold; private readonly uint* _bits; private readonly int _width, _height;

    public TextBitmap(int width, int height, int px)
    {
        _width = width; _height = height;
        _dc = CreateCompatibleDC(IntPtr.Zero);
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };   // negative height: top-down rows
        _bitmap = CreateDIBSection(_dc, ref info, 0, out var bits, IntPtr.Zero, 0); _bits = (uint*)bits;
        if (_bitmap == IntPtr.Zero) throw new InvalidOperationException("The test readout bitmap could not be created.");
        SelectObject(_dc, _bitmap);
        _font = CreateFont(-px, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, "Segoe UI");
        _bold = CreateFont(-px * 5 / 4, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
        SetBkMode(_dc, 1 /* TRANSPARENT */);
    }

    /// <summary>The bitmap as BGRA bytes, rows top to bottom.</summary>
    public ReadOnlySpan<byte> Pixels => new(_bits, _width * _height * 4);

    public void Draw(IEnumerable<(string Text, uint Rgb, int X, int Y, bool Bold)> lines)
    {
        new Span<uint>(_bits, _width * _height).Clear();
        foreach (var (text, rgb, x, y, bold) in lines)
        {
            SelectObject(_dc, bold ? _bold : _font);
            SetTextColor(_dc, (rgb >> 16 & 0xFF) | (rgb & 0xFF00) | (rgb & 0xFF) << 16);   // COLORREF is 0x00BBGGRR
            TextOut(_dc, x, y, text, text.Length);
        }
        GdiFlush();
    }

    public void Dispose() { DeleteObject(_font); DeleteObject(_bold); DeleteObject(_bitmap); DeleteDC(_dc); }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant, Colors; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW")] private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "TextOutW")] private static extern bool TextOut(IntPtr dc, int x, int y, string text, int length);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
