using System.Text; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

public enum TweakGroup { Essential, Advanced, Preference }
/// <summary>What the registry says of a tweak now. <see cref="Unknown"/>: it is a one-off action or a command whose effect cannot be read back.</summary>
public enum TweakState { Applied, NotApplied, Partial, Unknown }

/// <summary>One registry value a tweak sets. <paramref name="On"/> is written to apply it and <paramref name="Off"/> to undo it; a null
/// <paramref name="Off"/> deletes the value (Windows then uses its default), and <paramref name="Missing"/> is what Windows does when the value
/// is not there, so a machine that never had the value still reads as on or off. <paramref name="DeleteKeyOnUndo"/> removes the whole key.</summary>
public sealed record RegValue(RegistryHive Hive, string Path, string Name, RegistryValueKind Kind, object On, object? Off, object? Missing = null, bool DeleteKeyOnUndo = false);

public sealed record TweakCommand(string File, string Arguments);

/// <summary>
/// A change to Windows as the WinUtil tool offers it: a set of registry values and/or commands, with the way back. A preference is a switch whose
/// state is read from the registry; an essential or advanced tweak is ticked and run, and undone the same way. An action (a restore point,
/// removing temporary files) runs once and has no state and no undo.
/// </summary>
public sealed record Tweak(string Id, TweakGroup Group, string NameKey, string NoteKey, IReadOnlyList<RegValue> Values,
    IReadOnlyList<TweakCommand>? Apply = null, IReadOnlyList<TweakCommand>? Undo = null, bool IsAction = false, bool NeedsRestart = false)
{
    public bool CanUndo => !IsAction && (Values.Count > 0 || Undo is { Count: > 0 });
}

/// <summary>The registry as the tweaks see it: the seam that lets the catalog's reading and writing be tested without touching Windows.</summary>
public interface IRegistryAccess
{
    object? Get(RegistryHive hive, string path, string name);
    void Set(RegistryHive hive, string path, string name, object value, RegistryValueKind kind);
    void Delete(RegistryHive hive, string path, string name);
    void DeleteKey(RegistryHive hive, string path);
}

public sealed class WindowsRegistry : IRegistryAccess
{
    private static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
    public object? Get(RegistryHive hive, string path, string name) { using var root = Root(hive); using var key = root.OpenSubKey(path); return key?.GetValue(name); }
    public void Set(RegistryHive hive, string path, string name, object value, RegistryValueKind kind) { using var root = Root(hive); using var key = root.CreateSubKey(path, writable: true); key.SetValue(name, value, kind); }
    public void Delete(RegistryHive hive, string path, string name) { using var root = Root(hive); using var key = root.OpenSubKey(path, writable: true); key?.DeleteValue(name, throwOnMissingValue: false); }
    public void DeleteKey(RegistryHive hive, string path) { using var root = Root(hive); root.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
}

/// <summary>Reads, applies and undoes tweaks. Every change is read back afterwards by the page, never assumed.</summary>
public sealed class TweakEngine(IRegistryAccess registry, ICommandRunner runner)
{
    public TweakState Read(Tweak t)
    {
        if (t.IsAction || t.Values.Count == 0) return TweakState.Unknown;
        int on = 0, off = 0;
        foreach (var v in t.Values)
        {
            object? now = registry.Get(v.Hive, v.Path, v.Name) ?? v.Missing;
            if (Same(now, v.On)) on++; else off++;
        }
        return on == t.Values.Count ? TweakState.Applied : off == t.Values.Count ? TweakState.NotApplied : TweakState.Partial;
    }

