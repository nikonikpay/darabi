using System.Text.RegularExpressions;
namespace Mazesta.Core.Windows;

/// <summary>How many files are renamed: a find-and-replace in the name (the extension kept), or every name replaced by a base and a number.</summary>
public sealed record RenameRule(string Find, string Replace, bool Regex = false, bool MatchCase = false, bool Number = false, int Start = 1);

public enum RenameState { Same, Ready, Clash, Bad }

public sealed record RenameItem(string Path, string NewName, RenameState State);

/// <summary>
/// The preview of a batch rename, worked out before anything is touched: each file's new name and whether it can be given. Two files
/// may not end with one name, nor take the name of a file outside the batch, and a name Windows refuses is marked; the batch runs only
/// when nothing is marked.
/// </summary>
public static class RenamePlan
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();
    /// <summary>A name is at most 255 characters: an expression that needs longer than this on one has run away (catastrophic backtracking).</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    /// <summary>Null when the rule is usable; otherwise why not (a regular expression that does not parse).</summary>
    public static string? Check(RenameRule rule)
    {
        if (!rule.Regex || rule.Number) return null;
        try { _ = new Regex(rule.Find, Options(rule), Budget); return null; } catch (ArgumentException ex) { return ex.Message; }
    }

    public static IReadOnlyList<RenameItem> Make(IReadOnlyList<string> paths, RenameRule rule, Func<string, bool> exists)
    {
        int digits = Math.Max(2, (rule.Start + paths.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var regex = rule.Regex && !rule.Number && Check(rule) is null ? new Regex(rule.Find, Options(rule), Budget) : null;
        var names = paths.Select((p, i) =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(p), ext = System.IO.Path.GetExtension(p);
            string stem = rule.Number ? $"{rule.Replace}{(rule.Replace.Length > 0 && !rule.Replace.EndsWith(' ') ? " " : "")}{(rule.Start + i).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0')}"
                : regex is not null ? regex.Replace(name, rule.Replace)   // a timeout leaves here: the whole plan is refused, never shown as "unchanged"
                : rule.Find.Length == 0 ? name : name.Replace(rule.Find, rule.Replace, rule.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            return (Path: p, New: stem.TrimEnd(' ', '.') + ext, Old: System.IO.Path.GetFileName(p));
        }).ToList();

        // a name taken twice in a folder, or by a file there that is not renamed, is a clash for every file that wants it
        var taken = names.GroupBy(n => Key(n.Path, n.New), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batch = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Select(n =>
        {
            if (n.New == n.Old) return new RenameItem(n.Path, n.New, RenameState.Same);
            string stem = System.IO.Path.GetFileNameWithoutExtension(n.New), target = Key(n.Path, n.New);
            if (stem.Length == 0 || n.New.IndexOfAny(Invalid) >= 0 || Reserved.Contains(stem) || target.Length >= 260) return new RenameItem(n.Path, n.New, RenameState.Bad);
            // a new name that differs only in case is the same file to Windows, not a clash
            bool other = exists(target) && !batch.Contains(target) && !string.Equals(target, n.Path, StringComparison.OrdinalIgnoreCase);
            return new RenameItem(n.Path, n.New, taken.Contains(target) || other ? RenameState.Clash : RenameState.Ready);
        }).ToList();
    }

    private static string Key(string path, string name) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", name);
    private static RegexOptions Options(RenameRule r) => r.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
}

/// <summary>A file's checksum as people paste it from a download page ("SHA256: 9F86D081..."): the longest run of 32 or more hex digits,
/// any case; its length says which kind it is.</summary>
public static class Checksum
{
    public static string Clean(string text) => Regex.Matches(text, "[0-9a-fA-F]{32,}").Select(m => m.Value.ToLowerInvariant()).OrderByDescending(s => s.Length).FirstOrDefault() ?? "";
    public static string? KindOf(string text) => Clean(text).Length switch { 32 => "MD5", 40 => "SHA-1", 64 => "SHA-256", 128 => "SHA-512", _ => null };
    public static bool Matches(string computed, string pasted) => Clean(pasted).Length > 0 && Clean(computed) == Clean(pasted);
}
