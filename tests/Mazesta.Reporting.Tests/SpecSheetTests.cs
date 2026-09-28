using Mazesta.Core.Inventory; using Mazesta.Reporting; using Xunit;
namespace Mazesta.Reporting.Tests;

public class SpecSheetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact] public void The_sheet_holds_every_section_and_row_offline()
    {
        string html = SpecSheet.WriteHtml([new("پردازنده", [new("نام", "AMD Ryzen 9 3950X"), new("سوکت", "در دسترس نیست")])], ["gpu: timeout"], T0, "مازستا", "1.0.0", "مشخصات سیستم", "پانویس", rtl: true);
        Assert.Contains("dir=\"rtl\"", html); Assert.Contains("size:A4", html);
        Assert.Contains("AMD Ryzen 9 3950X", html); Assert.Contains("در دسترس نیست", html); Assert.Contains("gpu: timeout", html);
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("http", html.Replace("http-equiv", ""));
    }

    [Fact] public void The_json_is_the_whole_inventory_with_nulls_where_nothing_was_reported()
    {
        string json = SpecSheet.WriteJson(HardwareInventory.Empty with { Storage = [new("PLEXTOR PX-256M7VC", "P1", "SSD", "SATA", null, null, "Healthy", 0)] }, T0, "مازستا", "1.0.0");
        Assert.Contains("\"friendlyName\": \"PLEXTOR PX-256M7VC\"", json); Assert.Contains("\"wearPercent\": 0", json); Assert.Contains("\"sizeBytes\": null", json);
        Assert.Contains("مازستا", json);   // Persian stays readable, not \u-escaped
    }
}
