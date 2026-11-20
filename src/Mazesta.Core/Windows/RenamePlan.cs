using System.Text.RegularExpressions;
namespace Mazesta.Core.Windows;

public enum RenameCase { Keep, Lower, Upper, Title, Sentence }
public enum RenameDate { None, Prefix, Suffix }

/// <summary>
/// How many files are renamed. The first six fields are the original rule: a find-and-replace in the name (the extension kept), or, with
/// <c>Number</c>, every name replaced by a base and a number. The rest are extra steps, each off by default: cut characters, add text at
/// either end, number beside the old name, change case, tidy spaces, Persian digits and letters, the file's date, and the extension.
/// </summary>
public sealed record RenameRule(string Find, string Replace, bool Regex = false, bool MatchCase = false, bool Number = false, int Start = 1,
    bool FirstOnly = false, int RemoveFirst = 0, int RemoveLast = 0, string Prefix = "", string Suffix = "",
    bool KeepName = false, bool NumberFirst = false, int Step = 1, int Digits = 0, string Separator = " ",
    RenameCase Case = RenameCase.Keep, bool TrimSpaces = false, char? SpaceTo = null, bool LatinDigits = false, bool PersianLetters = false,
    RenameDate Date = RenameDate.None, bool WithTime = false, bool Shamsi = false, string? NewExtension = null, bool LowerExtension = false);

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
        if (!rule.Regex || (rule.Number && !rule.KeepName)) return null;
        try { _ = new Regex(rule.Find, Options(rule), Budget); return null; } catch (ArgumentException ex) { return ex.Message; }
    }

    /// <param name="modified">A file's last-write time, for the date step (the file's own by default).</param>
    public static IReadOnlyList<RenameItem> Make(IReadOnlyList<string> paths, RenameRule rule, Func<string, bool> exists, Func<string, DateTime>? modified = null)
    {
        modified ??= File.GetLastWriteTime;
        int digits = rule.Digits > 0 ? rule.Digits : Math.Max(2, (rule.Start + (long)Math.Max(0, paths.Count - 1) * rule.Step).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var regex = rule.Regex && (!rule.Number || rule.KeepName) && Check(rule) is null ? new Regex(rule.Find, Options(rule), Budget) : null;
        var names = paths.Select((p, i) =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(p), ext = System.IO.Path.GetExtension(p);
            string stem = Stem(name, i, rule, regex, digits);
            if (rule.RemoveFirst > 0) stem = stem[Math.Min(rule.RemoveFirst, stem.Length)..];
            if (rule.RemoveLast > 0) stem = stem[..Math.Max(0, stem.Length - rule.RemoveLast)];
            stem = rule.Prefix + stem + rule.Suffix;
            if (rule.TrimSpaces) stem = Regex.Replace(stem, @"\s+", " ").Trim();
            if (rule.SpaceTo is { } sp) stem = stem.Replace(' ', sp);
            if (rule.LatinDigits) stem = Latin(stem);
            if (rule.PersianLetters) stem = stem.Replace('ي', 'ی').Replace('ك', 'ک');   // Arabic yeh and kaf to the Persian letters
            stem = rule.Case switch
            {
                RenameCase.Lower => stem.ToLowerInvariant(), RenameCase.Upper => stem.ToUpperInvariant(),
                RenameCase.Title => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
                RenameCase.Sentence => stem.Length == 0 ? stem : char.ToUpperInvariant(stem[0]) + stem[1..].ToLowerInvariant(), _ => stem,
            };
            if (rule.Date != RenameDate.None) { string d = Stamp(modified(p), rule); stem = rule.Date == RenameDate.Prefix ? $"{d} {stem}" : $"{stem} {d}"; }
            if (rule.NewExtension is { } ne) ext = ne.Trim().TrimStart('.') is { Length: > 0 } x ? "." + x : "";
            if (rule.LowerExtension) ext = ext.ToLowerInvariant();
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

    private static string Stem(string name, int i, RenameRule r, Regex? regex, int digits)
    {
        string num = (r.Start + (long)i * r.Step).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
        string Join(string a, string b) => a.Length == 0 || b.Length == 0 || r.Separator.Length == 0 || a.EndsWith(r.Separator) || b.StartsWith(r.Separator) ? a + b : a + r.Separator + b;
        // numbering without KeepName replaces the whole name: the base is the Replace text; with KeepName the find-and-replace still runs first
        string core = r.Number && !r.KeepName ? r.Replace : Substitute(name, r, regex);
        return !r.Number ? core : r.NumberFirst ? Join(num, core) : Join(core, num);
    }

    private static string Substitute(string name, RenameRule r, Regex? regex)
    {
        if (regex is not null) return r.FirstOnly ? regex.Replace(name, r.Replace, 1) : regex.Replace(name, r.Replace);   // a timeout leaves here: the whole plan is refused, never shown as "unchanged"
        if (r.Find.Length == 0) return name;
        var cmp = r.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!r.FirstOnly) return name.Replace(r.Find, r.Replace, cmp);
        int at = name.IndexOf(r.Find, cmp);
        return at < 0 ? name : name[..at] + r.Replace + name[(at + r.Find.Length)..];
    }

    private static string Latin(string s) => string.Create(s.Length, s, (span, src) => { for (int i = 0; i < src.Length; i++) { char c = src[i]; span[i] = c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c; } });

    /// <summary>The file's date as part of a name (yyyy-MM-dd, with _HH-mm when the time is wanted), in the Persian calendar if asked.</summary>
    private static string Stamp(DateTime t, RenameRule r)
    {
        int y = t.Year, m = t.Month, d = t.Day;
        if (r.Shamsi) { var pc = new System.Globalization.PersianCalendar(); y = pc.GetYear(t); m = pc.GetMonth(t); d = pc.GetDayOfMonth(t); }
        string s = $"{y:0000}-{m:00}-{d:00}";
        return r.WithTime ? $"{s}_{t.Hour:00}-{t.Minute:00}" : s;
    }

    private static string Key(string path, string name) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", name);
    private static RegexOptions Options(RenameRule r) => r.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
}

/// <summary>A file's checksum as people paste it from a download page ("SHA256: 9F86D081..."): the longest run of 32 or more hex digits,
/// any case; its length says which kind it is.</summary>