using Mazesta.Hardware.Wmi; using Xunit;
namespace Mazesta.Hardware.Tests;

public class DriveModelsTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string K, object? V)[] kv) => kv.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Disks = [Row(("DeviceId", "0"), ("FriendlyName", "Samsung SSD 980 PRO 1TB")), Row(("DeviceId", "1"), ("FriendlyName", "WDC WD20EZRZ"))];

    [Fact] public void A_letter_names_the_drive_it_sits_on()
        => Assert.Equal("WDC WD20EZRZ", DriveModels.Match([Row(("DiskNumber", 0u), ("DriveLetter", 'C')), Row(("DiskNumber", 1u), ("DriveLetter", 'D'))], Disks, @"d:\"));
    [Fact] public void A_letter_spread_over_two_disks_or_on_none_names_nothing()
    {
        Assert.Null(DriveModels.Match([Row(("DiskNumber", 0u), ("DriveLetter", 'E')), Row(("DiskNumber", 1u), ("DriveLetter", 'E'))], Disks, @"E:\"));
        Assert.Null(DriveModels.Match([Row(("DiskNumber", 0u), ("DriveLetter", '\0'))], Disks, @"C:\"));
    }
}
