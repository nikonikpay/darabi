using System.Globalization; using System.Management;
namespace Mazesta.Hardware.Wmi;

/// <summary>
/// The physical drive's model behind a drive letter (C:\ on "Samsung SSD 980 PRO 1TB"), for the storage benchmark's comparison list: a letter
/// says nothing about the drive, the model does. Windows' storage classes give the partition's disk number and the disk's name; null when
/// the letter is not on one physical disk that Windows names (a network share, a storage space), never a guess.
/// </summary>
public static class DriveModels
{
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public static string? Of(IWmiQuery query, string root)
    {
        try { return Match(query.Query(Storage, "SELECT DiskNumber,DriveLetter FROM MSFT_Partition"), query.Query(Storage, "SELECT DeviceId,FriendlyName FROM MSFT_PhysicalDisk"), root); }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return null; }
    }

    internal static string? Match(IReadOnlyList<IReadOnlyDictionary<string, object?>> partitions, IReadOnlyList<IReadOnlyDictionary<string, object?>> disks, string root)
    {
        if (root.Length == 0) return null;
        char letter = char.ToUpperInvariant(root[0]);
        var numbers = partitions.Where(p => p.TryGetValue("DriveLetter", out var l) && l is not null && char.ToUpperInvariant(Convert.ToChar(l, CultureInfo.InvariantCulture)) == letter)
            .Select(p => Convert.ToString(p.GetValueOrDefault("DiskNumber"), CultureInfo.InvariantCulture)).Distinct().ToList();
        if (numbers.Count != 1) return null;
        var disk = disks.FirstOrDefault(d => Convert.ToString(d.GetValueOrDefault("DeviceId"), CultureInfo.InvariantCulture) == numbers[0]);
        return disk?.GetValueOrDefault("FriendlyName") is string name && name.Trim().Length > 0 ? name.Trim() : null;
    }
}
