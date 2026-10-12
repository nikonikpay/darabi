using Mazesta.Core.Health; using Xunit;
namespace Mazesta.Core.Tests;

public class SmartHealthTests
{
    [Fact] public void Hard_disk_sentinels_own_examples_come_out_the_same()
    {
        Assert.Equal(98, SmartHealth.FromAttributes(new Dictionary<int, ulong> { [5] = 2 }));
        Assert.Equal(95, SmartHealth.FromAttributes(new Dictionary<int, ulong> { [5] = 2, [197] = 5 }));
        Assert.Equal(30, SmartHealth.FromAttributes(new Dictionary<int, ulong> { [198] = 0x200 }));   // the limit caps the loss at 70 %
        Assert.Null(SmartHealth.FromAttributes(new Dictionary<int, ulong> { [9] = 27786 }));
    }
    [Fact] public void Wear_counts_the_ssd_life_used_and_the_bad_sectors_together_and_a_drive_with_neither_has_none()
    {
        Assert.Equal(8, SmartHealth.Wear(8, null));   // an NVMe drive that has used 8 % is 92 % healthy
        Assert.Equal(0, SmartHealth.Wear(null, 100)); // an HDD without bad sectors
        Assert.Equal(11, SmartHealth.Wear(8, 97));    // 92 % x 97 % = 89 %
        Assert.Null(SmartHealth.Wear(null, null));
    }
    [Fact] public void A_blob_is_read_in_twelve_byte_entries_after_a_two_byte_header()
    {
        var blob = new byte[2 + 24]; blob[2] = 5; blob[2 + 5] = 3; blob[14] = 197; blob[14 + 5] = 0x10; blob[14 + 6] = 0x01;
        var raw = SmartHealth.Parse(blob); Assert.Equal(3UL, raw[5]); Assert.Equal(0x110UL, raw[197]);
    }
}
