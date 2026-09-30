using System.Text.Json;
namespace Mazesta.Diagnostics.Ai;

/// <summary>A message of a kept chat: who wrote it, the words, and (an answer's) the tools it called with what they returned.</summary>
public sealed class AiChatMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public List<ToolExchange> Tools { get; set; } = [];
    public ChatTurn Turn => new(Role, Text, Tools);
}

public sealed class AiChat
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
    public List<AiChatMessage> Messages { get; set; } = [];
}

/// <summary>A kept chat as the history list shows it.</summary>
public sealed record AiChatInfo(string Id, string Title, DateTimeOffset Updated, int Count);

/// <summary>
/// The assistant's past chats, one JSON file each in <c>Data/ai/chats</c>, on this computer only; the user deletes them one by one or all at
/// once. The newest <see cref="Keep"/> are kept. A damaged file is left out of the list, never shown half-read.
/// </summary>
public sealed class AiChats
{
    public const int Keep = 100, TitleChars = 60;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _dir; private readonly object _lock = new();
    private Dictionary<string, AiChatInfo>? _index;

    public AiChats(string aiRoot) => _dir = Path.Combine(aiRoot, "chats");

    public static AiChat New(DateTimeOffset now) => new() { Id = now.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture), Created = now, Updated = now };

    /// <summary>The first line of the first question, cut at a word.</summary>
    public static string TitleOf(string text)
    {
        var s = text.Trim().Split('\n')[0].Trim();
        if (s.Length <= TitleChars) return s;
        int cut = s.LastIndexOf(' ', TitleChars - 1);
        return s[..(cut > 20 ? cut : TitleChars)].TrimEnd() + "…";
    }

    public IReadOnlyList<AiChatInfo> List() { lock (_lock) return [.. Index().Values.OrderByDescending(c => c.Updated)]; }

    public AiChat? Load(string id)
    {
        if (!Valid(id)) return null;
        try { return JsonSerializer.Deserialize<AiChat>(File.ReadAllText(PathOf(id)), Json); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Writes the chat (a chat with no message is not kept) and drops the oldest past <see cref="Keep"/>.</summary>
    public void Save(AiChat chat)
    {
        if (!Valid(chat.Id) || chat.Messages.Count == 0) return;
        if (chat.Title.Length == 0) chat.Title = TitleOf(chat.Messages.FirstOrDefault(m => m.Role == "user")?.Text ?? "");
        lock (_lock)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathOf(chat.Id), JsonSerializer.Serialize(chat, Json));
            var index = Index(); index[chat.Id] = Info(chat);
            foreach (var old in index.Values.OrderByDescending(c => c.Updated).Skip(Keep).ToList()) Remove(old.Id);
        }
    }

    public void Delete(string id) { if (Valid(id)) lock (_lock) Remove(id); }

    public void DeleteAll() { lock (_lock) foreach (var id in Index().Keys.ToList()) Remove(id); }

    private void Remove(string id)
    {
        try { File.Delete(PathOf(id)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        _index?.Remove(id);
    }

    private Dictionary<string, AiChatInfo> Index()
    {
        if (_index is not null) return _index;
        _index = [];
        if (!Directory.Exists(_dir)) return _index;
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
            if (Load(Path.GetFileNameWithoutExtension(f)) is { } c) _index[c.Id] = Info(c);
        return _index;
    }

    private static AiChatInfo Info(AiChat c) => new(c.Id, c.Title, c.Updated, c.Messages.Count);
    private string PathOf(string id) => Path.Combine(_dir, id + ".json");
    /// <summary>An id is the app's own stamp (digits and dashes); anything else from the page is refused, so it can never name another file.</summary>
    private static bool Valid(string id) => id.Length is > 0 and <= 40 && id.All(ch => char.IsAsciiDigit(ch) || ch == '-');
}
