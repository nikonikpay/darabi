using System.Text.Json; using System.Text.Json.Nodes;
namespace Mazesta.Persistence;

/// <summary>One line of the usage log: when, what kind of thing (<c>bench.run</c>, <c>tuning.auto</c>...) and its figures. Never a name, a path or anything typed.</summary>
public sealed record UsageLine(long Seq, string Json);

/// <summary>
/// What the app did, kept on this computer in <c>Data/logs/usage.jsonl</c>, a line per event: the tests and benchmarks run and their figures, the GPU tuning searches and
/// what they found (or why they failed), the assistant's use as a count. It is the owner's own record whatever the settings say, and it is what the settings'
/// "send usage statistics" switch sends (the lines after the last one sent), so what leaves the computer can be read here first. Beside it, in
/// <c>Data/config/usage.json</c>, is the installation's random id (made here, tied to nothing about the computer or its user) and how far the sending got.
/// The file is trimmed to its newest half when it passes 3 MB.
/// </summary>
public sealed class UsageLog(AppPaths paths)
{
    private const long MaxBytes = 3L << 20;
    private readonly object _lock = new(); private long _last; private UsageState? _state;
    public string File => Path.Combine(paths.LogsDir, "usage.jsonl");
    private string StateFile => Path.Combine(paths.ConfigDir, "usage.json");
    private sealed record UsageState(string InstallId, DateTimeOffset Created, long Sent);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    private UsageState State()
    {
        if (_state is not null) return _state;
        try { if (System.IO.File.Exists(StateFile) && JsonSerializer.Deserialize<UsageState>(System.IO.File.ReadAllText(StateFile), Options) is { InstallId.Length: 32 } s) return _state = s; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return _state = Save(new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, 0));
    }
    private UsageState Save(UsageState s)
    {
        try { Directory.CreateDirectory(paths.ConfigDir); System.IO.File.WriteAllText(StateFile, JsonSerializer.Serialize(s, Options)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return s;
    }

    /// <summary>The installation's random id: how many installations there are is counted by it, and nothing else.</summary>
    public string InstallId { get { lock (_lock) return State().InstallId; } }

    /// <summary>Adds an event. A failure to write is not the app's failure: the event is dropped.</summary>
    public void Append(string kind, JsonObject? data = null)
    {
        try
        {
            lock (_lock)
            {
                _last = Math.Max(_last + 1, DateTime.UtcNow.Ticks);
                var line = new JsonObject { ["s"] = _last, ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["k"] = kind };
                if (data is { Count: > 0 }) line["d"] = data;
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > MaxBytes) Trim();
                System.IO.File.AppendAllText(File, line.ToJsonString() + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void Trim()
    {
        var lines = System.IO.File.ReadAllLines(File);
        System.IO.File.WriteAllLines(File, lines.Skip(lines.Length / 2));
    }

    /// <summary>The lines not sent yet, oldest first, at most <paramref name="max"/>.</summary>
    public IReadOnlyList<UsageLine> Unsent(int max)
    {
        var result = new List<UsageLine>();
        try
        {
            long sent; lock (_lock) sent = State().Sent;
            if (!System.IO.File.Exists(File)) return result;
            foreach (string text in System.IO.File.ReadLines(File))
            {
                if (text.Length == 0 || JsonNode.Parse(text) is not JsonObject o || o["s"] is not JsonValue v || !v.TryGetValue(out long seq) || seq <= sent) continue;
                result.Add(new(seq, text)); if (result.Count >= max) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return result;
    }

    public void MarkSent(long seq) { lock (_lock) { var s = State(); if (seq > s.Sent) _state = Save(s with { Sent = seq }); } }
}
