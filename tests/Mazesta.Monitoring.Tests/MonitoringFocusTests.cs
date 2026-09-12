using Xunit;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Monitoring; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Monitoring.Tests;
public class MonitoringFocusTests
{
    [Fact] public void Request_raises_once_with_kinds()
    {
        var f = new MonitoringFocus(); var got = new List<FocusRequest>(); f.FocusRequested += r => got.Add(r);
        f.RequestFocus(new HashSet<HardwareKind> { HardwareKind.Cpu, HardwareKind.Gpu }, "test:power");
        Assert.Single(got); Assert.Contains(HardwareKind.Gpu, got[0].Kinds); Assert.Equal("test:power", got[0].Reason);
    }
}
