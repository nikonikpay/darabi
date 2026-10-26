using Mazesta.Core.Gaming; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>A program's own service that Windows starts at boot (not one of Windows', not a driver).</summary>
public sealed record AutoService(string Name, string Display, string? Note, string Path, bool Running);
/// <summary>A program that starts at sign-in: a Run entry or a file in a Startup folder. <paramref name="Id"/> is "source|name", what the page sends back.</summary>
public sealed record AutoStartup(string Id, string Name, string Command, string Source, bool Enabled);

/// <summary>
/// What starts with Windows. Services: only those a program installed (their file is outside the Windows folder and not Microsoft's), switched to Disabled and stopped;
/// the service key keeps its other settings, so putting back is the old start type. Startup programs: the same switch Task Manager has (the StartupApproved keys), so
/// nothing is deleted and Task Manager shows the same state. A name from the page is matched against what is listed before it is used.
/// </summary>
public static class Autorun
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services", Run = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const int Automatic = 2, Disabled = 4;

    public static IReadOnlyList<AutoService> ReadServices(IServiceControl sc)
    {
        var list = new List<AutoService>(); string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var root = Registry.LocalMachine.OpenSubKey(ServicesKey); if (root is null) return list;
        foreach (string name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            if (k?.GetValue("Start") is not int start || start != Automatic || k.GetValue("Type") is not int type || (type & 0x30) == 0) continue;   // automatic Win32 services; drivers are another thing
            string path = ImageFile(k.GetValue("ImagePath") as string);
            if (path.Length == 0 || path.StartsWith(win, StringComparison.OrdinalIgnoreCase) || path.Contains(@"\Microsoft", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Mazesta", StringComparison.OrdinalIgnoreCase)) continue;
            string display = k.GetValue("DisplayName") as string is { Length: > 0 } d && d[0] != '@' ? d : name;
            string? note = k.GetValue("Description") as string is { Length: > 0 } n && n[0] != '@' ? n : null;
            bool running = false; try { running = sc.Query(name)?.Running == true; } catch (System.ComponentModel.Win32Exception) { }
            list.Add(new(name, display, note, path, running));
        }
        return [.. list.OrderBy(s => s.Display, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The program file of an ImagePath: quotes and arguments taken off.</summary>
    public static string ImageFile(string? imagePath)
    {
        string p = (imagePath ?? "").Trim();
        if (p.StartsWith('"')) { int end = p.IndexOf('"', 1); return end > 0 ? p[1..end] : p.Trim('"'); }
        int exe = p.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? p[..(exe + 4)] : p;
    }

    /// <summary>Disables and stops one listed service; null when done, else Windows' own words. A service that is not listed is refused.</summary>
    public static string? DisableService(IServiceControl sc, string name)
    {
        if (!ReadServices(sc).Any(s => s.Name == name)) return "not a listed service";
        string? error = sc.SetStartType(name, Disabled);
        if (error is null) sc.Stop(name);   // a service that will not stop now is still off at the next start
        return error;
    }

    public static string? RestoreService(IServiceControl sc, string name, bool start)
    {
        string? error = sc.SetStartType(name, Automatic);
        return error is null && start ? sc.Start(name) : error;
    }

    public static IReadOnlyList<AutoStartup> ReadStartup()
    {
        var list = new List<AutoStartup>();
        foreach (var (source, hive, sub, approved) in new[] { ("hkcu", Registry.CurrentUser, Run, "Run"), ("hklm", Registry.LocalMachine, Run, "Run"), ("hklm32", Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32") })
        {
            using var k = hive.OpenSubKey(sub); if (k is null) continue;
            using var ap = hive.OpenSubKey(Approved + approved);
            foreach (string name in k.GetValueNames().Where(n => n.Length > 0)) list.Add(new($"{source}|{name}", name, k.GetValue(name)?.ToString() ?? "", source, IsEnabled(ap?.GetValue(name) as byte[])));
        }
        foreach (var (source, hive, folder) in new[] { ("user", Registry.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup)), ("common", Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)) })
        {
            if (!Directory.Exists(folder)) continue;
            using var ap = hive.OpenSubKey(Approved + "StartupFolder");
            foreach (string file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                list.Add(new($"{source}|{Path.GetFileName(file)}", Path.GetFileNameWithoutExtension(file), file, source, IsEnabled(ap?.GetValue(Path.GetFileName(file)) as byte[])));
        }
        return list;
    }

    /// <summary>Task Manager's switch for one startup program; null when done. An id that is not listed is refused.</summary>
    public static string? SetStartup(string id, bool on)
    {
        var item = ReadStartup().FirstOrDefault(s => s.Id == id); if (item is null) return "not a listed startup program";
        var hive = item.Source is "hkcu" or "user" ? Registry.CurrentUser : Registry.LocalMachine;
        string where = Approved + (item.Source switch { "hklm32" => "Run32", "user" or "common" => "StartupFolder", _ => "Run" }), name = id[(id.IndexOf('|') + 1)..];
        try { using var k = hive.CreateSubKey(where); k.SetValue(name, ApprovedValue(on, DateTime.UtcNow), RegistryValueKind.Binary); return null; }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return e.Message; }
    }

    /// <summary>Windows' StartupApproved value: the first byte is even (2) when the program may start and odd (3) when it was switched off, then the time it was switched off.</summary>
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] ApprovedValue(bool on, DateTime utcNow)
    {
        var v = new byte[12]; v[0] = (byte)(on ? 2 : 3);
        if (!on) BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(v, 4);
        return v;
    }
}
