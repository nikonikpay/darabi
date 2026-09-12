using System.Management;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiQuery : IWmiQuery
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (ManagementBaseObject o in results)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in o.Properties) row[p.Name] = p.Value;
            rows.Add(row); o.Dispose();
        }
        return rows;
    }
}
