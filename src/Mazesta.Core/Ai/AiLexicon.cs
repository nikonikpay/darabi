using System.Reflection; using System.Text.Json;
namespace Mazesta.Core.Ai;

/// <summary>The words people type to the assistant, read from the embedded <c>ai-lexicon.json</c> so a new way of asking is a line of data, not code.
/// A word list missing from the file is a build mistake and fails at start (the tests load it), never an assistant that silently understands less.</summary>
internal sealed class AiLexicon
{
    private readonly Dictionary<string, string[]> _lists, _places;
    private readonly Dictionary<string, Dictionary<string, string[]>> _groups;
    private AiLexicon(Dictionary<string, string[]> lists, Dictionary<string, Dictionary<string, string[]>> groups, Dictionary<string, string[]> places) { _lists = lists; _groups = groups; _places = places; }

    public static AiLexicon Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mazesta.Core.Ai.ai-lexicon.json") ?? throw new InvalidOperationException("ai-lexicon.json is not embedded.");
        var d = JsonSerializer.Deserialize<Shape>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("ai-lexicon.json is empty.");
        return new(d.Lists, d.Groups, d.Places);
    }
    private sealed record Shape(Dictionary<string, string[]> Lists, Dictionary<string, Dictionary<string, string[]>> Groups, Dictionary<string, string[]> Places);

    public string[] List(string name) => _lists.TryGetValue(name, out var w) ? w : throw new InvalidOperationException($"ai-lexicon.json has no list {name}.");
    public (string Key, string[] Words)[] Group(string name) => _groups.TryGetValue(name, out var g) ? [.. g.Select(x => (x.Key, x.Value))] : throw new InvalidOperationException($"ai-lexicon.json has no group {name}.");
    public string[] Places(string page, string? target = null) => _places.TryGetValue(target is null ? page : page + "/" + target, out var w) ? w : throw new InvalidOperationException($"ai-lexicon.json has no words for {page}/{target}.");
    public IEnumerable<string> PlaceKeys => _places.Keys;
}
