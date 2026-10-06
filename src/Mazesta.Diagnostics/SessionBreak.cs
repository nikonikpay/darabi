using System.Diagnostics.Eventing.Reader; using System.Text.RegularExpressions;
namespace Mazesta.Diagnostics;

/// <summary>Why a test session ended without finishing, as far as Windows' logs tell.</summary>
public enum BreakCause { AppClosed, AppCrashed, Restarted, PowerLoss, BlueScreen }

/// <param name="Code">The stop code of a blue screen (0x…), when Windows logged it.</param>
/// <param name="DisplayResets">How many times the display driver was reset (TDR) in the same window: a GPU that stopped answering.</param>
public sealed record SessionBreakInfo(BreakCause Cause, string? Code, int DisplayResets);

/// <summary>One event of the System or Application log that can explain a break.</summary>
public sealed record BreakEvent(DateTimeOffset Time, string Provider, int Id, string? Text);

public interface IBreakEventSource
{
    /// <summary>The events that can explain a break, logged from <paramref name="since"/> on.</summary>
    IReadOnlyList<BreakEvent> Since(DateTimeOffset since);
    /// <summary>When Windows last started.</summary>
    DateTimeOffset BootTime { get; }
}

/// <summary>
/// Tells why a session stopped, from what Windows logged after the checkpoint was last saved (so from facts, not a guess): a bug check (blue
/// screen, with its stop code) before an unexpected restart (Kernel-Power 41 or EventLog 6008, which also mark a power loss, a reset button or
/// a hang that was forced off), a normal restart after it, a crash of the app itself (Application Error 1000 or .NET Runtime 1026 naming it),
/// and otherwise the app was closed or ended while Windows kept running. Display-driver resets (TDR, Display 4101) are counted beside it.
/// </summary>
public static partial class SessionBreak
{
    /// <summary>The app's exe names in the event text: the users' edition, Mazesta's own, and the name before the rename.</summary>
    public static readonly string[] AppExes = ["Mazesta.exe", "Mazesta-Admin.exe", "MazestaWeb.exe"];

    public static SessionBreakInfo Classify(DateTimeOffset lastSaved, DateTimeOffset bootTime, IReadOnlyList<BreakEvent> events)
    {
        var after = events.Where(e => e.Time >= lastSaved.AddSeconds(-15)).ToList();
        int tdr = after.Count(e => e.Provider == "Display" && e.Id == 4101);
        if (after.FirstOrDefault(e => e.Id == 1001 && e.Provider.Contains("WER-SystemErrorReporting", StringComparison.Ordinal)) is { } bug)
            return new(BreakCause.BlueScreen, bug.Text is { } t && StopCode().Match(t) is { Success: true } m ? m.Value : null, tdr);
        if (after.Any(e => (e.Provider == "Microsoft-Windows-Kernel-Power" && e.Id == 41) || (e.Provider == "EventLog" && e.Id == 6008))) return new(BreakCause.PowerLoss, null, tdr);
        if (bootTime > lastSaved) return new(BreakCause.Restarted, null, tdr);
        if (after.Any(e => (e.Provider == "Application Error" && e.Id == 1000 || e.Provider == ".NET Runtime" && e.Id == 1026) && e.Text is { } txt && AppExes.Any(x => txt.Contains(x, StringComparison.OrdinalIgnoreCase))))
            return new(BreakCause.AppCrashed, null, tdr);
        return new(BreakCause.AppClosed, null, tdr);
    }

    [GeneratedRegex(@"0x[0-9A-Fa-f]{8}\b")] private static partial Regex StopCode();
}

/// <summary>Reads the few events <see cref="SessionBreak"/> needs from the System and Application logs.</summary>
public sealed class WindowsBreakEventSource : IBreakEventSource
{
    public DateTimeOffset BootTime => DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    public IReadOnlyList<BreakEvent> Since(DateTimeOffset since)
    {
        string time = $"TimeCreated[@SystemTime >= '{since.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']";
        var list = new List<BreakEvent>();
        Read(list, "System", $"*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='EventLog'] and EventID=6008) or (Provider[@Name='Display'] and EventID=4101) or (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001)) and {time}]]");
        Read(list, "Application", $"*[System[((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='.NET Runtime'] and EventID=1026)) and {time}]]");
        return list;
    }

    private static void Read(List<BreakEvent> list, string log, string query)
    {
        using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, query));
        for (var r = reader.ReadEvent(); r is not null && list.Count < 500; r = reader.ReadEvent())
            using (r)
            {
                string? text;
                try { text = r.FormatDescription(); } catch (EventLogException) { text = null; }
                text ??= string.Join(" ", r.Properties.Select(p => p.Value?.ToString()));
                list.Add(new(r.TimeCreated ?? default, r.ProviderName, r.Id, text));
            }
    }
}
