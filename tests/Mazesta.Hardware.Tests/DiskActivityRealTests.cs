using Mazesta.Hardware.Wmi; using Xunit;
namespace Mazesta.Hardware.Tests;

public sealed class DiskActivityRealTests
{
    [Fact, Trait("Category", "Hardware")]
    public void The_counters_list_the_physical_disks()
    {
        var r = DiskActivity.Sample(new WmiQuery(), 3);
        foreach (var (name, s) in r) Console.WriteLine($"{name}: " + string.Join(" ", s.Select(x => $"{x.BusyPercent:0}%/{x.BytesPerSec / 1e6:0.0}MBs")));
        Assert.NotEmpty(r); Assert.All(r.Values, l => Assert.Equal(3, l.Count));
    }
}
