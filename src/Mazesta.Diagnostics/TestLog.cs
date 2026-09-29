using System.Globalization;
namespace Mazesta.Diagnostics;

public enum TestLogLevel { Info, Step, Warning, Error }

/// <summary>
/// One line of the live test log: what a test is doing right now, in the technician's language, with the formula or command behind it.
/// <see cref="Key"/> is a localisation key and <see cref="Args"/> its values, already formatted (numbers in invariant culture); an argument
/// that starts with '@' is itself a key (a test's name, an outcome) and is translated too. <see cref="Formula"/> is Latin text shown as is:
/// the arithmetic being checked, or the command line being run. The log describes; it never decides a result.
/// </summary>
public sealed record TestLogEntry(DateTimeOffset At, TestId? Test, TestLogLevel Level, string Key, IReadOnlyList<string> Args, string? Formula = null);

public static class TestLog
{
    /// <summary>Adds a line to the run's log (nothing when nobody listens). Numbers are formatted invariantly; strings pass through.</summary>
    public static void Note(this TestExecutionRequest request, string key, string? formula = null, params object?[] args)
        => request.Log?.Invoke(new(request.Clock.UtcNow, null, TestLogLevel.Step, key, Format(args), formula));

    public static void NoteWarning(this TestExecutionRequest request, string key, string? formula = null, params object?[] args)
        => request.Log?.Invoke(new(request.Clock.UtcNow, null, TestLogLevel.Warning, key, Format(args), formula));

    public static void NoteError(this TestExecutionRequest request, string key, string? formula = null, params object?[] args)
        => request.Log?.Invoke(new(request.Clock.UtcNow, null, TestLogLevel.Error, key, Format(args), formula));

    internal static string[] Format(object?[] args) => [.. args.Select(a => a switch
    {
        null => "",
        double d => d.ToString(Math.Abs(d) >= 100 ? "F0" : Math.Abs(d) >= 1 ? "F1" : "G3", CultureInfo.InvariantCulture),
        float f => ((double)f).ToString("F1", CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => a.ToString() ?? "",
    })];
}

/// <summary>Says "now" at most once per <paramref name="every"/>, for the progress lines of a loop that runs thousands of times a second.</summary>
public sealed class LogPacer(TimeSpan every)
{
    private long _next = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(every.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
    public LogPacer() : this(TimeSpan.FromSeconds(5)) { }
    public bool Due()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now < Interlocked.Read(ref _next)) return false;
        Interlocked.Exchange(ref _next, now + (long)(every.TotalSeconds * System.Diagnostics.Stopwatch.Frequency));
        return true;
    }
}
