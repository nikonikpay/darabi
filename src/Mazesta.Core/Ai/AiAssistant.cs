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

    /// <summary>
    /// What the model is told about itself. It may state only what a tool returned in the same answer; a test or a benchmark starts only when the
    /// user confirms it on the page. The rules on repeating are there because a small model copies its own earlier answers: a chat where "the RAM
    /// test passed" was said once gets "the CPU test passed" next, with no test run, unless it is told that every request is a new run.
    /// </summary>
    public const string SystemPrompt =
        "You are the assistant inside Mazesta Test, a PC diagnostics app used in a computer service shop. Answer in the language the user writes in " +
        "(Persian or English); write Persian in plain, correct words. Be brief and practical: one to four sentences or a short list, and never repeat a " +
        "sentence or a list. You reach this computer and this app only through your tools: read its machine summary, live sensors, saved reports and " +
        "benchmark history; check which professional programs it runs; run tests (cpu, memory, storage, network, gpu) and benchmarks; open a page of " +
        "the app or point at a control on it; turn the on-screen overlay on or off. " +
        "When the user asks to test, check or measure something, call run_tests or run_benchmark now, every time, with only the areas the user named " +
        "(RAM is memory; the graphics card is gpu). Each request is a new run: an earlier result in this chat is old, and an earlier refusal does not " +
        "stop you from asking again. The app asks the user to confirm on the page before anything starts; do not ask in words. " +
        "When the user asks to see a part of the app, call open_page with the page id from the list below (overclock and undervolt are tuning, not overlay). " +
        "To tell whether the computer got slower, run the benchmark and report its change against the earlier best. " +
        "State only what a tool returned or what this prompt says about the computer, with its numbers and outcome names exactly; copy names " +
        "(tests, programs, parts) as the tool wrote them. A test whose outcome is not Passed did not pass, and a declined or unstarted run gave no " +
        "result. Never say that a test ran or passed unless run_tests returned it in this answer. " +
        "If a tool returned nothing or an error, say so. Never invent numbers, sensor readings, results, pages or buttons. " +
        "The app has no list of games: for a game, give this computer's parts and do not say at which resolution or settings it runs.";

    /// <summary>The prompt with what the model is told of this computer (read by the app, so a question about the RAM or the processor is
    /// answered from it) and of the app's pages, by the names the pages show.</summary>
    public static string Prompt(string machine, string pages) => SystemPrompt + "\nThis computer: " + machine + "\nThe app's pages: " + pages;

    /// <summary>The smallest model the assistant runs: below 4B parameters the answers in Persian are not usable (the 0.8B one loses the thread
    /// within a few turns), so such a model stays on the AI page for its benchmark.</summary>
    public const long MinChatModelBytes = 2_000_000_000;

    /// <summary>A reply kept in the history is cut to this many characters: a long one that went wrong would otherwise be copied by the next.</summary>
    public const int HistoryReplyChars = 600;

    /// <summary>
    /// Whether a message asks the app to do something (test, measure, open, switch) rather than asks a question. For such a message the model's
    /// first turn must be a tool call: a small model otherwise answers "done" from the chat's history without running anything. A question that
    /// happens to match only makes the model read something, or ask for a run the user can decline.
    /// </summary>
    public static bool AsksToAct(string text)
    {
        var s = text.ToLowerInvariant().Replace('\u200c', ' ').Replace('ي', 'ی').Replace('ك', 'ک');
        return ActWords.Any(s.Contains);
    }
    private static readonly string[] ActWords =
    [
        "تست", "چک", "آزمایش", "ازمایش", "بنچ", "اجرا", "بسنج", "اندازه بگیر", "بررسی کن", "امتحان",
        "باز کن", "بازکن", "نشان بده", "نشون بده", "برو ", "برو به", "ببر ", "روشن", "خاموش", "فعال",
        "test", "check", "benchmark", "measure", "run ", "open", "show", "go to", "turn on", "turn off", "enable", "disable",
    ];

    /// <summary>The newest messages that fit <see cref="HistoryChars"/>, oldest first; always at least the last one. A message's length counts the
    /// tool results it carries (see <c>AiAgent.HistoryResultChars</c>).</summary>
    public static IReadOnlyList<T> Trim<T>(IReadOnlyList<T> history, Func<T, int> length)
    {
        int total = 0, start = history.Count;
        while (start > 0 && (start == history.Count || total + length(history[start - 1]) <= HistoryChars)) { start--; total += length(history[start]); }
        return [.. history.Skip(start)];
    }
}
