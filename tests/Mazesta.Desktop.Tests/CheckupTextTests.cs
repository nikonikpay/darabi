using System.IO; using System.Text.RegularExpressions; using System.Xml.Linq; using Mazesta.Core.Health.Checkup; using Mazesta.Desktop.Services; using Xunit;
namespace Mazesta.Desktop.Tests;

/// <summary>Every finding the checkup can give is worded in both languages, and its numbers read the way the rest of the app writes them.</summary>
public class CheckupTextTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "Mazesta.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("Mazesta.sln not found above the test output.");
    }
    private static HashSet<string> Keys(string file) => [.. XDocument.Load(Path.Combine(RepoRoot(), "src", "Mazesta.Desktop", "Localization", file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!)];

    /// <summary>The keys the checkup names: a title and a text per finding, a line per hint, a word per level, and every measure the rules (and the
    /// bridge's comparison with other systems) put a name to.</summary>
    private static IEnumerable<string> Needed()
    {
        foreach (var c in Enum.GetValues<FindingCode>()) { yield return $"Check_{c}"; yield return $"Check_{c}_Text"; }
        foreach (var h in Enum.GetValues<FindingHint>().Where(h => h != FindingHint.None)) yield return $"Check_Hint_{h}";
        foreach (var l in Enum.GetValues<FindingLevel>()) yield return $"Check_Level_{l}";
        var sources = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Mazesta.Core", "Health", "Checkup"), "*.cs").Append(Path.Combine(RepoRoot(), "src", "Mazesta.App", "WebBridge.Benchmarks.cs"));
        foreach (var key in sources.SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"(Check_M_[A-Za-z]+)\"").Select(m => m.Groups[1].Value)).Distinct()) yield return key;
    }

    [Theory, InlineData("Strings.resx"), InlineData("Strings.fa.resx")]
    public void Every_finding_hint_level_and_measure_is_worded(string file)
    {
        var keys = Keys(file);
        var missing = Needed().Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("Check_M_Clock", 4850, "MHz", "4.85 GHz")]
    [InlineData("Check_M_Clock", 798, "MHz", "798 MHz")]
    [InlineData("Check_M_TempMax", 95.4, "°C", "95 °C")]
    [InlineData("Check_M_Power", 187.6, "W", "188 W")]
    [InlineData("Check_M_Diff", -15.24, "%", "−15.2 %")]
    [InlineData("Check_M_Diff", 3, "%", "+3.0 %")]
    [InlineData("Check_M_ClockDrop", 12.5, "%", "12.5 %")]
    [InlineData("Check_M_LinkWidth", 8, "×", "x8")]
    [InlineData("Check_M_LinkGen", 4, "", "Gen 4")]
    [InlineData("Check_M_Systems", 7, "", "7")]
    [InlineData("Check_M_RamNow", 3200, "MT/s", "3200 MT/s")]
    [InlineData("Check_M_Mine", 412.3, "GFLOPS", "412 GFLOPS")]
    public void Measures_read_as_the_app_writes_readings(string key, double value, string unit, string expected) => Assert.Equal(expected, CheckupText.Value(new(key, value, unit)));
}
