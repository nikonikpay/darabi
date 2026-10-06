using ComputeSharp; using Xunit;
using Mazesta.Diagnostics.Gpu.Benchmarks; using Mazesta.Diagnostics.Gpu.Scene;
using Vortice.Direct3D12; using Vortice.DXGI; using Vortice.Mathematics;
namespace Mazesta.Diagnostics.Gpu.Tests;

[Trait("Category", "Hardware")]
public class SceneOverlayHardwareTests
{
    [Fact] public void The_readout_lands_in_the_top_left_corner_of_the_frame()
    {
        using var s = new D3D12Session(GraphicsDevice.GetDefault());
        const int W = 640, H = 360;
        // A green frame standing in for the scene, in the present state the overlay expects its back buffers in.
        var target = s.Own(s.Device.CreateCommittedResource(HeapType.Default, ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, W, H, 1, 1, flags: ResourceFlags.AllowRenderTarget),
            ResourceStates.Present, new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0, 1, 0, 1))));
        var rtvHeap = s.Own(s.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 1)));
        s.Device.CreateRenderTargetView(target, null, rtvHeap.GetCPUDescriptorHandleForHeapStart());
        using var overlay = new SceneOverlay(s, [target], H);
        float[] trace = [.. Enumerable.Range(0, 60).Select(i => 90 + 30 * MathF.Sin(i * 0.3f))];
        overlay.Update(new("D3D12 + RT · heavy", 999, 998, 900, 870, trace, [.. trace.Select(v => v * 0.8f)], [new("GPU", SceneOverlay.GpuHue, [new("95", "%"), new("73", "°C"), new("84", "°C HOT"), new("348", "W"), new("52", "% FAN")]),
            new("VRAM", SceneOverlay.GpuHue, [new("9.8 / 24", "GB"), new("88", "°C"), new("1800", "MHz")]), new("CPU", SceneOverlay.CpuHue, [new("34", "%"), new("62", "°C"), new("95", "W"), new("4250", "MHz")]),
            new("RAM", SceneOverlay.RamHue, [new("18.2 / 64", "GB"), new("28", "%")])], "2560 × 1440", "12/60 s · errors 0", false));
        uint pitch = (W * 4 + 255) & ~255u;
        var pixels = s.Read((int)(pitch / 4 * H), (l, readback) =>
        {
            l.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.RenderTarget);
            l.ClearRenderTargetView(rtvHeap.GetCPUDescriptorHandleForHeapStart(), new Color4(0, 1, 0, 1));
            l.ResourceBarrierTransition(target, ResourceStates.RenderTarget, ResourceStates.Present);
            overlay.Draw(l, 0, W, H);
            l.ResourceBarrierTransition(target, ResourceStates.Present, ResourceStates.CopySource);
            l.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm, W, H, 1, pitch) }), 0, 0, 0, new TextureCopyLocation(target, 0));
            l.ResourceBarrierTransition(target, ResourceStates.CopySource, ResourceStates.Present);
        });
        uint At(int x, int y) => pixels[y * (pitch / 4) + x];
        static int G(uint rgba) => (int)(rgba >> 8 & 0xFF);
        Assert.Equal(255, G(At(W - 2, H - 2)));                    // far from the readout: the frame untouched
        var (bw, bh) = overlay.Size;
        Assert.InRange(G(At(bw - 4, bh - 2)), 60, 140);             // under the panel's corner, clear of the text: darkened, still seen through
        Assert.True(bh < H / 2 && bw < W / 2, $"{bw} × {bh}: the readout takes a corner, not the picture");
        if (Environment.GetEnvironmentVariable("MAZESTA_RENDER_DIR") is { Length: > 0 } dir) Png.Write(Path.Combine(dir, "overlay.png"), [.. Enumerable.Range(0, H).SelectMany(y => Enumerable.Range(0, W).Select(x => At(x, y)))], W, H);
        int white = 0; for (int y = 0; y < 60; y++) for (int x = 0; x < 200; x++) if ((At(x, y) & 0xFF) > 200) white++;   // red channel: only text has it
        Assert.True(white > 30, $"{white} text pixels");
    }
}
