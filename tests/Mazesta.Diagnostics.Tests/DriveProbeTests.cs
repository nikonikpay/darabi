using Mazesta.Diagnostics.Storage; using Xunit;
namespace Mazesta.Diagnostics.Tests;

public sealed class DriveProbeTests
{
    [Fact] public void An_ssd_under_100_MBps_or_5_ms_is_slow() { Assert.False(DriveProbe.IsSlow("SSD", 400, 500, 0.2)); Assert.True(DriveProbe.IsSlow("SSD", 60, 500, 0.2)); Assert.True(DriveProbe.IsSlow("SSD", 400, 500, 8)); }
    [Fact] public void A_hard_disk_is_slow_only_under_30_MBps_or_over_60_ms() { Assert.False(DriveProbe.IsSlow("HDD", 80, 120, 14)); Assert.True(DriveProbe.IsSlow("HDD", 80, 12, 14)); Assert.True(DriveProbe.IsSlow(null, 80, 120, 90)); }
}

public sealed class DriveProbeRealTests
{
    [Fact, Trait("Category", "Hardware")]
    public void The_probe_on_the_system_drive_writes_reads_back_and_leaves_nothing()
    {
        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var r = DriveProbe.Run(root, null);
        Console.WriteLine($"{r.Status} write {r.WriteMBps:0} MB/s read {r.ReadMBps:0} MB/s random {r.RandomMs:0.00} ms {r.Detail}");
        Assert.True(r.Status is ProbeStatus.Ok or ProbeStatus.Slow or ProbeStatus.Skipped, r.Status + " " + r.Detail);
        Assert.False(Directory.Exists(Path.Combine(root, ".mazesta-probe")));
    }
}
