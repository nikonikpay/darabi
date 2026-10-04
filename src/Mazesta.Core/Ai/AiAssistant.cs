namespace Mazesta.Core.Ai;

public enum AiAssistantStatus { Available, NoGpu, LittleVram, NoRoom }

/// <summary>Whether this machine is offered the assistant, and with which model.</summary>
public sealed record AiAssistantChoice(AiAssistantStatus Status, AiModel? Model);

/// <summary>
/// The rule for the chat assistant: only a computer with a graphics card of at least 4 GB of its own memory is offered it (a slower way of running
/// a language model is not worth the wait in a service shop), and the model is chosen for it: Qwen3.5 4B, a 3 GB download. Compared on the owner's
/// RTX 3090 (2026-10-01) with the same Persian questions and the app's tools, it wrote better Persian than Qwen3 4B and called the right tools more
/// often than Gemma 4 E2B/E4B and Qwen3 14B (which answered like it, at three times the size); Qwen3.5 0.8B wrote nonsense. A larger model the user
/// downloaded can still be picked (see the assistant's model list).
/// </summary>
public static class AiAssistantPolicy
{
    /// <summary>A "4 GB" card reports 4095 or 4096 MiB; the threshold leaves room for that.</summary>
    public const long MinVramBytes = (long)(3.9 * AiFitter.Gib);
    public const string BaseModelId = "qwen3.5-4b", LargeModelId = "qwen3-14b";
    /// <summary>The longest history sent to the model (characters): the server's context is <see cref="ServerContext"/> tokens and Persian costs about two characters a token.</summary>
    public const int HistoryChars = 4000;
    public const int MaxReplyTokens = 700;
    /// <summary>The server's context: the conversation, the tools' definitions and their results (see <c>AiAgent.MaxResultChars</c>) share it.</summary>
    public const int ServerContext = 6144;

    public static AiAssistantChoice Decide(AiMachine pc)
    {
        if (pc.VramBytes is not { } vram || vram <= 0) return new(AiAssistantStatus.NoGpu, null);
        if (vram < MinVramBytes) return new(AiAssistantStatus.LittleVram, null);
        var small = AiCatalog.Find(BaseModelId)!;
        return AiFitter.Fit(small, pc, ServerContext).Mode is AiFitMode.Gpu or AiFitMode.Split ? new(AiAssistantStatus.Available, small) : new(AiAssistantStatus.NoRoom, null);
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
        "the app or point at a control on it; turn the on-screen overlay over games on or off; turn the tray monitor (the icon by the Windows clock that " +
        "warns about heat in the background) on or off and set the temperature it warns at; open a window of Windows from a fixed list (This PC, " +
        "Device Manager, Task Manager, Disk Management…); give Windows commands from the app's checked list (windows_command) and run the ones that " +
        "only read (run_windows_command); check the drivers (check_drivers; installing is on the Drivers page). Never write a Windows command that windows_command did not return: a wrong one can damage the system; if " +
        "it has none, say the app has no checked command for it. You can not run anything else in Windows, change files or install programs. " +
        "When the user asks to test, check or measure something, call run_tests or run_benchmark now, every time, with only the areas the user named " +
        "(RAM is memory; the graphics card is gpu); several benchmarks go in one run_benchmark call, never one call each. Each request is a new run: an earlier result in this chat is old, and an earlier refusal does not " +
        "stop you from asking again. The app asks the user to confirm on the page before anything starts; do not ask in words. " +
        "run_tests: all=true when every test of an area is asked for, together=true to load processor, memory and graphics card at once, minutes for a length; " +
        "list_tests gives every test and benchmark with its options. After run_tests, tell each outcome and, from its judgment, each part's highest temperature, " +
        "whether it was fully used (usedFullPower) and its findings; call a temperature fine only when a finding says so. " +
        "When the user asks to see a part of the app, call open_page with the page id from the list below (overclock and undervolt are tuning, not overlay). " +
        "To tell whether the computer got slower, run the benchmark and report its change against the earlier best. " +
        "When the user asks to diagnose, check up or troubleshoot the computer or the system as a whole (\"عیب یابی کن\", \"سیستم رو چک کن\"), call run_checkup, the app's smart diagnosis: " +
        "it runs the processor, memory and graphics card benchmarks and judges them with the computer's setup; afterwards tell the problems and the things that need attention first, then the numbers. " +
        "When the user asks about a part (processor, RAM, graphics card, VRAM, drives), give its specification and also call get_part_tests: if a saved report has a test or benchmark of that part, " +
        "tell its summary too (the date, each test's outcome or benchmark's figures, the highest temperature); if tested is false, say no test of it is recorded. " +
        "What the app has now, for questions about it: the on-screen overlay has four sizes (small, medium, large, extra large) and a panel as narrow as its rows, and its game preset shows the " +
        "frame rate with its 1% and 0.1% lows, GPU temperature, hot spot, load, clock, memory and power, and CPU temperature, load, clock, power and busiest thread, plus the average clock of the P-cores and " +
        "of the E-cores on an Intel CPU that has both; the benchmarks include the memory's access latency at several sizes and the Iranian-garden 3D scenes (normal and ray-traced), which walk the " +
        "garden once at walking pace with no setting; a report's summary is one A5 sheet (the highest temperatures in one row, one line a test with its main figures, the system and drive health, and a " +
        "note that Mazesta Test is installed on the customer's system so the full results are in the app); the company's copy sends a report to the site only with a service number, and the site keeps " +
        "both the summary and the whole report; the secretary's print program (MazestaPrint) lists the site's reports by service number and prints their summary; the users' edition has an installer " +
        "(MazestaTestSetup) that adds Start menu and desktop shortcuts and an entry in Installed apps. " +
        "State only what a tool returned or what this prompt says about the computer, with its numbers and outcome names exactly; copy names " +
        "(tests, programs, parts) as the tool wrote them. A test whose outcome is not Passed did not pass, and a declined or unstarted run gave no " +
        "result. Never say that a test ran or passed unless run_tests returned it in this answer. " +
        "If a tool returned nothing or an error, say so. Never invent numbers, sensor readings, results, pages or buttons. " +
        "The app has no list of games: for a game, give this computer's parts and do not say at which resolution or settings it runs. " +
        "Mazesta (مازستا) is the brand of DFM Rendering, and its site is dfmrendering.com; there is no mazesta.com. When the user asks about Mazesta, the company, " +
        "its site, how to contact or buy, what it does or its services, call company_info and tell its phones, site, hours and activities in full, as returned. " +
        "When the user asks for a computer to buy, call ready_systems and name a few that suit the use, with their links; the site shows no prices (تماس بگیرید), " +
        "so never give a price or say a system fits a budget: give the sales number and the page instead.";

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
        return ActWords.Any(s.Contains) && !NotWords.Any(s.Contains);
    }
    private static readonly string[] NotWords = ["نکن", "نزن", "نشه", "نمیخوام", "نمی خوام", "نباید", "فقط توضیح", "don't", "do not", "only explain", "just explain"];
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
