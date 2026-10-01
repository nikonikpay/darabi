using System.Text.Json; using System.Text.Json.Nodes;
namespace Mazesta.Core.Ai;

/// <summary>
/// A tool's result made small enough for the model's context without breaking it: cutting the text at a length left half an object, which the
/// page could not draw and the model misread. Here the JSON stays whole: long texts are shortened, and lists lose items (the unremarkable ones first:
/// a passed test, a note) until it fits, with the number left out said beside the list. The page and the history keep the full result.
/// </summary>
public static class AiJson
{
    private const int TextMax = 240;

    public static string Shrink(string json, int max)
    {
        if (json.Length <= max) return json;
        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch (JsonException) { return json[..max] + " …(cut)"; }
        if (root is null) return json[..max] + " …(cut)";
        ShortenTexts(root);
        string s = root.ToJsonString(Options);
        while (s.Length > max && Longest(root) is { } list)
        {
            int at = Plain(list.Array);
            list.Array.RemoveAt(at < 0 ? list.Array.Count - 1 : at);
            if (list.Owner is JsonObject o) o[list.Name + "Omitted"] = (o[list.Name + "Omitted"]?.GetValue<int>() ?? 0) + 1;
            s = root.ToJsonString(Options);
        }
        return s.Length <= max ? s : JsonSerializer.Serialize(new { error = "the result was too long to send", length = json.Length }, Options);
    }

    private static readonly JsonSerializerOptions Options = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void ShortenTexts(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                    if (o[key] is JsonValue v && v.TryGetValue(out string? t) && t.Length > TextMax) o[key] = t[..TextMax] + "…";
                    else if (o[key] is { } child) ShortenTexts(child);
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                    if (a[i] is JsonValue v && v.TryGetValue(out string? t) && t.Length > TextMax) a[i] = t[..TextMax] + "…";
                    else if (a[i] is { } child) ShortenTexts(child);
                break;
        }
    }

    private sealed record List(JsonArray Array, JsonNode? Owner, string Name);

    /// <summary>The list with the most text in it that still has more than one item.</summary>
    private static List? Longest(JsonNode root)
    {
        List? best = null; int bestLen = 0;
        void Walk(JsonNode? n, JsonNode? owner, string name)
        {
            if (n is JsonArray a)
            {
                int len = a.ToJsonString().Length;
                if (a.Count > 1 && len > bestLen) { best = new(a, owner, name); bestLen = len; }
                foreach (var x in a) Walk(x, null, "");
            }
            else if (n is JsonObject o) foreach (var p in o) Walk(p.Value, o, p.Key);
        }
        Walk(root, null, "");
        return best;
    }

    /// <summary>The last item that says nothing went wrong (a pass, a note, a good finding), or -1.</summary>
    private static int Plain(JsonArray a)
    {
        for (int i = a.Count - 1; i >= 0; i--)
            if (a[i] is JsonObject o && (Is(o, "outcome", "Passed") || Is(o, "level", "Good") || Is(o, "level", "Note") || Is(o, "level", "Info"))) return i;
        return -1;
        static bool Is(JsonObject o, string key, string value) => o[key] is JsonValue v && v.TryGetValue(out string? s) && s == value;
    }
}
