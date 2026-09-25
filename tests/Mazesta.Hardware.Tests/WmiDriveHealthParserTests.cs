using Xunit; using Mazesta.Hardware.Wmi;
namespace Mazesta.Hardware.Tests;

public class WmiDriveHealthParserTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string, object?)[] p) => p.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact] public void Health_codes_and_counters_map_and_unreported_values_stay_null()
    {
        var parsed = WmiDriveHealthParser.Parse([
            (Row(("FriendlyName", "MSI M390 1TB"), ("SerialNumber", "S1 "), ("MediaType", (ushort)4), ("HealthStatus", (ushort)0)),
             Row(("Wear", (byte)3), ("Temperature", (byte)38), ("TemperatureMax", (byte)61), ("ReadErrorsUncorrected", 0UL), ("WriteErrorsUncorrected", 0UL), ("PowerOnHours", 1234U))),
            (Row(("FriendlyName", "WDC HDD"), ("MediaType", (ushort)3), ("HealthStatus", (ushort)1)), Row(("Wear", (byte)0), ("Temperature", (byte)0))),
            (Row(("FriendlyName", "USB stick"), ("HealthStatus", (ushort)5)), null)]);
        Assert.Equal(new Mazesta.Core.Providers.DriveHealth("MSI M390 1TB", "S1", "Healthy", 3, 38, 61, 0, 0, 1234), parsed[0]);
        Assert.Equal(("Warning", (int?)null, (double?)null), (parsed[1].Status, parsed[1].WearPercent, parsed[1].TemperatureC));   // HDD wear and 0 °C are "not reported"
        Assert.Equal("Unknown", parsed[2].Status); Assert.Null(parsed[2].PowerOnHours);
    }
}

/// <summary>Reads the real drives (Category=Hardware): Windows must give every physical drive a health status. The reliability counters need
/// administrator rights, so without them they are simply absent - never zeros.</summary>
[Trait("Category", "Hardware")]
public class WmiDriveHealthHardwareTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact] public void Every_physical_drive_has_a_health_status()
    {
        var drives = new WmiDriveHealthProvider(new WmiQuery()).Read();
        foreach (var d in drives) output.WriteLine(d.ToString());
        Assert.NotEmpty(drives); Assert.All(drives, d => Assert.NotNull(d.Status));
    }
}

public class WmiPageFileTests
{
    private sealed class Fake : IWmiQuery
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql) => wql.Contains("Win32_ComputerSystem")
            ? [new Dictionary<string, object?> { ["AutomaticManagedPagefile"] = true }]
            : [new Dictionary<string, object?> { ["Name"] = @"C:\pagefile.sys", ["AllocatedBaseSize"] = 16384U, ["CurrentUsage"] = 120U, ["PeakUsage"] = 900U }];
    }
    [Fact] public void Page_file_setting_and_usage_are_read()
    {
        var p = WmiPageFile.Read(new Fake());
        Assert.True(p.SystemManaged); Assert.Equal((@"C:\pagefile.sys", 16384L, 120L, 900L), (p.Files[0].Path, p.Files[0].AllocatedMb, p.Files[0].CurrentMb, p.Files[0].PeakMb));
    }
    [Fact] [Trait("Category", "Hardware")] public void This_machines_page_file_reads() => Assert.NotNull(WmiPageFile.Read(new WmiQuery()).SystemManaged);
}
