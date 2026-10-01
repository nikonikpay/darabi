using System.Globalization; using System.Text.Json; using System.Text.RegularExpressions;
namespace Mazesta.Core.Drivers;

/// <summary>What on the motherboard a driver package is for.</summary>
public enum BoardPart { Chipset, Lan, Audio, Wireless, Bluetooth, Bios }

/// <summary>
/// A driver package a maker offers for this computer: who offers it (the board's maker, AMD or Intel), its version and date as the maker lists
/// them, the file and the hash the maker publishes for it (when it does), and, for Intel's packages, the hardware IDs it is for.
/// </summary>
public sealed record BoardPackage(BoardPart Part, string Source, string Title, string Version, DateOnly? Date, string? Size, string Url,
    string? Sha256 = null, string? Sha1 = null, string? Vendor = null, IReadOnlyList<string>? HardwareIds = null);

/// <summary>A device and its driver as Windows lists them (Win32_PnPSignedDriver).</summary>
public sealed record BoardDevice(string Id, string Name, string? Class, string? Maker, string? Version, DateOnly? Date);

/// <summary>A package and what this computer has for it. <see cref="Newer"/> is null when the two versions cannot be compared or nothing is installed.</summary>
public sealed record BoardItem(BoardPackage Package, string? DeviceName, string? Installed, DateOnly? InstalledDate, bool? Newer, bool Missing);

/// <summary>
/// The motherboard's drivers from their makers: ASUS's support API (the board's own page: chipset, LAN, audio, Wi-Fi, Bluetooth and the BIOS),
/// AMD's chipset page, and the data Intel's Driver &amp; Support Assistant reads (Intel's Wi-Fi, Bluetooth and Ethernet packages with the hardware
/// IDs each is for). Parsing and comparing only; the fetching is in the Diagnostics layer.
/// </summary>
public static partial class BoardDrivers
{
    public static string AsusDriversUrl(string model) => $"https://www.asus.com/support/api/product.asmx/GetPDDrivers?website=global&model={Uri.EscapeDataString(model)}&osid=52";
    public static string AsusBiosUrl(string model) => $"https://www.asus.com/support/api/product.asmx/GetPDBIOS?website=global&model={Uri.EscapeDataString(model)}";
    public const string IntelDataUrl = "https://dsadata.intel.com/data/en";
    public const string IntelChipsetPage = "https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html";

    /// <summary>The makers whose names a package's title or a device's maker field is matched by.</summary>
    private static readonly string[] Vendors = ["Realtek", "Intel", "AMD", "MediaTek", "Qualcomm", "Killer", "Marvell", "Aquantia", "Broadcom", "Nuvoton"];

