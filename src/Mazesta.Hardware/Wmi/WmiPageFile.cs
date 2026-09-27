namespace Mazesta.Hardware.Wmi;

public sealed record PageFileInfo(bool? SystemManaged, IReadOnlyList<(string Path, long? AllocatedMb, long? CurrentMb, long? PeakMb)> Files);

/// <summary>The page file as Windows has it set (read only: Windows Tools shows it; changing it stays in Windows' own dialog).</summary>
public static class WmiPageFile
{
    public static PageFileInfo Read(IWmiQuery query)
    {
        bool? managed = query.Query(@"root\cimv2", "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").Select(r => r.TryGetValue("AutomaticManagedPagefile", out var v) && v is bool b ? b : (bool?)null).FirstOrDefault();
        static long? N(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        var files = query.Query(@"root\cimv2", "SELECT Name,AllocatedBaseSize,CurrentUsage,PeakUsage FROM Win32_PageFileUsage")
            .Select(r => (Path: r.TryGetValue("Name", out var n) ? n as string ?? "?" : "?", N(r, "AllocatedBaseSize"), N(r, "CurrentUsage"), N(r, "PeakUsage"))).ToList();
        return new(managed, files);
    }
}

/// <summary>What the page file will be after the next restart (Win32_PageFileSetting), which can differ from what is in use now.</summary>
public sealed record PageFileSettingInfo(string Path, long InitialMb, long MaximumMb);

/// <summary>Writes a virtual-memory setting through WMI, the way Windows' own dialog does: automatic management on or off, then the per-drive
/// settings replaced. Takes effect after a restart. Needs administrator rights.</summary>
public static class WmiPageFileWriter
{
    public static IReadOnlyList<PageFileSettingInfo> Settings()
    {
        using var searcher = new System.Management.ManagementObjectSearcher(@"root\cimv2", "SELECT Name,InitialSize,MaximumSize FROM Win32_PageFileSetting");
        return [.. searcher.Get().OfType<System.Management.ManagementObject>().Select(o =>
        {
            using (o) return new PageFileSettingInfo(o["Name"] as string ?? "?", Convert.ToInt64(o["InitialSize"] ?? 0, System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt64(o["MaximumSize"] ?? 0, System.Globalization.CultureInfo.InvariantCulture));
        })];
    }

    public static void Apply(Mazesta.Core.Windows.PageFilePlan plan)
    {
        var scope = new System.Management.ManagementScope(@"root\cimv2", new System.Management.ConnectionOptions { EnablePrivileges = true });
        scope.Connect();
        using (var searcher = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
            foreach (var cs in searcher.Get().OfType<System.Management.ManagementObject>())
                using (cs) { cs["AutomaticManagedPagefile"] = plan.Mode == Mazesta.Core.Windows.PageFileMode.SystemManaged; cs.Put(); }
        if (plan.Mode == Mazesta.Core.Windows.PageFileMode.SystemManaged) return;
        // Turning automatic management off leaves (or makes) a setting for the system drive: every setting is removed, then the chosen one made.
        using (var searcher = new System.Management.ManagementObjectSearcher(scope, new System.Management.ObjectQuery("SELECT * FROM Win32_PageFileSetting")))
            foreach (var s in searcher.Get().OfType<System.Management.ManagementObject>()) using (s) s.Delete();
        if (plan.Mode != Mazesta.Core.Windows.PageFileMode.Custom) return;
        using var cls = new System.Management.ManagementClass(scope, new System.Management.ManagementPath("Win32_PageFileSetting"), null);
        using var setting = cls.CreateInstance();
        setting["Name"] = plan.Drive![..1] + @":\pagefile.sys";
        setting["InitialSize"] = (uint)plan.InitialMb; setting["MaximumSize"] = (uint)plan.MaximumMb;
        setting.Put(new System.Management.PutOptions { Type = System.Management.PutType.CreateOnly });
    }
}
