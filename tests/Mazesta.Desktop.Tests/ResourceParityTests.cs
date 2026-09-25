using System.IO; using System.Text.RegularExpressions; using System.Xml.Linq; using Xunit;
namespace Mazesta.Desktop.Tests;

/// <summary>Every user-visible string must exist in both languages (AGENTS.md): a key in one resx and not the other, an empty
/// Persian value, or a XAML <c>{loc:Loc Key}</c> that names no key would show a raw key or nothing at run time.</summary>
public class ResourceParityTests
{
    private static readonly string Localization = Path.Combine(RepoRoot(), "src", "Mazesta.Desktop", "Localization");
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "Mazesta.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("Mazesta.sln not found above the test output.");
    }
    private static Dictionary<string, string> Read(string file)
        => XDocument.Load(Path.Combine(Localization, file)).Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "");

    [Fact] public void English_and_Persian_have_the_same_keys()
    {
        var en = Read("Strings.resx"); var fa = Read("Strings.fa.resx");
        Assert.Empty(en.Keys.Except(fa.Keys).Select(k => "missing in Persian: " + k).Concat(fa.Keys.Except(en.Keys).Select(k => "missing in English: " + k)));
    }

    [Fact] public void No_Persian_or_English_value_is_empty()
    {
        Assert.Empty(Read("Strings.fa.resx").Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => "fa: " + p.Key));
        Assert.Empty(Read("Strings.resx").Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => "en: " + p.Key));
    }

    [Fact] public void No_Persian_value_is_double_encoded()   // UTF-8 read as Latin-1 turns Persian into "Ø§Ù..." - accented Latin never belongs in these strings
        => Assert.Empty(Read("Strings.fa.resx").Where(p => p.Value.Any(c => c is >= 'À' and <= 'ÿ')).Select(p => "fa: " + p.Key));

    [Fact] public void Every_key_a_view_names_exists()
    {
        var keys = Read("Strings.resx").Keys.ToHashSet();
        var used = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Mazesta.Desktop"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{loc:Loc\s+(?:Key=)?([A-Za-z0-9_.]+)\}").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)));
        Assert.Empty(used.Where(u => !keys.Contains(u.Key)).Select(u => $"{u.File}: {u.Key}"));
    }
}
