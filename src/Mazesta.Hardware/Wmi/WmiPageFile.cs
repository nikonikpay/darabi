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
