namespace Mazesta.Core.Ai;

public enum AiAssistantStatus { Available, NoGpu, LittleVram, NoRoom }

/// <summary>Whether this machine is offered the assistant, and with which model.</summary>
public sealed record AiAssistantChoice(AiAssistantStatus Status, AiModel? Model);

/// <summary>
/// The rule for the chat assistant: only a computer with a graphics card of at least 4 GB of its own memory is offered it (a slower way of running
/// a language model is not worth the wait in a service shop), and the model is chosen for it, never by the user from the whole catalog. Both
/// are Qwen3, which writes Persian; the bigger one only where it runs wholly on the GPU with room to spare.
/// </summary>
public static class AiAssistantPolicy
{
    /// <summary>A "4 GB" card reports 4095 or 4096 MiB; the threshold leaves room for that.</summary>
    public const long MinVramBytes = (long)(3.9 * AiFitter.Gib);
    public const string BaseModelId = "qwen3-4b", LargeModelId = "qwen3-14b";
    /// <summary>The longest history sent to the model (characters): the server's context is <see cref="AiFitter.Context"/> tokens and Persian costs about two characters a token.</summary>
    public const int HistoryChars = 4000;
    public const int MaxReplyTokens = 700;
    /// <summary>The server's context: the conversation, the tools' definitions and their results (see <c>AiAgent.MaxResultChars</c>) share it.</summary>
    public const int ServerContext = 6144;

    public static AiAssistantChoice Decide(AiMachine pc)
    {
        if (pc.VramBytes is not { } vram || vram <= 0) return new(AiAssistantStatus.NoGpu, null);
        if (vram < MinVramBytes) return new(AiAssistantStatus.LittleVram, null);
        var large = AiCatalog.Find(LargeModelId)!; var small = AiCatalog.Find(BaseModelId)!;
        var f = AiFitter.Fit(large, pc);
        if (f.Mode == AiFitMode.Gpu && !f.Tight) return new(AiAssistantStatus.Available, large);
        return AiFitter.Fit(small, pc).Mode is AiFitMode.Gpu or AiFitMode.Split ? new(AiAssistantStatus.Available, small) : new(AiAssistantStatus.NoRoom, null);
    }

    /// <summary>What the model is told about itself. It may state only what a tool returned, and it says what it can not do (it can not run tests yet).</summary>
    public const string SystemPrompt =
        "You are the assistant inside Mazesta Test, a PC diagnostics app used in a computer service shop. Answer in the language the user writes in " +
        "(Persian or English); write Persian in plain, correct words. Be brief and practical. You can read this computer's data only through your tools " +
        "(machine summary, live sensors, saved reports, benchmark history): call one when the question needs it. State only what a tool returned; if it " +
        "returned nothing or an error, say so. Never invent numbers, sensor readings or results, and never say that you ran a test or a benchmark: you can " +
        "not run them yet. When the user wants something tested, tell them which page of the app does it (Tests, Benchmarks, Check-up).";

    /// <summary>The newest messages that fit <see cref="HistoryChars"/>, oldest first; always at least the last one.</summary>
    public static IReadOnlyList<T> Trim<T>(IReadOnlyList<T> history, Func<T, int> length)
    {
        int total = 0, start = history.Count;
        while (start > 0 && (start == history.Count || total + length(history[start - 1]) <= HistoryChars)) { start--; total += length(history[start]); }
        return [.. history.Skip(start)];
    }
}
