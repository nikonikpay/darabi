namespace Mazesta.Diagnostics;

public enum TestOptionKind { Choice, Integer, Text }

public sealed record OptionChoice(string Value, string Label);

/// <summary>One thing the technician may set for a test beyond duration and repeat (which drive, how many
/// MB, which GPU). Declared by the test itself, so the Test Center renders any test's options without
/// knowing what they mean. <see cref="Choices"/> is a function, evaluated when the page is built, because a
/// list of drives or adapters is a fact about this machine right now, not a constant.</summary>
public sealed record TestOption(string Key, string LabelKey, TestOptionKind Kind, string Default, Func<IReadOnlyList<OptionChoice>>? Choices = null);

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
