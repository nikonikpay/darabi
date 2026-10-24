using System.Text.RegularExpressions;
namespace Mazesta.Core.Windows;

/// <summary>What a device that can wake the computer is, as far as its name tells (powercfg gives names only). Network is decided by the adapter list, not by a word.</summary>
public enum WakeKind { Input, Network, Usb, Other }

/// <param name="Armed">Windows lets this device wake the computer from sleep (Device Manager's "Allow this device to wake the computer").</param>
public sealed record WakeDevice(string Name, WakeKind Kind, bool Armed);

/// <summary>
/// Reading <c>powercfg</c>'s lists. Its words change with the Windows language, so nothing here relies on them: a device line is trusted only if
/// it is in the "can be programmed" list (a localized "none" line is not), and the wake-timer setting is the last two hex numbers of the query.
/// </summary>
public static class WakeSources
{
    private static readonly Regex Hex = new(@"0x[0-9a-fA-F]{8}", RegexOptions.Compiled);
    private static readonly string[] InputWords = ["keyboard", "mouse", "hid", "touchpad", "pointing", "trackpad", "trackball", "gamepad"];

    public static IReadOnlyList<WakeDevice> Devices(IEnumerable<string> programmable, IEnumerable<string> armed, IEnumerable<string> networkNames)
    {
        var on = new HashSet<string>(armed.Select(Clean), StringComparer.OrdinalIgnoreCase);
        var net = new HashSet<string>(networkNames.Select(Clean), StringComparer.OrdinalIgnoreCase);
        return [.. programmable.Select(Clean).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => new WakeDevice(n, Classify(n, net), on.Contains(n))).OrderBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public static WakeKind Classify(string name, ISet<string> networkNames)
    {
        if (networkNames.Contains(name)) return WakeKind.Network;
        string n = name.ToLowerInvariant();
        if (InputWords.Any(n.Contains)) return WakeKind.Input;
        return n.Contains("usb") ? WakeKind.Usb : WakeKind.Other;
    }

    /// <summary>The "Allow wake timers" setting from <c>powercfg /query … RTCWAKE</c>: 0 off, 1 on, 2 important ones only; null when the output has no such numbers.</summary>
    public static (int? Ac, int? Dc) TimerSetting(IEnumerable<string> lines)
    {
        var all = lines.SelectMany(l => Hex.Matches(l).Select(m => Convert.ToInt32(m.Value, 16))).ToList();
        return all.Count >= 2 ? (all[^2], all[^1]) : (null, null);
    }

    /// <summary>A name powercfg prints may carry stray spaces; the comparison and the command both use the cleaned one.</summary>
    public static string Clean(string name) => name.Trim();
}
