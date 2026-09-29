using Xunit; using Mazesta.Core.Inventory;
namespace Mazesta.Core.Tests;

public class PciVendorsTests
{
    [Fact] public void The_board_maker_comes_from_the_subsystem_vendor_in_the_pnp_id()   // the owner's RTX 3090, which HWiNFO names "PNY RTX 3090 XLR8"
    {
        var sub = PciVendors.Subsystem(@"PCI\VEN_10DE&DEV_2204&SUBSYS_136A196E&REV_A1\4&2AE1B128&0&0019");
        Assert.Equal(((ushort)0x196E, (ushort)0x136A), sub);
        Assert.Equal("PNY", PciVendors.Name(sub!.Value.Vendor));
    }
    [Fact] public void An_unknown_vendor_or_an_id_without_subsys_is_not_named()
    {
        Assert.Null(PciVendors.Name(0xBEEF));
        Assert.Null(PciVendors.Subsystem(@"USB\VID_046D&PID_C52B")); Assert.Null(PciVendors.Subsystem(null));
    }
}
