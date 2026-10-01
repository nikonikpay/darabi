using System.Globalization; using System.Text.Json; using System.Xml.Linq;
namespace Mazesta.Core.Drivers;

/// <summary>One driver NVIDIA publishes for a card, as its own driver lookup lists it.</summary>
/// <param name="Studio">An NVIDIA Studio driver (tested with creative programs); otherwise Game Ready, or for a professional card its RTX/Quadro driver.</param>
public sealed record NvidiaRelease(string Version, DateOnly? Date, bool Studio, string Name, string Url, string? Size, string? DetailsUrl);

/// <summary>A card as NVIDIA's driver site numbers it: the product series (psid) and the product (pfid).</summary>
/// <param name="GeForce">A GeForce card: it has the Game Ready / Studio choice. A professional card (RTX A-series, Quadro, RTX PRO) has one driver line.</param>
public sealed record NvidiaProduct(int SeriesId, int ProductId, string Name, string Series, bool GeForce);

/// <summary>
/// NVIDIA's own driver lookup (the service behind nvidia.com/drivers), read here without I/O: the product list (series and products, XML), the
/// releases for one product (JSON), and the version Windows shows for an NVIDIA driver turned into NVIDIA's number (32.0.16.1062 is 610.62:
/// the last five digits). Which of two drivers is newer is decided by NVIDIA's numbers, never by dates.
/// </summary>
public static class NvidiaDrivers
{
    /// <summary>NVIDIA's version (610.62) of the version Windows reports for its driver (32.0.16.1062), or null for one that is not NVIDIA's form.</summary>
    public static string? FromWindowsVersion(string? windowsVersion)
    {
        var parts = windowsVersion?.Split('.');
        if (parts is not { Length: 4 } || parts.Any(p => !p.All(char.IsAsciiDigit) || p.Length == 0)) return null;
        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    /// <summary>Compares two NVIDIA versions (610.62 &lt; 617.14) by their numbers.</summary>
    public static int Compare(string a, string b)
    {
        static (int, int) Parse(string v) { var p = v.Split('.'); return (int.TryParse(p[0], out int x) ? x : 0, p.Length > 1 && int.TryParse(p[1], out int y) ? y : 0); }
        return Parse(a).CompareTo(Parse(b));
    }

    /// <summary>The card NVIDIA's list knows by the name Windows gives it ("NVIDIA GeForce RTX 3090" is "GeForce RTX 3090" in the list). Only an
    /// exact name counts, so a card is never matched to a near one (a 3090 Ti's driver is the same, but its page is not); a laptop chip is found
    /// among the notebook series.</summary>
    public static NvidiaProduct? FindProduct(string gpuName, string seriesXml, string productsXml)
    {
        static string Key(string s) => string.Join(' ', s.Replace("NVIDIA", "", StringComparison.OrdinalIgnoreCase).Replace("(R)", "").Replace("(TM)", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var series = Values(seriesXml).ToDictionary(v => v.Value, v => v.Name);
        string want = Key(gpuName); bool laptop = want.Contains("laptop") || want.Contains("notebook") || want.Contains("max-q");
        var found = Values(productsXml).Where(p => Key(p.Name) == want && series.ContainsKey(p.Parent))
            .Select(p => new NvidiaProduct(p.Parent, p.Value, p.Name, series[p.Parent], series[p.Parent].Contains("GeForce", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Series.Contains("Notebook", StringComparison.OrdinalIgnoreCase) != laptop ? 1 : 0).ThenBy(p => p.Series.Length).ToList();
        return found.FirstOrDefault();
    }

    private static IEnumerable<(int Parent, int Value, string Name)> Values(string xml)
    {
        foreach (var v in XDocument.Parse(xml).Descendants("LookupValue"))
            if (int.TryParse(v.Element("Value")?.Value, out int value) && v.Element("Name")?.Value is { } name)
                yield return (int.TryParse(v.Attribute("ParentID")?.Value, out int parent) ? parent : 0, value, name.Trim());
    }

    /// <summary>The releases in a lookup answer, newest first; a beta is left out. <paramref name="studio"/> is what was asked for: the answer
    /// marks each release itself (IsCRD), and one of the other kind is dropped.</summary>
    public static IReadOnlyList<NvidiaRelease> ParseReleases(string json, bool studio)
    {
        using var d = JsonDocument.Parse(json);
        if (!d.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array) return [];
        var list = new List<NvidiaRelease>();
        foreach (var x in ids.EnumerateArray())
        {
            if (!x.TryGetProperty("downloadInfo", out var i)) continue;
            string S(string n) => i.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? Uri.UnescapeDataString(v.GetString() ?? "") : "";
            if (S("Version") is not { Length: > 0 } version || S("DownloadURL") is not { Length: > 0 } url || S("IsBeta") == "1") continue;
            bool crd = S("IsCRD") == "1";
            if (crd != studio) continue;
            DateOnly? date = DateOnly.TryParseExact(S("ReleaseDateTime"), "ddd MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
            list.Add(new(version, date, crd, S("Name"), url, S("DownloadURLFileSize") is { Length: > 0 } size ? size : null, S("DetailsURL") is { Length: > 0 } du ? du : null));
        }
        return [.. list.OrderByDescending(r => r.Version, Comparer<string>.Create(Compare))];
    }

    /// <summary>The address of NVIDIA's lookup for a card's releases: Windows 10/11 64-bit, DCH drivers, English notes.</summary>
    public static string LookupUrl(NvidiaProduct p, bool studio, int count = 15) =>
        "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup" +
        $"&psid={p.SeriesId}&pfid={p.ProductId}&osID=57&languageCode=1033&beta=0&isWHQL=0&dltype=-1&dch=1&upCRD={(studio ? 1 : 0)}&qnf=0&sort1=1&numberOfResults={count}";

    public const string SeriesUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2";
    public const string ProductsUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
}
