using System.Diagnostics.Eventing.Reader;
namespace Mazesta.Diagnostics.Whea;

public sealed record HardwareErrorEvent(DateTimeOffset Time, int EventId, string Summary);

/// <summary>Windows' own record of hardware faults that happened while a test ran - the seam that lets the
/// engine be tested without a machine that has faults.</summary>
public interface IHardwareErrorSource { IReadOnlyList<HardwareErrorEvent> Since(DateTimeOffset start); }

/// <summary>
/// Reads <c>Microsoft-Windows-WHEA-Logger</c> events from the System log. WHEA (Windows Hardware Error
/// Architecture) is where the CPU, memory controller and PCIe report corrected and uncorrected machine
/// errors - the kind a stress test provokes and a passing checksum cannot see. HWiNFO shows the same count
/// as "Windows Hardware Errors (WHEA)"; a nonzero value after a test is strong evidence against the machine.
/// </summary>
public sealed class WheaErrorSource : IHardwareErrorSource
{
    private const int MaxEvents = 200;

    public IReadOnlyList<HardwareErrorEvent> Since(DateTimeOffset start)
    {
        string query = $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime >= '{start.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query));
        var events = new List<HardwareErrorEvent>();
        for (var record = reader.ReadEvent(); record is not null && events.Count < MaxEvents; record = reader.ReadEvent())
            using (record) events.Add(new(record.TimeCreated ?? default, record.Id, Summarize(record)));
        return events;
    }

    private static string Summarize(EventRecord record)
    {
        try { return record.FormatDescription()?.ReplaceLineEndings(" ").Trim() is { Length: > 0 } text ? (text.Length > 160 ? text[..160] : text) : $"event {record.Id}"; }
        catch (EventLogException) { return $"event {record.Id}"; }
    }
}
