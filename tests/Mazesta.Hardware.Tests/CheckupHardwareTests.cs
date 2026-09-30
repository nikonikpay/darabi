using Mazesta.Hardware.Details; using Xunit; using Xunit.Abstractions;
namespace Mazesta.Hardware.Tests;

/// <summary>The checkup's readers on the real machine: the power plan must read on any Windows; the slot links print for a check against HWiNFO.</summary>
[Trait("Category", "Hardware")]
public class CheckupHardwareTests(ITestOutputHelper output)
{
    [Fact] public void Power_plan_reads()
    {
        var p = PowerSettings.Read();
        output.WriteLine($"mains {p.OnMains} battery {p.HasBattery} max {p.MaxProcessorPercent} boost {p.BoostMode}");
        Assert.NotNull(p.OnMains); Assert.InRange(p.MaxProcessorPercent ?? -1, 0, 100);
    }

    [Fact] public void Slot_links_print()
    {
        var q = new Mazesta.Hardware.Wmi.WmiQuery();
        foreach (var r in q.Query(@"root\cimv2", "SELECT Name,PNPDeviceID FROM Win32_VideoController"))
        {
            string? id = r.GetValueOrDefault("PNPDeviceID") as string;
            var s = PciDevice.Slot(id);
            output.WriteLine($"{r.GetValueOrDefault("Name")}: {(s is null ? "no link" : $"port Gen{s.Port.CurrentGen} x{s.Port.CurrentWidth} (slot max Gen{s.Port.MaxGen} x{s.Port.MaxWidth}), card max Gen{s.CardMaxGen} x{s.CardMaxWidth}")}");
        }
    }
}