    /// <summary>Applies (or undoes) the tweak. Returns null when every step worked, otherwise what failed, in the words Windows gave.</summary>
    public async Task<string?> RunAsync(Tweak t, bool apply, Action<string>? line, CancellationToken ct)
    {
        var problems = new List<string>();
        foreach (var v in t.Values)
        {
            try
            {
                if (apply) registry.Set(v.Hive, v.Path, v.Name, v.On, v.Kind);
                else if (v.DeleteKeyOnUndo) registry.DeleteKey(v.Hive, v.Path);
                else if (v.Off is null) registry.Delete(v.Hive, v.Path, v.Name);
                else registry.Set(v.Hive, v.Path, v.Name, v.Off, v.Kind);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException) { problems.Add($"{v.Path}\\{v.Name}: {e.Message}"); }
        }
        foreach (var c in (apply ? t.Apply : t.Undo) ?? [])
        {
            var r = await runner.RunAsync(c.File, c.Arguments, WindowsTool.Oem, line, ct).ConfigureAwait(false);
            if (r.ExitCode != 0) problems.Add($"{Path.GetFileName(c.File)} {c.Arguments}: exit code {r.ExitCode}" + (r.Output.Count > 0 ? $" ({r.Output[^1]})" : ""));
        }
        return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }

    internal static bool Same(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (int x, int y) => x == y,
        (string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
        (int x, string y) => x.ToString(System.Globalization.CultureInfo.InvariantCulture) == y,
        (string x, int y) => x == y.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Equals(a, b),
    };
}

/// <summary>
/// The tweaks, chosen from WinUtil's list for what a PC service shop does on a customer's machine: each one is reversible from here except
/// the actions, and nothing that removes Windows components or apps (OneDrive, Edge, AppX) is offered - those cannot be undone by a switch.
/// </summary>
public static class TweakCatalog
{
    private const RegistryHive M = RegistryHive.LocalMachine, U = RegistryHive.CurrentUser;
    private const RegistryValueKind D = RegistryValueKind.DWord, S = RegistryValueKind.String;
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private static readonly string Ps = @"WindowsPowerShell\v1.0\powershell.exe";

