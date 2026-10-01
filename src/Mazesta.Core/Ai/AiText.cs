namespace Mazesta.Core.Ai;

/// <summary>
/// A small model, once it repeats a block, tends to repeat it until it runs out of tokens (the same "choose one" list twenty times). The reply is
/// stopped at the third copy, and the copies after the first are cut from what is kept.
/// </summary>
public static class AiText
{
    private const int Tail = 48, MinLength = 160;

    /// <summary>Whether the text ends in a run it already wrote twice before.</summary>
    public static bool Looping(string text)
    {
        if (text.Length < MinLength) return false;
        string tail = text[^Tail..];
        int count = 0, at = 0;
        while ((at = text.IndexOf(tail, at, StringComparison.Ordinal)) >= 0) { count++; at += Tail; if (count >= 3) return true; }
        return false;
    }

    /// <summary>The text with each paragraph (a block between blank lines, or a line of 40 characters or more) kept only the first time it appears.</summary>
    public static string Unloop(string text)
    {
        var seen = new HashSet<string>(); var kept = new List<string>();
        foreach (var block in text.Replace("\r\n", "\n").Split("\n\n"))
        {
            string key = block.Trim();
            if (key.Length == 0) continue;
            if (!seen.Add(key)) continue;
            var lines = new List<string>();
            foreach (var line in block.Split('\n')) { string k = line.Trim(); if (k.Length >= 40 && !seen.Add("\u0001" + k)) continue; lines.Add(line); }
            if (lines.Count > 0) kept.Add(string.Join('\n', lines));
        }
        return string.Join("\n\n", kept).Trim();
    }
}
