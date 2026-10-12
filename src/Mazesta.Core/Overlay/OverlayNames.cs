using System.Text.RegularExpressions;
namespace Mazesta.Core.Overlay;

/// <summary>A part's name short enough for the top of its overlay block: the maker's long form is trimmed ("NVIDIA GeForce RTX 5070" → "RTX 5070", "AMD Ryzen 9 9950X3D 16-Core Processor" →
/// "Ryzen 9 9950X3D"), nothing is invented, and a name that would become empty stays as it was.</summary>
public static partial class OverlayNames
{
    public static string Short(string name)
    {
        string s = name.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
        s = Suffix().Replace(s, ""); s = Prefix().Replace(s, "");
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length == 0 ? name.Trim() : s;
    }

    [GeneratedRegex(@"\s+(\d+-Core\s+)?Processor\b.*$|\s+CPU\s*@.*$|\s+with\s+Radeon.*$|\s+Graphics$", RegexOptions.IgnoreCase)] private static partial Regex Suffix();
    [GeneratedRegex(@"^(NVIDIA\s+GeForce\s+|NVIDIA\s+|AMD\s+(?=Ryzen|Radeon|Athlon|EPYC)|Intel\s+(?=Core|Arc|Xeon|Pentium|Celeron))", RegexOptions.IgnoreCase)] private static partial Regex Prefix();
}