    public static readonly IReadOnlyList<Tweak> All =
    [
        // ——— Essential ———
        new("restorePoint", TweakGroup.Essential, "Tweak_RestorePoint", "Tweak_RestorePoint_Note",
            [new(M, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", D, 0, null)],
            Apply: [new(Ps, "-NoProfile -NonInteractive -Command \"Enable-ComputerRestore -Drive $env:SystemDrive; Checkpoint-Computer -Description 'Mazesta' -RestorePointType MODIFY_SETTINGS\"")],
            IsAction: true),
        new("tempFiles", TweakGroup.Essential, "Tweak_TempFiles", "Tweak_TempFiles_Note", [], IsAction: true),
        new("activityHistory", TweakGroup.Essential, "Tweak_ActivityHistory", "Tweak_ActivityHistory_Note",
            [new(M, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", D, 0, null, 1), new(M, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", D, 0, null, 1),
             new(M, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", D, 0, null, 1)]),
        new("consumerFeatures", TweakGroup.Essential, "Tweak_ConsumerFeatures", "Tweak_ConsumerFeatures_Note",
            [new(M, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", D, 1, null, 0)]),
        new("telemetry", TweakGroup.Essential, "Tweak_Telemetry", "Tweak_Telemetry_Note",
            [new(M, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", D, 0, null, 3),
             new(U, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", D, 0, 1, 1),
             new(U, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", D, 0, 1, 1)]),
        new("location", TweakGroup.Essential, "Tweak_Location", "Tweak_Location_Note",
            [new(M, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", S, "Deny", "Allow", "Allow")]),
        new("deliveryOptimization", TweakGroup.Essential, "Tweak_DeliveryOptimization", "Tweak_DeliveryOptimization_Note",
            [new(M, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", D, 0, null, 1)]),
        new("widgets", TweakGroup.Essential, "Tweak_Widgets", "Tweak_Widgets_Note",
            [new(M, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", D, 0, null, 1)], NeedsRestart: true),
        new("gameDvr", TweakGroup.Essential, "Tweak_GameDvr", "Tweak_GameDvr_Note",
            [new(U, @"System\GameConfigStore", "GameDVR_Enabled", D, 0, 1, 1), new(M, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", D, 0, null, 1)]),
        new("endTask", TweakGroup.Essential, "Tweak_EndTask", "Tweak_EndTask_Note",
            [new(U, Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", D, 1, 0, 0)]),
        new("wpbt", TweakGroup.Essential, "Tweak_Wpbt", "Tweak_Wpbt_Note",
            [new(M, @"SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", D, 1, null, 0)], NeedsRestart: true),

        // ——— Advanced: each has a cost the technician should weigh ———
        new("backgroundApps", TweakGroup.Advanced, "Tweak_BackgroundApps", "Tweak_BackgroundApps_Note",
            [new(U, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", D, 1, 0, 0)]),
        new("classicMenu", TweakGroup.Advanced, "Tweak_ClassicMenu", "Tweak_ClassicMenu_Note",
            [new(U, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", S, "", null, DeleteKeyOnUndo: true)], NeedsRestart: true),
        new("copilot", TweakGroup.Advanced, "Tweak_Copilot", "Tweak_Copilot_Note",
            [new(U, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", D, 1, null, 0),
             new(M, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", D, 1, null, 0)], NeedsRestart: true),
        new("storageSense", TweakGroup.Advanced, "Tweak_StorageSense", "Tweak_StorageSense_Note",
            [new(U, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", D, 0, 1, 1)]),
        new("visualFx", TweakGroup.Advanced, "Tweak_VisualFx", "Tweak_VisualFx_Note",
            [new(U, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", D, 2, 0, 0)], NeedsRestart: true),
        new("ipv4First", TweakGroup.Advanced, "Tweak_Ipv4First", "Tweak_Ipv4First_Note",
            [new(M, @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", D, 0x20, null, 0)], NeedsRestart: true),
        new("teredo", TweakGroup.Advanced, "Tweak_Teredo", "Tweak_Teredo_Note", [],
            Apply: [new("netsh.exe", "interface teredo set state disabled")], Undo: [new("netsh.exe", "interface teredo set state default")]),
        new("utcClock", TweakGroup.Advanced, "Tweak_UtcClock", "Tweak_UtcClock_Note",
            [new(M, @"SYSTEM\CurrentControlSet\Control\TimeZoneInformation", "RealTimeIsUniversal", D, 1, null, 0)], NeedsRestart: true),

        // ——— Preferences: switches, read from the registry ———
        new("darkTheme", TweakGroup.Preference, "Pref_DarkTheme", "",
            [new(U, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", D, 0, 1, 1),
             new(U, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", D, 0, 1, 1)]),
        new("bingSearch", TweakGroup.Preference, "Pref_BingSearch", "", [new(U, Search, "BingSearchEnabled", D, 1, 0, 1)]),
        new("fileExtensions", TweakGroup.Preference, "Pref_FileExtensions", "", [new(U, Adv, "HideFileExt", D, 0, 1, 1)]),
        new("hiddenFiles", TweakGroup.Preference, "Pref_HiddenFiles", "", [new(U, Adv, "Hidden", D, 1, 2, 2)]),
        new("mouseAcceleration", TweakGroup.Preference, "Pref_MouseAcceleration", "",
            [new(U, @"Control Panel\Mouse", "MouseSpeed", S, "1", "0", "1"), new(U, @"Control Panel\Mouse", "MouseThreshold1", S, "6", "0", "6"),
             new(U, @"Control Panel\Mouse", "MouseThreshold2", S, "10", "0", "10")]),
        new("stickyKeys", TweakGroup.Preference, "Pref_StickyKeys", "", [new(U, @"Control Panel\Accessibility\StickyKeys", "Flags", S, "510", "58", "510")]),
        new("taskbarCenter", TweakGroup.Preference, "Pref_TaskbarCenter", "", [new(U, Adv, "TaskbarAl", D, 1, 0, 1)]),
        new("taskbarSearch", TweakGroup.Preference, "Pref_TaskbarSearch", "", [new(U, Search, "SearchboxTaskbarMode", D, 1, 0, 1)]),
        new("taskView", TweakGroup.Preference, "Pref_TaskView", "", [new(U, Adv, "ShowTaskViewButton", D, 1, 0, 1)]),
        new("snapping", TweakGroup.Preference, "Pref_Snapping", "", [new(U, @"Control Panel\Desktop", "WindowArrangementActive", S, "1", "0", "1")]),
        new("gameMode", TweakGroup.Preference, "Pref_GameMode", "", [new(U, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", D, 1, 0, 1)]),
        new("verboseLogon", TweakGroup.Preference, "Pref_VerboseLogon", "", [new(M, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", D, 1, 0, 0)]),
        new("bsodDetails", TweakGroup.Preference, "Pref_BsodDetails", "", [new(M, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", D, 1, 0, 0)]),
        new("longPaths", TweakGroup.Preference, "Pref_LongPaths", "", [new(M, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", D, 1, 0, 0)]),
    ];

    /// <summary>WinUtil's two recommended selections: "standard" for a normal clean-up, "minimal" for a machine that should change as little as possible.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Presets = new Dictionary<string, string[]>
    {
        ["standard"] = ["restorePoint", "tempFiles", "activityHistory", "consumerFeatures", "telemetry", "location", "deliveryOptimization", "widgets", "gameDvr", "endTask", "wpbt"],
        ["minimal"] = ["restorePoint", "consumerFeatures", "telemetry", "wpbt"],
    };

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>Removes what can be removed from the user's and Windows' temporary folders; a file in use is skipped, not forced.</summary>
public static class TempFiles
{
    public static (long Files, long Bytes, long Skipped) Clean(IEnumerable<string> folders)
    {
        long files = 0, bytes = 0, skipped = 0;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var f in SafeFiles(folder))
            {
                try { long size = new FileInfo(f).Length; File.Delete(f); files++; bytes += size; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
            }
            foreach (var d in SafeDirs(folder).OrderByDescending(d => d.Length))
                try { Directory.Delete(d); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return (files, bytes, skipped);
    }
    public static IEnumerable<string> Folders() => [Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")];
    private static readonly EnumerationOptions Opt = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    private static IEnumerable<string> SafeFiles(string d) { try { return Directory.EnumerateFiles(d, "*", Opt).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; } }
    private static IEnumerable<string> SafeDirs(string d) { try { return Directory.EnumerateDirectories(d, "*", Opt).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; } }
}

public enum UpdateProfile { Default, Recommended, Disabled, Custom }

/// <summary>
/// WinUtil's three Windows Update profiles as registry policies and service start types. Recommended defers feature updates a year and
/// quality updates four days, keeps drivers out of quality updates and never restarts while someone is signed in (the policies work on Pro,
/// Enterprise and Education; Home ignores the deferrals). Disabled turns automatic updates off and stops and disables the update services; Windows'
/// own repair service can turn them back on. Default removes every one of these policies and puts the services back as Windows ships them.
/// </summary>
public static class UpdateProfiles
{
    private const string Wu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", Au = Wu + @"\AU";
    private static readonly (string Path, string Name, int Value)[] Recommended =
    [
        (Wu, "DeferFeatureUpdates", 1), (Wu, "DeferFeatureUpdatesPeriodInDays", 365), (Wu, "DeferQualityUpdates", 1), (Wu, "DeferQualityUpdatesPeriodInDays", 4),
        (Wu, "ExcludeWUDriversInQualityUpdate", 1), (Au, "NoAutoRebootWithLoggedOnUsers", 1),
    ];
    private static readonly (string Path, string Name, int Value)[] Disable = [(Au, "NoAutoUpdate", 1), (Au, "AUOptions", 1)];

    public static UpdateProfile Read(IRegistryAccess r)
    {
        int? Get(string path, string name) => r.Get(RegistryHive.LocalMachine, path, name) is int v ? v : null;
        if (Get(Au, "NoAutoUpdate") == 1) return UpdateProfile.Disabled;
        bool any = Recommended.Concat(Disable).Any(x => Get(x.Path, x.Name) is not null);
        if (!any) return UpdateProfile.Default;
        return Recommended.All(x => Get(x.Path, x.Name) == x.Value) ? UpdateProfile.Recommended : UpdateProfile.Custom;
    }

    public static async Task<string?> ApplyAsync(UpdateProfile profile, IRegistryAccess r, ICommandRunner runner, Action<string>? line, CancellationToken ct)
    {
        var problems = new List<string>();
        void Try(Action a) { try { a(); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { problems.Add(e.Message); } }
        foreach (var (path, name, _) in Recommended.Concat(Disable)) Try(() => r.Delete(RegistryHive.LocalMachine, path, name));
        var set = profile switch { UpdateProfile.Recommended => Recommended, UpdateProfile.Disabled => Disable, _ => [] };
        foreach (var (path, name, value) in set) Try(() => r.Set(RegistryHive.LocalMachine, path, name, value, RegistryValueKind.DWord));
        TweakCommand[] commands = profile == UpdateProfile.Disabled
            ? [new("sc.exe", "stop wuauserv"), new("sc.exe", "config wuauserv start= disabled"), new("sc.exe", "stop UsoSvc"), new("sc.exe", "config UsoSvc start= disabled")]
            : [new("sc.exe", "config wuauserv start= demand"), new("sc.exe", "config UsoSvc start= delayed-auto")];
        foreach (var c in commands)
        {
            var res = await runner.RunAsync(c.File, c.Arguments, WindowsTool.Oem, line, ct).ConfigureAwait(false);
            // "stop" on a service that is not running answers 1062; that is the state wanted, not a failure.
            if (res.ExitCode != 0 && !(c.Arguments.StartsWith("stop", StringComparison.Ordinal) && res.ExitCode == 1062)) problems.Add($"sc {c.Arguments}: exit code {res.ExitCode}");
        }
        return problems.Count == 0 ? null : string.Join(Environment.NewLine, problems);
    }
}

/// <summary>The DNS servers the active physical adapters use, and switching them to a public resolver or back to what DHCP gives.</summary>
public static class DnsChoice
{
    public static readonly IReadOnlyDictionary<string, string[]> Providers = new Dictionary<string, string[]>
    {
        ["cloudflare"] = ["1.1.1.1", "1.0.0.1"], ["google"] = ["8.8.8.8", "8.8.4.4"], ["quad9"] = ["9.9.9.9", "149.112.112.112"],
        ["shecan"] = ["178.22.122.100", "185.51.200.2"], ["electro"] = ["78.157.42.100", "78.157.42.101"], ["403"] = ["10.202.10.202", "10.202.10.102"],
    };

    /// <summary>The adapters that are up (not loopback, not tunnels) with their IPv4 DNS servers.</summary>
    public static IReadOnlyList<(string Name, string[] Servers)> Current() =>
        [.. System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType is not (System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel))
            .Select(n => (n.Name, n.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray()))];

    /// <summary>The provider whose servers an adapter uses, "auto" when it has none of theirs.</summary>
    public static string Identify(IEnumerable<string> servers) => Providers.FirstOrDefault(p => servers.Any(s => p.Value.Contains(s))).Key ?? "auto";

    /// <summary>netsh commands for one adapter: the provider's two servers, or back to DHCP ("auto").</summary>
    public static IReadOnlyList<TweakCommand> Commands(string adapter, string provider) => provider == "auto" || !Providers.TryGetValue(provider, out var s)
        ? [new("netsh.exe", $"interface ipv4 set dnsservers name=\"{adapter}\" source=dhcp")]
        : [new("netsh.exe", $"interface ipv4 set dnsservers name=\"{adapter}\" source=static address={s[0]} register=primary validate=no"),
           new("netsh.exe", $"interface ipv4 add dnsservers name=\"{adapter}\" address={s[1]} index=2 validate=no")];

    public static async Task<string?> ApplyAsync(string provider, ICommandRunner runner, CancellationToken ct)
    {
        var problems = new StringBuilder();
        foreach (var (name, _) in Current())
            foreach (var c in Commands(name, provider))
            {
                var r = await runner.RunAsync(c.File, c.Arguments, WindowsTool.Oem, null, ct).ConfigureAwait(false);
                if (r.ExitCode != 0) problems.AppendLine($"{name}: {(r.Output.Count > 0 ? r.Output[^1] : $"exit code {r.ExitCode}")}");
            }
        if (problems.Length == 0) await runner.RunAsync("ipconfig.exe", "/flushdns", WindowsTool.Oem, null, ct).ConfigureAwait(false);
        return problems.Length == 0 ? null : problems.ToString().Trim();
    }
}
