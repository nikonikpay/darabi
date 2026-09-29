using System.Diagnostics.Eventing.Reader; using System.Text.RegularExpressions;
namespace Mazesta.Diagnostics.Storage;

/// <summary>One storage event Windows logged: who logged it, its id and level, and the device it names (\Device\Harddisk2\DR2, RaidPort1).</summary>
public sealed record StorageEvent(DateTimeOffset Time, string Provider, int EventId, string Level, string? Device, string Summary);

public interface IStorageEventSource { IReadOnlyList<StorageEvent> Since(DateTimeOffset start); }

/// <summary>
/// The storage path's own complaints from the System log while a test ran: the disk class driver (7 bad block, 11 controller error, 15 not
/// ready, 51 paging error, 153 I/O retried, 154 hardware error, 157 surprise removal), the port and miniport drivers (129 reset to device, 9
/// timeout: storport, stornvme, storahci, Intel RST) and NTFS (55, 98: file-system damage). None of them alone proves a failing drive - a
/// reset or retry also comes from a cable, a controller, a driver or the power - so they are evidence reported with the test, never its verdict.
/// </summary>
public sealed partial class StorageEventSource : IStorageEventSource
{
    private const int MaxEvents = 200;
    private static readonly string[] Providers = ["disk", "Disk", "Microsoft-Windows-StorPort", "stornvme", "storahci", "iaStorA", "iaStorAC", "iaStorAVC", "iaStorVD", "Ntfs", "Microsoft-Windows-Ntfs"];
    [GeneratedRegex(@"\\Device\\Harddisk\d+\\DR\d+|\bDisk \d+\b|RaidPort\d+|\bvolume [A-Z]:")] private static partial Regex DeviceName();
    internal static string? DeviceIn(string text) => DeviceName().Match(text) is { Success: true } m ? m.Value : null;

    public IReadOnlyList<StorageEvent> Since(DateTimeOffset start)
    {
        string providers = string.Join(" or ", Providers.Select(p => $"@Name='{p}'"));
        string query = $"*[System[Provider[{providers}] and (Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '{start.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query));
        var events = new List<StorageEvent>();
        for (var record = reader.ReadEvent(); record is not null && events.Count < MaxEvents; record = reader.ReadEvent())
            using (record)
            {
                string text = Describe(record);
                events.Add(new(record.TimeCreated ?? default, record.ProviderName, record.Id, record.Level switch { 1 => "Critical", 2 => "Error", _ => "Warning" }, DeviceIn(text), text));
            }
        return events;
    }

    /// <summary>"disk 153 ×3 (\Device\Harddisk2\DR2), stornvme 129 ×1 (RaidPort1)": one entry per provider, event and device.</summary>
    public static string Summarize(IReadOnlyList<StorageEvent> events)
        => string.Join(", ", events.GroupBy(e => (e.Provider, e.EventId, e.Device)).Select(g => $"{g.Key.Provider} {g.Key.EventId} ×{g.Count()}" + (g.Key.Device is { } d ? $" ({d})" : "")));

    private static string Describe(EventRecord record)
    {
        try { return record.FormatDescription()?.ReplaceLineEndings(" ").Trim() is { Length: > 0 } text ? (text.Length > 200 ? text[..200] : text) : $"event {record.Id}"; }
        catch (EventLogException) { return $"event {record.Id}"; }
    }
}
