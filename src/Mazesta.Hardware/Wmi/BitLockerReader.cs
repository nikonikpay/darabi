using System.Management; using Mazesta.Core.Health;
namespace Mazesta.Hardware.Wmi;

/// <summary>
/// BitLocker's state of every lettered volume, from Win32_EncryptableVolume (administrator only, which the app is). It only reads: GetConversionStatus,
/// GetProtectionStatus and GetKeyProtectors change nothing. Null when Windows has no BitLocker provider or refuses (a Windows without it, no rights):
/// then nothing is known, which the page says, never "off". A figure one volume would not give stays null and makes that volume Unknown.
/// </summary>
public static class BitLockerReader
{
    private const string Scope = @"root\CIMV2\Security\MicrosoftVolumeEncryption";

    public static IReadOnlyList<BitLockerVolume>? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM Win32_EncryptableVolume", new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30) });
            using var results = searcher.Get();
            var list = new List<BitLockerVolume>();
            foreach (ManagementObject v in results)
                using (v)
                {
                    if (v["DriveLetter"] is not string letter || letter.Length == 0) continue;   // a recovery or system partition with no letter: nobody's data the user can see
                    list.Add(new(letter.ToUpperInvariant(), Out(v, "GetProtectionStatus", "ProtectionStatus"), Out(v, "GetConversionStatus", "ConversionStatus"),
                        Protectors(v, 0)?.Length, Protectors(v, 3) is { } recovery ? recovery.Length > 0 : null));
                }
            return [.. list.OrderBy(x => x.Letter, StringComparer.Ordinal)];
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>A status method's out value, or null when the call fails or returns an error code.</summary>
    private static int? Out(ManagementObject v, string method, string field)
    {
        try { using var r = v.InvokeMethod(method, null, null); return r is not null && Convert.ToUInt32(r["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture) == 0 && r[field] is { } x ? Convert.ToInt32(x, System.Globalization.CultureInfo.InvariantCulture) : null; }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException) { return null; }
    }

    /// <summary>The ids of the volume's key protectors of one type (0 = all, 3 = numerical recovery password), or null when they could not be read.</summary>
    private static string[]? Protectors(ManagementObject v, uint type)
    {
        try
        {
            using var args = v.GetMethodParameters("GetKeyProtectors"); args["KeyProtectorType"] = type;
            using var r = v.InvokeMethod("GetKeyProtectors", args, null);
            return Convert.ToUInt32(r["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture) == 0 ? r["VolumeKeyProtectorID"] as string[] ?? [] : null;
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException) { return null; }
    }
}
