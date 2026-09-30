using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Health.Checkup; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.Services;

/// <summary>A finding in the user's language: its title, what it means and what to do, the line its measurements point to, and each measured
/// number with its name. Numbers stay Latin, as every reading in the app does.</summary>
public static class CheckupText
{
    public static string Title(Finding f) => Loc.Get($"Check_{f.Code}");
    public static string Text(Finding f) => Loc.Get($"Check_{f.Code}_Text");
    public static string? Hint(Finding f) => f.Hint == FindingHint.None ? null : Loc.Get($"Check_Hint_{f.Hint}");
    public static string Level(FindingLevel level) => Loc.Get($"Check_Level_{level}");

    /// <summary>The finding worded for a saved report, which keeps the words (a report is read later, by another version or in another language).</summary>
    public static Mazesta.Reporting.FindingEntry Entry(Finding f)
        => new(f.Level.ToString(), Title(f), Text(f), Hint(f), f.Subject, [.. f.Measures.Select(m => new Mazesta.Reporting.FindingMeasure(Loc.Get(m.Key), Value(m)))]);

    public static string Value(Measure m)
    {
        static string N(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
        return m.Unit switch
        {
            "MHz" => m.Value >= 1000 ? N(m.Value / 1000, "0.00") + " GHz" : N(m.Value, "0") + " MHz",
            "°C" => N(m.Value, "0") + " °C",
            "W" => N(m.Value, "0") + " W",
            "s" => N(m.Value, "0") + " s",
            "MT/s" => N(m.Value, "0") + " MT/s",
            "×" => "x" + N(m.Value, "0"),
            "%" when m.Key == "Check_M_Diff" => (m.Value > 0 ? "+" : m.Value < 0 ? "−" : "") + N(Math.Abs(m.Value), "0.0") + " %",
            "%" => N(m.Value, "0.#") + " %",
            "" when m.Key.EndsWith("Gen", StringComparison.Ordinal) || m.Key.EndsWith("GenExpected", StringComparison.Ordinal) => "Gen " + N(m.Value, "0"),
            "" => N(m.Value, "0"),
            _ => Units.FormatMeasured(m.Value, m.Unit),
        };
    }
}
