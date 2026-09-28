namespace Mazesta.Diagnostics;

/// <summary>Why a test cannot run on this machine at all: a localisation key for the page, and a technical detail for the log.</summary>
public sealed record Unavailability(string ReasonKey, string Detail);

/// <summary>
/// A test or benchmark that can tell before it runs that this machine cannot run it (no GPU with hardware ray tracing, no AVX2...). The
/// page then shows the row disabled with the reason instead of offering a run that can only end Unsupported. It is asked with the options
/// chosen now, because the answer can depend on them (which GPU). The run itself still checks: a missing feature is Unsupported, never a pass.
/// </summary>
public interface ITestAvailability
{
    /// <returns>Null when the test can run with these options.</returns>
    Unavailability? CheckAvailability(TestOptions options);
}

public static class TestAvailability
{
    /// <summary>Asks <paramref name="test"/> if it can say; a check that throws is treated as available, so a failing probe never hides a test.</summary>
    public static Unavailability? Check(object? test, TestDefinition definition, IReadOnlyDictionary<string, string>? chosen)
    {
        if (test is not ITestAvailability a) return null;
        try { return a.CheckAvailability(new TestOptions(definition, chosen)); }
        catch (Exception) { return null; }
    }
}
