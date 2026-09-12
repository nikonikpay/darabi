using System.Resources;

namespace Mazesta.Desktop.Localization;

public static class HelpText
{
    private static readonly ResourceManager Rm = new("Mazesta.Desktop.Localization.Help", typeof(HelpText).Assembly);
    public static string Get(string key) => Rm.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("fa-IR")) ?? key;
}
