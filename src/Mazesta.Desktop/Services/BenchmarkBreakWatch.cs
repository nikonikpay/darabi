using System.IO; using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics; using Mazesta.Diagnostics.Benchmarks;
using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Services;

/// <summary>
/// A benchmark the computer did not live through. While one runs, a small file names it and when it started; a run that ends, however it ends,
/// removes it. Found at the next start, the run was cut off - the app closed, or Windows restarted, stopped on a blue screen or lost power - and
/// <see cref="Message"/> says which benchmark it was and why, as far as Windows' own logs tell (<see cref="SessionBreak"/>, never guessed). The
/// test queue keeps its own checkpoint for the same purpose.
/// </summary>
public sealed class BenchmarkBreakWatch
{
    private sealed record Mark(string Id, string NameKey, DateTimeOffset StartedAt);
    private readonly string _file;
    private readonly ILogger _log;

    /// <summary>What was found at this start, in the user's language, or null.</summary>
    public string? Message { get; }

    public BenchmarkBreakWatch(BenchmarkRunner runner, string sessionsDir, IBreakEventSource? breaks, ILogger log)
    {
        _file = Path.Combine(sessionsDir, "benchmark-running.json"); _log = log;
        Message = Read(breaks);
        runner.Started += (b, _) => Write(new(b.Definition.Id.Value, b.Definition.NameKey, DateTimeOffset.Now));
        runner.BusyChanged += busy => { if (!busy) Clear(); };
    }

    private string? Read(IBreakEventSource? breaks)
    {
        Mark? mark;
        try { mark = File.Exists(_file) ? JsonSerializer.Deserialize<Mark>(File.ReadAllText(_file)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _log.LogWarning(e, "Could not read the benchmark marker"); mark = null; }
        Clear();
        if (mark is null) return null;
        string text = Loc.Format("Bench_Break", Loc.Get(mark.NameKey), mark.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        if (breaks is not null)
            try
            {
                var why = SessionBreak.Classify(mark.StartedAt, breaks.BootTime, breaks.Since(mark.StartedAt.AddSeconds(-15)));
                text += " " + (why.Code is { } code ? Loc.Format("Test_Break_BlueScreenCode", code) : Loc.Get($"Test_Break_{why.Cause}"));
                if (why.DisplayResets > 0) text += " " + Loc.Format("Test_Break_Tdr", why.DisplayResets);
            }
            catch (Exception e) when (e is System.Diagnostics.Eventing.Reader.EventLogException or UnauthorizedAccessException) { }   // no reason is better than a guessed one
        _log.LogWarning("A benchmark was cut off: {Message}", text);
        return text;
    }

    private void Write(Mark m)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file, JsonSerializer.Serialize(m)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not write the benchmark marker"); }
    }

    private void Clear()
    {
        try { File.Delete(_file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning(e, "Could not remove the benchmark marker"); }
    }
}
