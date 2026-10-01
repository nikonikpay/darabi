using Microsoft.Win32;
namespace Mazesta.Diagnostics.Drivers;

/// <summary>The programs installed on this computer by the names Windows lists them under (Settings › Apps), read from the uninstall entries
/// of both registry views and of the user. Windows' own components (SystemComponent = 1) and updates are left out.</summary>
public static class InstalledPrograms
{
    private const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<string> Read() => [.. Entries().Select(e => e.Name)];

    /// <summary>The programs with the version each lists (DisplayVersion), when it lists one.</summary>
    public static IReadOnlyList<(string Name, string? Version)> Entries()
    {
        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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
                    names.TryAdd(name.Trim(), (k.GetValue("DisplayVersion") as string)?.Trim() is { Length: > 0 } v ? v : null);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return [.. names.OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase).Select(n => (n.Key, n.Value))];
    }
}
