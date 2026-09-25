using System.Text.RegularExpressions; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Windows power plans as <c>powercfg /list</c> prints them. The GUIDs are what matter (names can be localised); switching uses
/// <c>powercfg /setactive</c>, which is reversible by switching back.</summary>
public static partial class PowerPlans
{
    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+?)\)\s*(\*)?\s*$")] private static partial Regex Line();
    public static IReadOnlyList<PowerPlan> Parse(IReadOnlyList<string> output)
        => [.. output.Select(l => Line().Match(l)).Where(m => m.Success).Select(m => new PowerPlan(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), m.Groups[3].Success))];
}

/// <summary>Game Mode and hardware-accelerated GPU scheduling (HAGS) as set in the registry. Null means "not set": Windows then uses its own
/// default, which depends on the version and the GPU - so it is shown as that, not as on or off.</summary>
public sealed record GamingStatus(bool? GameMode, bool? GpuScheduling)
{
    public static GamingStatus Read()
    {
        static int? Dword(RegistryKey root, string path, string name) { using var key = root.OpenSubKey(path); return key?.GetValue(name) is int v ? v : null; }
        int? gameMode = Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
        int? hags = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        return new(gameMode is null ? null : gameMode != 0, hags switch { 2 => true, 1 => false, _ => null });
    }
}
