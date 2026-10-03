using Xunit; using Mazesta.Core.Inventory; using Mazesta.Hardware.Wmi;
namespace Mazesta.Hardware.Tests;

public class BatteryReaderTests
{
    private sealed class Fake(bool withDesign = true, bool any = true) : IWmiQuery
    {
        private const string Name = @"ACPI\PNP0C0A\1_0";
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql)
        {
            Assert.Equal(@"root\wmi", scope);
            if (!any) return [];
            if (wql.Contains("BatteryStaticData")) return withDesign ? [new Dictionary<string, object?> { ["InstanceName"] = Name, ["DesignedCapacity"] = 57000U, ["DeviceName"] = " DELL 0XYZ ", ["ManufactureName"] = "SMP" }] : [];
            if (wql.Contains("BatteryFullChargedCapacity")) return [new Dictionary<string, object?> { ["InstanceName"] = Name, ["FullChargedCapacity"] = 45600U }];
            if (wql.Contains("BatteryCycleCount")) return [new Dictionary<string, object?> { ["InstanceName"] = Name, ["CycleCount"] = 0U }];
            return [new Dictionary<string, object?> { ["InstanceName"] = Name, ["RemainingCapacity"] = 22800U, ["ChargeRate"] = 0, ["DischargeRate"] = 14500, ["Voltage"] = 11400U, ["PowerOnline"] = false, ["Charging"] = false, ["Discharging"] = true }];
        }
    }

    [Fact] public void Health_is_full_capacity_against_design_capacity()
    {
        var b = Assert.Single(BatteryReader.Read(new Fake()));
        Assert.Equal((80.0, 50.0, 11400L, "DELL 0XYZ"), (b.HealthPercent, b.ChargePercent, b.LostMwh, b.Name));
        Assert.Equal((14500L, true, false), (b.RateMw, b.Discharging, b.OnMains));
        Assert.Null(b.CycleCount);   // 0 is what a controller that does not count reports
    }

    [Fact] public void A_battery_without_a_design_capacity_has_no_health()
    {
        var b = Assert.Single(BatteryReader.Read(new Fake(withDesign: false)));
        Assert.Null(b.HealthPercent); Assert.Null(b.LostMwh); Assert.Equal(50.0, b.ChargePercent);
    }

    [Fact] public void A_desktop_has_no_battery() => Assert.Empty(BatteryReader.Read(new Fake(any: false)));

    [Fact] public void A_drain_is_the_energy_that_left_over_the_time_it_took()
    {
        var start = new BatteryInfo(null, null, 57000, 45600, 22800, null, false, true, false, null, null);
        var d = BatteryDrain.Between(start, start with { RemainingMwh = 20400 }, TimeSpan.FromMinutes(10))!;
        Assert.Equal((2400L, 14.4, 190.0), (d.UsedMwh, d.MeanWatts, d.FullChargeMinutes));
        Assert.Null(BatteryDrain.Between(start, start with { RemainingMwh = 22790 }, TimeSpan.FromMinutes(10)));   // too little to measure
        Assert.Null(BatteryDrain.Between(start, start with { RemainingMwh = 23000 }, TimeSpan.FromMinutes(10)));   // it charged
        Assert.Null(BatteryDrain.Between(start with { RemainingMwh = null }, start, TimeSpan.FromMinutes(10)));
    }
}
