using Xunit;
using Mazesta.Diagnostics;
namespace Mazesta.Diagnostics.Tests;

public sealed class FailureAdviceTests
{
    [Theory]
    [InlineData("gpu.scene.d3d", "Win32Exception: The GPU device instance has been suspended. Use GetDeviceRemovedReason", FailureKind.GpuDriver)]
    [InlineData("gpu.scene.rt", "No DirectX 12 hardware GPU is available.", FailureKind.GpuDriver)]
    [InlineData("cpu.matrix", "DXGI_ERROR_DEVICE_REMOVED", FailureKind.GpuDriver)]
    [InlineData("gpu.steady", "something about a shader", FailureKind.Gpu)]
    [InlineData("gpu.vram", "plain", FailureKind.Gpu)]
    [InlineData("bench.gpu.ai", "plain", FailureKind.Gpu)]
    [InlineData("memory.pattern", "mismatch", FailureKind.Memory)]
    [InlineData("storage.sequential", null, FailureKind.Storage)]
    [InlineData("network.speed", "timeout", FailureKind.Network)]
    [InlineData("cpu.linpack", "x", FailureKind.Cpu)]
    [InlineData("windows.sfc", "x", FailureKind.Windows)]
    [InlineData("whatever", "Out of memory", FailureKind.Memory)]
    [InlineData("whatever", "x", FailureKind.Unknown)]
    public void Classifies(string id, string? detail, FailureKind expected) => Assert.Equal(expected, FailureAdvice.Classify(id, detail));
}
