using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// The two entries the app can put in Explorer's right-click menu, in the current user's own part of the registry (no administrator needed to add them, and nothing of the
/// system's is changed): "block in firewall" on programs (.exe) and "exclude from antivirus" on folders. Each runs this app with an argument and the file's path; the app asks
/// Windows for the administrator right it needs (the manifest does) and says what it did.
/// </summary>
public static class ShieldShellMenu
{
    public const string FirewallArgument = "--fw-block", AntivirusArgument = "--av-exclude";
    private const string FirewallKey = @"Software\Classes\exefile\shell\MazestaFirewallBlock", AntivirusKey = @"Software\Classes\Directory\shell\MazestaAvExclude";

    public static string Command(bool firewall, string exe) => $"\"{exe}\" {(firewall ? FirewallArgument : AntivirusArgument)} \"%1\"";

    /// <summary>What is registered now: the label and the program the entry starts, or null when there is no entry.</summary>
    public static (string Label, string Command)? Registered(bool firewall)
    {
        using var key = Registry.CurrentUser.OpenSubKey(firewall ? FirewallKey : AntivirusKey); if (key is null) return null;
        using var command = key.OpenSubKey("command");
        return (key.GetValue("") as string ?? "", command?.GetValue("") as string ?? "");
    }

    /// <summary>Whether the entry exists and starts this very program with this label (a moved or renamed copy, or another language, makes it stale).</summary>
    public static bool IsCurrent(bool firewall, string exe, string label) => Registered(firewall) is { } r && r.Command == Command(firewall, exe) && r.Label == label;

    public static void Set(bool firewall, bool on, string exe, string label)
    {
        string path = firewall ? FirewallKey : AntivirusKey;
        if (!on) { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); return; }
        using var key = Registry.CurrentUser.CreateSubKey(path); key.SetValue("", label); key.SetValue("Icon", $"\"{exe}\",0");
        using var command = key.CreateSubKey("command"); command.SetValue("", Command(firewall, exe));
    }

    /// <summary>Takes both entries out (the uninstaller's call).</summary>
    public static void RemoveAll() { foreach (bool fw in new[] { true, false }) try { Set(fw, false, "", ""); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { } }
}
