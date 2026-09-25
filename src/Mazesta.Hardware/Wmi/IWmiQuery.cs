namespace Mazesta.Hardware.Wmi;
public interface IWmiQuery
{
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql);

    /// <summary>(The query must select the key properties - SELECT * - or the rows have no path to follow.)</summary>
    /// <summary>Each row of <paramref name="wql"/> with the first object of <paramref name="relatedClass"/> associated with it, or null when there is
    /// none or it cannot be read (some classes, such as the drive reliability counters, are reachable only by association).</summary>
    IReadOnlyList<(IReadOnlyDictionary<string, object?> Row, IReadOnlyDictionary<string, object?>? Related)> QueryWithRelated(string scope, string wql, string relatedClass)
        => [.. Query(scope, wql).Select(r => (r, (IReadOnlyDictionary<string, object?>?)null))];
}
