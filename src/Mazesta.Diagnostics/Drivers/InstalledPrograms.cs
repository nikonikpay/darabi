using Microsoft.Win32;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>The programs installed on this computer by the names Windows lists them under (Settings › Apps), read from the uninstall entries
/// of both registry views and of the user. Windows' own components (SystemComponent = 1) and updates are left out.</summary>
public static class InstalledPrograms
{
    private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<string> Read()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view); using var list = root.OpenSubKey(Key);
                if (list is null) continue;
                foreach (string sub in list.GetSubKeyNames())
                {
                    using var k = list.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is not string name || name.Trim().Length == 0) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1 || k.GetValue("ParentKeyName") is string) continue;
                    names.Add(name.Trim());
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return [.. names.Order(StringComparer.OrdinalIgnoreCase)];
    }
}
