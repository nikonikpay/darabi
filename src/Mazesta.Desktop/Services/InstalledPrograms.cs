using Mazesta.Core.Tuning; using Microsoft.Win32;
namespace Mazesta.Desktop.Services;

/// <summary>A program Windows lists as installed, with the executable that is the program itself (what a rule on "this program is running" matches).</summary>
public sealed record InstalledProgram(string Name, string Exe);

/// <summary>
/// The installed programs, from the uninstall lists of the registry (both views of the machine and the user's own): the name Windows shows and the executable - the
/// one its icon names, else the one in its folder whose name is closest to the program's. Programs with no executable found are left out: a rule could not
/// recognise them running. Read when asked, never kept.
/// </summary>
public static class InstalledPrograms
{
    private static readonly string[] Roots = [@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"];

    public static IReadOnlyList<InstalledProgram> Read()
    {
        var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, root) in new[] { (Registry.LocalMachine, Roots[0]), (Registry.LocalMachine, Roots[1]), (Registry.CurrentUser, Roots[0]) })
        {
            try
            {
                using var key = hive.OpenSubKey(root); if (key is null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var k = key.OpenSubKey(sub);
                        if (k?.GetValue("DisplayName") is not string name || name.Length == 0 || k.GetValue("SystemComponent") is 1 || k.GetValue("ParentKeyName") is not null) continue;
                        string? exe = GpuAutoRules.ExeName(k.GetValue("DisplayIcon") as string) ?? FromFolder(name, k.GetValue("InstallLocation") as string);
                        if (exe is null || exe.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || exe.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase)) continue;
                        found.TryAdd(exe, new(name, exe));
                    }
                    catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return [.. found.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The executable in the folder (or its bin folder) whose name shares the most letters with the program's, if it shares any run of four.</summary>
    internal static string? FromFolder(string program, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        try
        {
            static string Key(string s) => new([.. s.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
            string want = Key(program);
            var candidates = new[] { folder, Path.Combine(folder, "bin"), Path.Combine(folder, "bin64") }.Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.exe")).Select(Path.GetFileName).OfType<string>()
                .Where(n => !n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) && !n.Contains("update", StringComparison.OrdinalIgnoreCase) && !n.Contains("crash", StringComparison.OrdinalIgnoreCase)).ToList();
            return candidates.Select(n => (Name: n, Key: Key(Path.GetFileNameWithoutExtension(n)))).Where(c => c.Key.Length >= 3 && (want.Contains(c.Key) || c.Key.Contains(want[..Math.Min(want.Length, 4)])))
                .OrderByDescending(c => c.Key.Length).Select(c => c.Name).FirstOrDefault();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return null; }
    }
}
