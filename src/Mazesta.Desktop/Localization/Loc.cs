using System.Globalization;
using System.Resources;

namespace Mazesta.Desktop.Localization;

public static class Loc
{
    private static readonly ResourceManager Rm = new("Mazesta.Desktop.Localization.Strings", typeof(Loc).Assembly);
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static bool IsRtl => Culture.TextInfo.IsRightToLeft;

    public static void SetLanguage(string code)
    {
        Culture = CultureInfo.GetCultureInfo(code == "fa" ? "fa-IR" : "en");
        CultureInfo.CurrentUICulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
    }

    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