    public static string? VendorIn(string? text) => text is null ? null : Vendors.FirstOrDefault(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));

    /// <summary>ASUS's category names to the board's parts; utilities, RAID and graphics are not the board's drivers (the card has its own section).</summary>
    public static BoardPart? PartOf(string category)
    {
        string c = category.ToLowerInvariant();
        return c switch
        {
            "chipset" => BoardPart.Chipset, "lan" => BoardPart.Lan, "audio" => BoardPart.Audio,
            _ when c.Contains("bluetooth", StringComparison.Ordinal) => BoardPart.Bluetooth,
            _ when c.Contains("wireless", StringComparison.Ordinal) || c.Contains("wifi", StringComparison.Ordinal) || c.Contains("wi-fi", StringComparison.Ordinal) || c.Contains("wlan", StringComparison.Ordinal) => BoardPart.Wireless,
            _ => null,
        };
    }

    /// <summary>
    /// The newest package of each part and maker on an ASUS answer (a board sold with two Wi-Fi chips lists both makers' drivers; each is kept
    /// so the one this board has can be matched). Null when ASUS does not know the model.
    /// </summary>
    public static IReadOnlyList<BoardPackage>? ParseAsus(string json, bool bios = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("Status", out var st) || st.GetString() != "SUCCESS" || !root.TryGetProperty("Result", out var res) || res.ValueKind != JsonValueKind.Object) return null;
        var list = new List<BoardPackage>();
        foreach (var group in res.GetProperty("Obj").EnumerateArray())
        {
            BoardPart? part = bios ? (group.GetProperty("Name").GetString() == "BIOS" ? BoardPart.Bios : null) : PartOf(group.GetProperty("Name").GetString() ?? "");
            if (part is null) continue;
            foreach (var f in group.GetProperty("Files").EnumerateArray())
            {
                if (Text(f, "IsRelease") is "0") continue;
                string? url = f.TryGetProperty("DownloadUrl", out var du) && du.ValueKind == JsonValueKind.Object ? Text(du, "Global") : null;
                string? version = Text(f, "Version");
                if (url is null || version is null) continue;
                string title = WebText(Text(f, "Title") ?? Text(f, "Description") ?? "");
                var date = DateOnly.TryParseExact(Text(f, "ReleaseDate"), "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
                list.Add(new(part.Value, "asus", title, version, date, Text(f, "FileSize"), url.Split('?')[0], Sha256: Text(f, "sha256"), Vendor: part == BoardPart.Bios ? "ASUS" : VendorIn(title)));
            }
        }
        return [.. list.GroupBy(p => (p.Part, p.Vendor)).Select(g => g.OrderByDescending(p => p.Date ?? DateOnly.MinValue).First())];
    }

    /// <summary>AMD's chipset package from its drivers page: the download link names the version; the size and date stand beside it.</summary>
    public static BoardPackage? ParseAmdChipset(string html)
    {
        var link = AmdLink().Match(html);
        if (!link.Success) return null;
        string text = Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ");
        var meta = AmdMeta().Match(text);
        return new(BoardPart.Chipset, "amd", "AMD Chipset Software", link.Groups[1].Value,
            meta.Success && DateOnly.TryParseExact(meta.Groups[3].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
            meta.Success ? meta.Groups[2].Value : null, link.Value, Vendor: "AMD");
    }

    /// <summary>
    /// Intel's Wi-Fi, Bluetooth and Ethernet packages from the Driver &amp; Support Assistant's data (software-configurations.json): the version is
    /// the driver's own (comparable to what Windows lists), and each comes with the PCI or USB IDs it is for.
    /// </summary>
    public static IReadOnlyList<BoardPackage> ParseIntel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<BoardPackage>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("IsBeta", out var beta) && beta.ValueKind == JsonValueKind.True) continue;
            if (!e.TryGetProperty("Components", out var comps) || !e.TryGetProperty("Files", out var files) || files.GetArrayLength() == 0) continue;
            foreach (var c in comps.EnumerateArray())
            {
                BoardPart? part = Text(c, "Category") switch { "Wireless" => BoardPart.Wireless, "Bluetooth" => BoardPart.Bluetooth, "Ethernet" or "Networking" or "LAN" => BoardPart.Lan, _ => null };
                if (part is null || Text(c, "Version") is not { } version) continue;
                var ids = c.TryGetProperty("DetectionValues", out var dv) ? dv.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
                if (ids.Count == 0) continue;
                var file = files[0];
                long? bytes = file.TryGetProperty("Size", out var sz) && sz.TryGetInt64(out var b) ? b : null;
                var date = e.TryGetProperty("DisplayReleaseDate", out var rd) && DateTime.TryParse(rd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? DateOnly.FromDateTime(dt) : (DateOnly?)null;
                list.Add(new(part.Value, "intel", WebText(Text(e, "Name") ?? ""), version, date, bytes is { } n ? string.Create(CultureInfo.InvariantCulture, $"{n / 1048576.0:0.#} MB") : null, Text(file, "Url") ?? "",
                    Sha1: Text(file, "Hash"), Vendor: "Intel", HardwareIds: ids));
            }
        }
        return [.. list.Where(p => p.Url.Length > 0)];
    }

    /// <summary>Whether a device's ID (PCI\VEN_8086&amp;DEV_2725&amp;SUBSYS_00248086&amp;REV_1A\…) is one of Intel's patterns (VEN_8086&amp;DEV_2725&amp;SUBSYS_*8086).</summary>
    public static bool Matches(string deviceId, string pattern)
    {
        string body = deviceId.Contains('\\', StringComparison.Ordinal) ? deviceId.Split('\\')[1] : deviceId;
        var rx = "^" + Regex.Escape(pattern).Replace(@"\*", "[^&\\\\]*", StringComparison.Ordinal) + "(&|$)";
        return Regex.IsMatch(body, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// The newer of two versions, part by part, or null when they cannot be compared: one is not plain numbers, or they have a different number of
    /// parts (a maker's package number beside the driver number Windows lists; told side by side, never guessed).
    /// </summary>
    public static int? Compare(string? offered, string? installed)
    {
        if (Parts(offered) is not { } a || Parts(installed) is not { } b || a.Length != b.Length) return null;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return 0;
    }

    private static long[]? Parts(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim().TrimStart('v', 'V').Split('.');
        var r = new long[s.Length];
        for (int i = 0; i < s.Length; i++) if (!long.TryParse(s[i], NumberStyles.None, CultureInfo.InvariantCulture, out r[i])) return null;
        return r;
    }

    /// <summary>AMD's chipset package suits every AMD desktop board (one package for AM4 and AM5); the page asked is the board's socket's.</summary>
    public static string AmdChipsetPage(string? boardModel) => AmdChipset().Match(boardModel ?? "") is { Success: true } m && m.Groups[1].Value[0] is '3' or '4' or '5'
        ? "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am4/b550.html" : "https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/b650.html";

    /// <summary>The board maker's own support page for the model, for the makers whose sites a program cannot read.</summary>
    public static string? SupportPage(string? maker, string? model)
    {
        if (maker is null || model is null) return null;
        string m = Uri.EscapeDataString(model);
        return maker.ToUpperInvariant() switch
        {
            var x when x.Contains("ASUS", StringComparison.Ordinal) => $"https://www.asus.com/searchresult?searchType=support&searchKey={m}",
            var x when x.Contains("MICRO-STAR", StringComparison.Ordinal) || x.Contains("MSI", StringComparison.Ordinal) => $"https://www.msi.com/search/{m}",
            var x when x.Contains("GIGABYTE", StringComparison.Ordinal) => $"https://www.gigabyte.com/Search?kw={m}",
            var x when x.Contains("ASROCK", StringComparison.Ordinal) => $"https://www.asrock.com/support/index.asp?cat=Drivers&Model={m}",
            _ => null,
        };
    }

    private static readonly string[] WirelessWords = ["Wireless", "Wi-Fi", "WiFi", "WLAN", "802.11"];
    private static bool IsWireless(string name) => WirelessWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Each package beside what this computer has for it: the device it is for and the driver Windows lists for that device (or, for a chipset
    /// package, the installed program; for the BIOS, the BIOS version). A package of a maker's board page whose device this computer does not have
    /// (a board sold with and without Wi-Fi lists the Wi-Fi driver either way) is kept with <see cref="BoardItem.Missing"/>, so nothing is offered
    /// for it. Intel's packages are matched by hardware ID, and only those this computer has a device for are kept; of several that fit a device,
    /// the newest.
    /// </summary>
    public static IReadOnlyList<BoardItem> Match(IEnumerable<BoardPackage> packages, IReadOnlyList<BoardDevice> devices, IReadOnlyList<(string Name, string? Version)> programs, string? bios)
    {
        var items = new List<BoardItem>();
        foreach (var p in packages)
        {
            if (p.HardwareIds is { } ids)
            {
                var d = devices.FirstOrDefault(x => ids.Any(id => Matches(x.Id, id)));
                if (d is not null) items.Add(Item(p, d.Name, d.Version, d.Date));
                continue;
            }
            switch (p.Part)
            {
                case BoardPart.Bios: items.Add(Item(p, null, bios, null)); break;
                case BoardPart.Chipset:
                    var prog = programs.FirstOrDefault(x => p.Vendor == "AMD" ? x.Name.Equals("AMD Chipset Software", StringComparison.OrdinalIgnoreCase)
                        : p.Vendor == "Intel" && x.Name.Contains("Chipset Device Software", StringComparison.OrdinalIgnoreCase));
                    items.Add(Item(p, prog.Name, prog.Version, null)); break;
                default:
                    var dev = devices.FirstOrDefault(x => Fits(p, x));
                    items.Add(dev is null ? new BoardItem(p, null, null, null, null, Missing: true) : Item(p, dev.Name, dev.Version, dev.Date)); break;
            }
        }
        // Of Intel's packages for one device, the newest (Intel's data lists several generations that all fit an older chip).
        return [.. items.GroupBy(i => i.Package.HardwareIds is null ? i.Package.Source + i.Package.Part + i.Package.Vendor + i.Package.Url : "intel" + i.Package.Part + i.DeviceName)
            .Select(g => g.Aggregate((a, b) => (Compare(b.Package.Version, a.Package.Version) ?? (b.Package.Date ?? DateOnly.MinValue).CompareTo(a.Package.Date ?? DateOnly.MinValue)) > 0 ? b : a))];

        static BoardItem Item(BoardPackage p, string? device, string? installed, DateOnly? date)
            => new(p, device, installed, date, installed is null ? null : Compare(p.Version, installed) is { } c ? c > 0 : null, Missing: false);
    }

    /// <summary>Whether a device is the one a board maker's package of this part and maker is for.</summary>
    private static bool Fits(BoardPackage p, BoardDevice d)
    {
        if (p.Vendor is null || !(d.Maker?.Contains(p.Vendor, StringComparison.OrdinalIgnoreCase) == true || d.Name.Contains(p.Vendor, StringComparison.OrdinalIgnoreCase))) return false;
        string cls = d.Class ?? "", id = d.Id;
        return p.Part switch
        {
            BoardPart.Lan => cls.Equals("NET", StringComparison.OrdinalIgnoreCase) && id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && !IsWireless(d.Name),
            BoardPart.Wireless => cls.Equals("NET", StringComparison.OrdinalIgnoreCase) && id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) && IsWireless(d.Name),
            BoardPart.Audio => id.StartsWith("HDAUDIO\\", StringComparison.OrdinalIgnoreCase) && cls.Equals("MEDIA", StringComparison.OrdinalIgnoreCase),
            BoardPart.Bluetooth => cls.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) && (id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s.Trim() : null;
    private static string WebText(string s) => System.Net.WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", " ")).Replace("®", "", StringComparison.Ordinal).Replace("™", "", StringComparison.Ordinal).Replace("*", "", StringComparison.Ordinal).Trim();

    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/AMD_Chipset_Software_([0-9.]+[0-9])\.exe", RegexOptions.IgnoreCase)] private static partial Regex AmdLink();
    [GeneratedRegex(@"AMD Chipset Drivers Revision Number ([0-9.]+) File Size ([0-9.]+ [KMG]B) Release Date (\d{4}-\d{2}-\d{2})")] private static partial Regex AmdMeta();
    [GeneratedRegex(@"\b[ABX](\d)\d0(?!\d)")] private static partial Regex AmdChipset();
}
