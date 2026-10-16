using System.Text.RegularExpressions;
namespace Mazesta.Desk;

/// <summary>What the page may ask of the site, decided on the C# side so a page that went wrong (or a script injected into it) cannot reach any other address.</summary>
public static partial class DeskRules
{
    public const string DefaultServer = "https://www.dfmrendering.com";
    public const string ApiRoot = "wp-json/mazesta/v1/";

    /// <summary>The site's address as typed ("dfmrendering.com", "https://…/wp-json/…") to its origin, or null. Only https is accepted, except for a local test server (localhost, 127.0.0.1) where the password never leaves the PC.</summary>
    public static Uri? Server(string? typed)
    {
        string s = (typed ?? "").Trim();
        if (s.Length == 0 || s.Length > 200) return null;
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.UserInfo)) return null;
        bool local = u.IsLoopback;
        if (u.Scheme != Uri.UriSchemeHttps && !(local && u.Scheme == Uri.UriSchemeHttp)) return null;
        return new Uri(u.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>The page's path ("staff/jobs?q=R-12") when it is a staff route with a harmless query; the address to call is <c>server + ApiRoot + path</c>.</summary>
    public static bool PathAllowed(string? path) => path is { Length: > 0 and <= 600 } && Route().IsMatch(path);

    public static bool MethodAllowed(string? m) => m is "GET" or "POST";

    [GeneratedRegex(@"^staff/[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*(\?[A-Za-z0-9_\-=&%.~+]*)?$")]
    private static partial Regex Route();
}
