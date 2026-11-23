using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public sealed class DiskStallTests
{
    private static DiskActivitySample[] Run(int n, double busy, double bytes) => [.. Enumerable.Repeat(new DiskActivitySample(busy, bytes), n)];
    [Fact] public void Busy_and_idle_for_every_sample_is_stalled() => Assert.True(DiskStall.IsStalled(Run(6, 100, 200_000)));
    [Fact] public void A_busy_disk_that_moves_data_is_just_working() => Assert.False(DiskStall.IsStalled(Run(6, 100, 400_000_000)));
    [Fact] public void An_idle_disk_is_not_stalled() => Assert.False(DiskStall.IsStalled(Run(6, 3, 0)));
    [Fact] public void One_calm_sample_or_too_few_samples_is_not_a_stall() { Assert.False(DiskStall.IsStalled([.. Run(5, 100, 0), new(10, 0)])); Assert.False(DiskStall.IsStalled(Run(4, 100, 0))); }
}
