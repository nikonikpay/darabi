using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Persistence.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class HealthShareTests
{
    [Fact] public void The_tray_reads_what_the_app_published() { HealthShare.Publish(new HealthSample(61.5, 48, 12, 3600, GpuHotSpotC: 55)); var s = HealthShare.Read(TimeSpan.FromSeconds(30)); Assert.Equal(61.5, s?.CpuTempC); Assert.Equal(55, s?.GpuHotSpotC); }
    [Fact] public void An_old_sample_is_not_read() { HealthShare.Publish(new HealthSample(70, null, null, null)); Assert.Null(HealthShare.Read(TimeSpan.FromSeconds(-1))); }
}
