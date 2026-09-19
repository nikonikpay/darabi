using System.Text.RegularExpressions;
namespace Mazesta.Core.Hardware;

/// <summary>Where a sensor sits inside its hardware node: which kind, and which family of same-named
/// sensors ("Core" for "Core #1"…"Core #16"). An empty <see cref="Family"/> is the kind's generic bucket
/// ("Temperatures", "Fans").</summary>
public readonly record struct SensorSection(SensorKind Kind, string Family);

/// <summary>
/// Groups a node's sensors the way HWiNFO does: sensors that are numbered instances of one thing ("Core
/// #1 … Core #16" clocks) become one section, related loose sensors of the same kind share the kind's
/// bucket, and a sensor with nothing to be grouped with stays flat. Pure name/kind logic - the display
/// text of a section (and its language) is the UI's job.
/// </summary>
public static partial class SensorGrouping
{
    [GeneratedRegex(@"#\d+")] private static partial Regex InstanceMarker();
    [GeneratedRegex(@"^([A-Za-z]+)\d+$")] private static partial Regex LettersThenDigits();
    /// <summary>A named family ("Core" ×16) is a section of its own only when it is big enough to be worth a header; two or three related sensors read better inside the kind's bucket.</summary>
    private const int MinFamilySize = 3;
    private static readonly HashSet<string> KindWords = new(Enum.GetNames<SensorKind>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The name without its instance number: "Core #3 (Effective)" → "Core Effective", "CCD1 (Tdie)" →
    /// "CCD Tdie". A family that is only the kind's own word ("Temperature #4", "Fan #2") is the generic bucket.</summary>
    public static string Family(SensorDefinition sensor)
    {
        string bare = InstanceMarker().Replace(sensor.Name, " ").Replace("(", " ").Replace(")", " ");
        var words = bare.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => LettersThenDigits().Match(w) is { Success: true } m ? m.Groups[1].Value : w);
        string family = string.Join(' ', words);
        string ownKind = " " + sensor.Kind;
        if (family.EndsWith(ownKind, StringComparison.OrdinalIgnoreCase)) family = family[..^ownKind.Length];   // "Fan #1 Control" (a Control sensor) is the fan it drives
        return KindWords.Contains(family) ? "" : family;
    }

    /// <summary>A section per sensor of one node, or null for a sensor that stays flat. Families of at least
    /// <see cref="MinFamilySize"/> are sections; every other sensor of a kind shares the kind's bucket as
    /// soon as there are two of them (or the bucket is already a section), and a lone one stays flat.</summary>
    public static IReadOnlyDictionary<SensorId, SensorSection?> Classify(IReadOnlyList<SensorDefinition> sensors)
    {
        var familyOf = sensors.ToDictionary(s => s.Id, s => (s.Kind, Family: Family(s)));
        var size = familyOf.Values.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        bool IsSection((SensorKind Kind, string Family) key) => size[key] >= MinFamilySize;
        var loosePerKind = sensors.Where(s => !IsSection(familyOf[s.Id])).GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Count());
        bool HasBucket(SensorKind k) => size.GetValueOrDefault((k, "")) >= MinFamilySize || loosePerKind.GetValueOrDefault(k) >= 2;

        var result = new Dictionary<SensorId, SensorSection?>(sensors.Count);
        foreach (var s in sensors)
        {
            var key = familyOf[s.Id];
            result[s.Id] = IsSection(key) ? new SensorSection(key.Kind, key.Family)
                         : HasBucket(s.Kind) ? new SensorSection(s.Kind, "")
                         : null;
        }
        return result;
    }
}
