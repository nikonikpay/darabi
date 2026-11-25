using Xunit; using Mazesta.Core.Health; using Mazesta.Core.Providers;
namespace Mazesta.Core.Tests;

public class DriveReportTests
{
    // The WD Blue SN570 of the owner's CrystalDiskInfo screenshot: healthy, 61 °C, five error-log entries and 1767 unsafe shutdowns.
    private static NvmeHealthLog Log(byte warning = 0, double temp = 61, int spare = 100, int used = 0, ulong media = 0, uint critMinutes = 0, uint warnMinutes = 0)
        => new(warning, temp, spare, 10, used, 0x2F89DC9, 0x10FFDE1, 2527, 1115, 1767, media, 5, WarningTempMinutes: warnMinutes, CriticalTempMinutes: critMinutes);
    private static DriveHealth Nvme(NvmeHealthLog log, string status = "Healthy", double? warnC = 84) =>
        new("WD Blue SN570 1TB", null, status, log.PercentageUsed, log.TemperatureC, null, null, null, 1115, "NVMe", Media: "SSD", Nvme: log, WarnTempC: warnC, CritTempC: 88);
    private static DriveHealth Hdd(params SmartAttribute[] smart) => new("ST4000DM000", null, "Healthy", null, 35, null, null, null, 40000, "SATA", Media: "HDD", Smart: smart);

    [Fact] public void A_healthy_drive_with_error_log_entries_and_unsafe_shutdowns_is_good_though_warm()
    {
        var r = DriveReport.Of(Nvme(Log(temp: 45)));
        Assert.Equal(DriveLevel.Good, r.Level); Assert.Empty(r.Issues); Assert.Equal(100, r.HealthPercent);
        Assert.All(r.Params, p => Assert.Equal(DriveLevel.Good, p.Level));
        Assert.Contains(r.Params, p => p.Name == "DriveNvme_ErrorLog" && p.Value == "5");
    }

    [Fact] public void An_ssd_is_amber_from_60_and_red_from_70_a_hard_disk_from_50_and_55_and_a_drive_s_own_lower_limit_wins()
    {
        Assert.Equal((60.0, 70.0), DriveReport.TempBands("SSD", 84)); Assert.Equal((50.0, 55.0), DriveReport.TempBands("HDD", null));
        Assert.Equal((60.0, 65.0), DriveReport.TempBands("SSD", 65)); Assert.Equal((53.0, 58.0), DriveReport.TempBands("SSD", 58));
        Assert.Equal(DriveLevel.Watch, DriveReport.Of(Nvme(Log(temp: 61))).TempLevel);
        var hot = DriveReport.Of(Nvme(Log(temp: 72)));
        Assert.Equal(DriveLevel.Fail, hot.TempLevel); Assert.Equal("Hot", hot.Issues[0].Code); Assert.Equal(72, hot.Issues[0].Count);
        Assert.Null(DriveReport.Of(Nvme(Log()) with { Nvme = null, TemperatureC = null }).TempLevel);   // no sensor: no level, never a guess
    }

    [Fact] public void A_critical_warning_bit_spare_below_its_threshold_or_media_errors_fail_and_open_their_row()
    {
        var r = DriveReport.Of(Nvme(Log(warning: 0b101, spare: 4, media: 3, temp: 40)));
        Assert.Equal(DriveLevel.Fail, r.Level);
        Assert.Equal(["NvmeBit0", "NvmeBit2", "MediaErrors"], r.Issues.Select(i => i.Code));
        Assert.Equal("DriveWhy_Spare", r.Params.Single(p => p.Name == "DriveNvme_Spare").Why);
        Assert.Equal(DriveLevel.Fail, r.Params.Single(p => p.Name == "DriveNvme_MediaErrors").Level);
        Assert.True(DriveAttention.Needs(Nvme(Log(media: 1)))); Assert.True(DriveAttention.Needs(Nvme(Log(warning: 4))));
    }

    [Fact] public void The_temperature_warning_bit_and_time_at_the_critical_limit_are_cautions_and_time_at_the_warning_limit_only_marks_its_row()
    {
        Assert.Equal(DriveLevel.Watch, DriveReport.Of(Nvme(Log(warning: 0b10, temp: 40))).Level);
        Assert.Equal("CriticalTempTime", DriveReport.Of(Nvme(Log(critMinutes: 3, temp: 40))).Issues.Single().Code);
        var w = DriveReport.Of(Nvme(Log(warnMinutes: 9, temp: 40)));
        Assert.Equal(DriveLevel.Good, w.Level); Assert.Equal(DriveLevel.Watch, w.Params.Single(p => p.Name == "DriveNvme_WarnTempTime").Level);
    }

    [Fact] public void Wear_is_amber_under_80_percent_life_and_red_under_60()
    {
        Assert.Equal("Wearing", DriveReport.Of(Nvme(Log(used: 25, temp: 40))).Issues.Single().Code);
        var worn = DriveReport.Of(Nvme(Log(used: 99, temp: 40)));
        Assert.Equal(DriveLevel.Fail, worn.Level); Assert.Equal(1, worn.HealthPercent); Assert.Contains(worn.Issues, i => i.Code == "Worn" && i.Count == 1);
    }

    [Fact] public void An_ata_attribute_at_its_threshold_fails_a_hard_disk_s_bad_sectors_are_a_caution_and_an_unknown_id_is_vendor_specific()
    {
        var failing = DriveReport.Of(Hdd(new(5, 9, 9, 0, 10), new(9, 50, 50, 40000, 0)));
        Assert.Equal(DriveLevel.Fail, failing.Level); Assert.Equal(5, failing.Issues.Single().Count);
        Assert.Equal(("05", "DriveAttr_5", "DriveWhy_AtaThreshold"), failing.Params.Select(p => (p.Id, p.Name, p.Why)).First());
        var sectors = DriveReport.Of(Hdd(new SmartAttribute(197, 100, 100, 8, 0)) with { BadSectors = 8 });
        Assert.Equal(DriveLevel.Watch, sectors.Level); Assert.Equal("DriveWhy_Sectors", sectors.Params.Single().Why);
        Assert.Equal("DriveAttr_Vendor", DriveReport.Of(Hdd(new SmartAttribute(233, 100, 100, 5, 0))).Params.Single().Name);
        Assert.False(new SmartAttribute(5, 100, 100, 0, 0).Failing); Assert.False(new SmartAttribute(5, 100, 100, 0, null).Failing);   // threshold 0 or unknown never fails
    }

    [Fact] public void Smart_attributes_are_read_with_their_normalized_values_and_the_drive_s_thresholds()
    {
        byte[] blob = new byte[2 + 24], limits = new byte[2 + 24];
        blob[2] = 5; blob[5] = 100; blob[6] = 99; blob[7] = 3;     // id 5: current 100, worst 99, raw 3
        blob[14] = 194; blob[17] = 70; blob[18] = 60; blob[19] = 38;
        limits[2] = 5; limits[3] = 36;
        var a = SmartHealth.ReadAttributes(blob, limits);
        Assert.Equal(new SmartAttribute(5, 100, 99, 3, 36), a[0]); Assert.Equal(new SmartAttribute(194, 70, 60, 38, null), a[1]);
    }
}
