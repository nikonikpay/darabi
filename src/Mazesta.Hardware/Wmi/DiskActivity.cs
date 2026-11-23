using System.Globalization; using System.Management; using Mazesta.Core.Health;
namespace Mazesta.Hardware.Wmi;

/// <summary>Each physical disk's activity from Windows' performance counters (the same ones Task Manager shows): its instance name ("0 C: D:") with
/// the share of time it was busy and the bytes it moved per second. The formatted counters are worked out between two queries, so the first
/// query of a fresh connection reads zero: <see cref="Sample"/> takes one reading to prime them and keeps the rest.</summary>
public static class DiskActivity
{
    private const string Scope = @"root\cimv2", Wql = "SELECT Name,PercentDiskTime,DiskBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name <> '_Total'";

    public static Dictionary<string, List<DiskActivitySample>> Sample(IWmiQuery query, int samples, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<DiskActivitySample>>();
        try
        {
            query.Query(Scope, Wql);
            for (int i = 0; i < samples; i++)
            {
                if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(1))) break;
                foreach (var r in query.Query(Scope, Wql))
                {
                    string name = Convert.ToString(r.GetValueOrDefault("Name"), CultureInfo.InvariantCulture) ?? ""; if (name.Length == 0) continue;
                    if (!result.TryGetValue(name, out var list)) result[name] = list = [];
                    list.Add(new(Convert.ToDouble(r.GetValueOrDefault("PercentDiskTime") ?? 0, CultureInfo.InvariantCulture), Convert.ToDouble(r.GetValueOrDefault("DiskBytesPersec") ?? 0, CultureInfo.InvariantCulture)));
                }
            }
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return result;
    }
}
