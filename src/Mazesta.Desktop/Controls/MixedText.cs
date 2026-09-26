using System.Windows; using System.Windows.Controls; using System.Windows.Documents; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;

/// <summary>
/// A Persian sentence with Latin numbers in it ("مرحله‌ی ۳: هسته +120 MHz"). The app font draws every ASCII digit as a Persian digit, and a
/// Latin font draws the Persian words in a foreign face; so the text is split into runs, each in its own font: Persian letters in the app
/// font, Latin letters and digits (with the spaces and signs between them) in the Latin font. The paragraph keeps the page's direction, so the
/// sentence reads right to left; callers keep each number with its unit through left-to-right marks (TuningViewModel.Ltr).
/// Set <c>controls:MixedText.Text</c> on a TextBlock instead of Text.
/// </summary>
public static class MixedText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(MixedText), new PropertyMetadata(null, OnText));
    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static void OnText(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        if (e.NewValue is not string text || text.Length == 0) return;
        var latin = (FontFamily)Application.Current.FindResource("App.Font.Latin");
        foreach (var (segment, isLatin) in Split(text)) block.Inlines.Add(isLatin ? new Run(segment) { FontFamily = latin } : new Run(segment));
    }

    /// <summary>The text cut where it changes between Persian and Latin. Characters of neither (spaces, punctuation, direction marks) stay with
    /// the run they follow, so "348 W" stays one Latin run.</summary>
    internal static IEnumerable<(string Segment, bool Latin)> Split(string text)
    {
        int start = 0; bool? current = null;
        for (int i = 0; i < text.Length; i++)
        {
            bool? kind = IsPersian(text[i]) ? false : char.IsAsciiLetterOrDigit(text[i]) || text[i] is '°' or 'µ' ? true : null;
            if (kind is null || kind == current) continue;
            if (current is not null && i > start) { yield return (text[start..i], current.Value); start = i; }
            current = kind;
        }
        if (start < text.Length) yield return (text[start..], current ?? false);
    }

    private static bool IsPersian(char c) => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';
}
