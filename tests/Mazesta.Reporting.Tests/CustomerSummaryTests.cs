using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class CustomerSummaryTests
{
    private static readonly StorageDeviceInfo Nvme = new("MSI M390 1TB", "5112 3092 5101", "SSD", "NVMe", 1_000_204_886_016, "EDFM00.1", "Healthy");
    private static readonly StorageDeviceInfo Hdd = new("WDC WD20PURZ", "WD-WX12", "HDD", "SATA", 2_000_398_934_016, "01.01A01", "Healthy");

    [Fact] public void Drive_health_is_matched_to_the_inventory_by_serial_and_a_drive_without_health_still_appears()
    {
        var drives = CustomerSummaryBuilder.Drives([new("MSI M390 1TB", "511230925101", "Warning", 7, 41, 55, 0, 0, 5321)], [Nvme, Hdd]);
        Assert.Equal(2, drives.Count);
        Assert.Equal(new DriveSummary("MSI M390 1TB", "SSD", "NVMe", 1_000_204_886_016, "Warning", 7, 41, 5321), drives[0]);
        Assert.Equal(new DriveSummary("WDC WD20PURZ", "HDD", "SATA", 2_000_398_934_016, "Healthy", null, null, null), drives[1]);   // nothing invented for it
    }

    private static CustomerSummary Summary(params TemperatureSummary[] temps) => new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero), "مازستا", "S-1405-0042", "1.0.0",
        HardwareInventory.Empty with { Cpu = new("AMD Ryzen 9 3950X", Core.Hardware.HardwareVendor.Amd, 16, 32, 3500, "AM4"), Bios = new("American Megatrends Inc.", "F42", new DateTime(2024, 5, 7), "3.3"), Storage = [Nvme] },
        [new("MSI M390 1TB", "SSD", "NVMe", 1_000_204_886_016, "Healthy", 3, null, 812)], temps, ReportVerdict.Passed, new DateTimeOffset(2026, 9, 26, 18, 0, 0, TimeSpan.Zero));

    [Fact] public void The_sheet_is_a5_right_to_left_and_shows_bios_drive_health_and_temperatures()
    {
        string html = SummaryHtml.Write(Summary(new TemperatureSummary("CPU", 46, 78, DateTimeOffset.Now)));
        Assert.Contains("dir=\"rtl\"", html); Assert.Contains("size:A5", html);
        Assert.Contains("F42", html); Assert.Contains(SummaryText.Persian.HealthHealthy, html); Assert.Contains("3%", html); Assert.Contains("46 °C", html); Assert.Contains("78 °C", html);
        Assert.Contains("S-1405-0042", html); Assert.Contains(ReportText.Persian.VerdictPassed, html);
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("http", html.Replace("http-equiv", ""));
    }

    [Fact] public void A_value_that_was_not_measured_says_so_and_is_never_printed_as_zero()
    {
        string html = SummaryHtml.Write(Summary(new TemperatureSummary("GPU", null, null, DateTimeOffset.Now)));
        Assert.Contains(SummaryText.Persian.NotReported, html); Assert.DoesNotContain(">0 °C", html);
    }

    [Fact] public void English_wording_is_left_to_right() => Assert.Contains("dir=\"ltr\"", SummaryHtml.Write(Summary(), wording: SummaryText.English));
}
