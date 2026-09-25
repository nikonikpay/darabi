using System.Management;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiQuery : IWmiQuery
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (ManagementBaseObject o in results) { rows.Add(Properties(o)); o.Dispose(); }
        return rows;
    }

    public IReadOnlyList<(IReadOnlyDictionary<string, object?> Row, IReadOnlyDictionary<string, object?>? Related)> QueryWithRelated(string scope, string wql, string relatedClass)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var rows = new List<(IReadOnlyDictionary<string, object?>, IReadOnlyDictionary<string, object?>?)>();
        foreach (ManagementObject o in results)
        {
            IReadOnlyDictionary<string, object?>? related = null;
            try { using var r = o.GetRelated(relatedClass); foreach (ManagementBaseObject x in r) { related ??= Properties(x); x.Dispose(); } }
            catch (Exception e) when (e is ManagementException or InvalidOperationException or UnauthorizedAccessException) { }   // not readable here (for example without administrator rights): left out, never guessed
            rows.Add((Properties(o), related)); o.Dispose();
        }
        return rows;
    }

    private static Dictionary<string, object?> Properties(ManagementBaseObject o)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in o.Properties) row[p.Name] = p.Value;
        return row;
    }
}
