using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>
/// The two entries the app can put in Explorer's right-click menu, in the current user's own part of the registry (no administrator needed to add them, and nothing of the
/// system's is changed): "block in firewall" on programs (.exe) and "exclude from antivirus" on folders and files. Each runs this app with an argument and the file's path; the app asks
/// Windows for the administrator right it needs (the manifest does) and says what it did.
/// </summary>
public static class ShieldShellMenu
{
    public const string FirewallArgument = "--fw-block", AntivirusArgument = "--av-exclude";
    private const string FirewallKey = @"Software\Classes\exefile\shell\MazestaFirewallBlock", AntivirusKey = @"Software\Classes\Directory\shell\MazestaAvExclude";

    /// <summary>Where else the same entry sits: a shortcut to a program (the app reads the shortcut's target), the empty space of an open folder (%V is that folder) and a drive.</summary>
    private static (string Key, string Arg)[] Extras(bool firewall) => firewall
        ? [(@"Software\Classes\lnkfile\shell\MazestaFirewallBlock", "%1")]
        : [(@"Software\Classes\Directory\Background\shell\MazestaAvExclude", "%V"), (@"Software\Classes\*\shell\MazestaAvExclude", "%1"), (@"Software\Classes\Drive\shell\MazestaAvExclude", "%1")];

    public static string Command(bool firewall, string exe, string arg = "%1") => $"\"{exe}\" {(firewall ? FirewallArgument : AntivirusArgument)} \"{arg}\"";

    /// <summary>What is registered now: the label and the program the entry starts, or null when there is no entry.</summary>
    public static (string Label, string Command)? Registered(bool firewall)
    {
        using var key = Registry.CurrentUser.OpenSubKey(firewall ? FirewallKey : AntivirusKey); if (key is null) return null;
        using var command = key.OpenSubKey("command");
        return (key.GetValue("") as string ?? "", command?.GetValue("") as string ?? "");
    }

    /// <summary>Whether the entry exists and starts this very program with this label (a moved or renamed copy, or another language, makes it stale).</summary>
    public static bool IsCurrent(bool firewall, string exe, string label) => Registered(firewall) is { } r && r.Command == Command(firewall, exe) && r.Label == label
        && Extras(firewall).All(x => { using var c = Registry.CurrentUser.OpenSubKey(x.Key + @"\command"); return c?.GetValue("") as string == Command(firewall, exe, x.Arg); });

    public static void Set(bool firewall, bool on, string exe, string label)
    {
        string main = firewall ? FirewallKey : AntivirusKey;
        foreach (var (path, arg) in Extras(firewall).Prepend((main, "%1")))
        {
            if (!on) { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); continue; }
            using var key = Registry.CurrentUser.CreateSubKey(path); key.SetValue("", label); key.SetValue("Icon", $"\"{exe}\",0");
            using var command = key.CreateSubKey("command"); command.SetValue("", Command(firewall, exe, arg));
        }
    }

    /// <summary>Takes both entries out (the uninstaller's call).</summary>
    public static void RemoveAll() { foreach (bool fw in new[] { true, false }) try { Set(fw, false, "", ""); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { } }
}
