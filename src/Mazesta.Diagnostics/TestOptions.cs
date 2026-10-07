namespace Mazesta.Diagnostics;

public enum TestOptionKind { Choice, Integer, Text }

/// <param name="Localized">The label is a localisation key (a fixed choice such as a load pattern), not data such as a drive name.</param>
/// <param name="Summary">What the choice means in plain technical terms, the same in every language: it is what a finished run keeps as "how it was set up".</param>
public sealed record OptionChoice(string Value, string Label, bool Localized = false, string? Summary = null);

/// <summary>One thing the technician may set for a test beyond duration and repeat (which drive, how many
/// MB, which GPU). Declared by the test itself, so the Test Center renders any test's options without
/// knowing what they mean. <see cref="Choices"/> is a function, evaluated when the page is built, because a
/// list of drives or adapters is a fact about this machine right now, not a constant.</summary>
/// <param name="Preferred">What the option starts at on this machine when that differs from <see cref="Default"/> (ray tracing on where the card has it): asked when the page is built; null or a failure leaves the default.</param>
/// <param name="When">"key=value" (or "key=a|b"): the option only matters while that other option of the same test has one of those values,
/// and the page shows it only then (the steady load has no high and low percentages to set). Null: it always matters.</param>
public sealed record TestOption(string Key, string LabelKey, TestOptionKind Kind, string Default, Func<IReadOnlyList<OptionChoice>>? Choices = null, string? When = null, Func<string>? Preferred = null)
{
    /// <summary>Whether the option matters with the values <paramref name="valueOf"/> gives for the test's other options.</summary>
    public bool Applies(Func<string, string?> valueOf)
    {
        if (When is null) return true;
        int cut = When.IndexOf('=');
        return cut > 0 && valueOf(When[..cut]) is { } now && When[(cut + 1)..].Split('|').Contains(now);
    }
}

/// <summary>The values chosen for one run. Anything not chosen falls back to the option's declared default,
/// so an executor never sees a missing key.</summary>
public sealed class TestOptions(TestDefinition definition, IReadOnlyDictionary<string, string>? chosen)
{
    public static TestOptions None(TestDefinition definition) => new(definition, null);

    public string Get(string key)
        => chosen?.GetValueOrDefault(key) is { Length: > 0 } v ? v
         : definition.Options.FirstOrDefault(o => o.Key == key)?.Default ?? throw new KeyNotFoundException($"'{definition.Id}' declares no option '{key}'.");

    public int GetInt(string key) => int.TryParse(Get(key), out int v) ? v : throw new FormatException($"Option '{key}' of '{definition.Id}' is not a whole number: '{Get(key)}'.");
}
