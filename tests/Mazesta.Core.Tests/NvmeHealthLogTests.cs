using System.Buffers.Binary; using Xunit; using Mazesta.Core.Providers;
namespace Mazesta.Core.Tests;

public class NvmeHealthLogTests
{
    /// <summary>A log page laid out as the NVMe base specification puts it (figure "SMART / Health Information Log").</summary>
    private static byte[] Page()
    {
        var p = new byte[512];
        p[0] = 0b101;                                               // spare below threshold, reliability degraded
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), 311);   // 311 K = 37.85 °C
        p[3] = 97; p[4] = 10; p[5] = 4;
        void U64(int at, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(at), v);
        U64(32, 2_000_000); U64(48, 1_000_000); U64(112, 812); U64(128, 5321); U64(144, 44); U64(160, 3); U64(176, 17);
        return p;
    }

    [Fact] public void Every_field_is_read_from_its_offset()
    {
        var l = NvmeHealthLog.Parse(Page());
        Assert.Equal(5, l.CriticalWarning); Assert.Equal(37.85, l.TemperatureC!.Value, 2);
        Assert.Equal((97, 10, 4), (l.AvailableSparePercent, l.SpareThresholdPercent, l.PercentageUsed));
        Assert.Equal((2_000_000UL, 1_000_000UL, 812UL, 5321UL, 44UL, 3UL, 17UL), (l.DataUnitsRead, l.DataUnitsWritten, l.PowerCycles, l.PowerOnHours, l.UnsafeShutdowns, l.MediaErrors, l.ErrorLogEntries));
        Assert.Equal(512e9, l.BytesWritten);   // data units are 1000 blocks of 512 bytes
        Assert.Equal(["available spare below threshold", "reliability degraded by media or internal errors"], l.Warnings);
    }

    [Fact] public void A_zero_temperature_is_unreported_and_a_short_page_is_refused()
    {
        var p = Page(); p[1] = p[2] = 0;
        Assert.Null(NvmeHealthLog.Parse(p).TemperatureC);
        Assert.Throws<ArgumentException>(() => NvmeHealthLog.Parse(new byte[100]));
    }
}
