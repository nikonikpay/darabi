using System.IO; using System.Net; using System.Net.Http; using System.Text.Json; using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

/// <summary>A product from the shop's site, as plain text: the site's HTML is never passed to the page.</summary>
public sealed record ShopProduct(long Id, string Title, string Summary, string Link, string? Image, DateTimeOffset Fetched);

/// <summary>
/// One random product from the shop's own WordPress site (dfmrendering.com, the WordPress REST API's product list) for the dashboard. The site
/// answers with HTML inside its JSON (the title's entities, the excerpt's lists); that is reduced to plain text here, so the page only ever
/// shows words and never markup. The image is fetched once and handed over as a data URL (the page may load nothing from the internet itself).
/// The last product is cached in <c>Data/cache/shop</c> and shown while offline; nothing is shown when there has never been one.
/// </summary>
public sealed partial class ShopFeed(string cacheDir, ILogger log)
{
    public const string Site = "https://www.dfmrendering.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12), DefaultRequestHeaders = { { "User-Agent", "MazestaTest/1.0 (+https://www.dfmrendering.com)" } } };
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);
    private const int MaxImageBytes = 600_000;
    private readonly string _file = Path.Combine(cacheDir, "shop", "product.json");
    private int _total;

    /// <summary>The cached product if it is recent, otherwise a new one (or the cached one when the site cannot be reached).</summary>
    public async Task<ShopProduct?> GetAsync(bool another)
    {
        var cached = Read();
        if (!another && cached is not null && DateTimeOffset.UtcNow - cached.Fetched < Fresh) return cached;
        try { var fresh = await FetchAsync(cached?.Id).ConfigureAwait(false); if (fresh is not null) { Save(fresh); return fresh; } }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        { log.LogInformation("Shop product not fetched: {Message}", e.Message); }
        return cached;
    }

    private async Task<ShopProduct?> FetchAsync(long? not)
    {
        // The site reports how many products there are; a random page of one product picks one of them. The first call learns the count.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int page = _total > 0 ? Random.Shared.Next(1, _total + 1) : 1;
            using var res = await Http.GetAsync($"{Site}/wp-json/wp/v2/product?per_page=1&page={page}&_fields=id,link,title,excerpt,featured_media").ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            if (res.Headers.TryGetValues("X-WP-Total", out var totals) && int.TryParse(totals.FirstOrDefault(), out int total)) _total = total;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (doc.RootElement.GetArrayLength() == 0) continue;
            var p = doc.RootElement[0];
            long id = p.GetProperty("id").GetInt64();
            if ((id == not || page == 1) && _total > 1 && attempt < 2) continue;   // the first call only learned the count; and not the same product again
            string link = p.GetProperty("link").GetString() ?? "";
            if (!IsShopLink(link)) return null;
            string title = Plain(p.GetProperty("title").GetProperty("rendered").GetString());
            string summary = Clip(Plain(p.GetProperty("excerpt").GetProperty("rendered").GetString()), 220);
            string? image = p.TryGetProperty("featured_media", out var m) && m.TryGetInt64(out long media) && media > 0 ? await ImageAsync(media).ConfigureAwait(false) : null;
            return new ShopProduct(id, title, summary, link, image, DateTimeOffset.UtcNow);
        }
        return null;
    }

    private static async Task<string?> ImageAsync(long media)
    {
        using var res = await Http.GetAsync($"{Site}/wp-json/wp/v2/media/{media}?_fields=source_url,media_details").ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        var root = doc.RootElement;
        string? url = root.TryGetProperty("media_details", out var d) && d.TryGetProperty("sizes", out var sizes) && sizes.ValueKind == JsonValueKind.Object
            ? new[] { "medium_large", "medium", "woocommerce_single", "full" }.Select(s => sizes.TryGetProperty(s, out var size) ? size.GetProperty("source_url").GetString() : null).FirstOrDefault(u => u is not null)
            : null;
        url ??= root.TryGetProperty("source_url", out var src) ? src.GetString() : null;
        if (url is null || !IsShopLink(url)) return null;
        using var img = await Http.GetAsync(url).ConfigureAwait(false);
        var type = img.Content.Headers.ContentType?.MediaType;
        if (!img.IsSuccessStatusCode || type is not ("image/jpeg" or "image/png" or "image/webp")) return null;
        var bytes = await img.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return bytes.Length > MaxImageBytes ? null : $"data:{type};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>Only the shop's own pages are opened or loaded.</summary>
    public static bool IsShopLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host is "www.dfmrendering.com" or "dfmrendering.com";

    /// <summary>HTML to plain text: tags become spaces (list items a separator), entities decoded, whitespace collapsed.</summary>
    internal static string Plain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = Items().Replace(html, " · ");
        s = Tags().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Spaces().Replace(s, " ").Trim();
        return s.Trim('·', ' ');
    }

    internal static string Clip(string s, int max)
    {
        if (s.Length <= max) return s;
        int cut = s.LastIndexOf(' ', max - 1);
        return s[..(cut > 40 ? cut : max)].TrimEnd(' ', '·', '،', ',') + "…";
    }

    private ShopProduct? Read()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<ShopProduct>(File.ReadAllText(_file)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void Save(ShopProduct p)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file, JsonSerializer.Serialize(p)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.LogInformation("Shop product not cached: {Message}", e.Message); }
    }

    [GeneratedRegex(@"</li>\s*<li[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex Items();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
