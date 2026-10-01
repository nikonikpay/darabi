using Xunit; using Mazesta.Diagnostics.Gpu.Benchmarks;
namespace Mazesta.Diagnostics.Gpu.Tests;

public class GpuFaultTests
{
    [Fact] public void Only_a_lost_device_or_a_wrong_result_is_the_cards_fault()
    {
        Assert.Equal(GpuFault.Kind.Lost, GpuFault.Of(new GpuLostException("removed")));
        Assert.Equal(GpuFault.Kind.Wrong, GpuFault.Of(new GpuWrongResultException("differs")));
        Assert.Equal(GpuFault.Kind.Lost, GpuFault.Of(new SharpGen.Runtime.SharpGenException(new SharpGen.Runtime.Result(unchecked((int)0x887A0005)))));
        Assert.Equal(GpuFault.Kind.Internal, GpuFault.Of(new InvalidOperationException("The test readout bitmap could not be created.")));
        Assert.Equal(GpuFault.Kind.Internal, GpuFault.Of(new FileNotFoundException("shader")));
        Assert.Equal(GpuFault.Kind.Cancelled, GpuFault.Of(new OperationCanceledException()));
        Assert.Equal(GpuFault.Kind.Unsupported, GpuFault.Of(new GpuUnsupportedException("no DXR")));
    }
}
