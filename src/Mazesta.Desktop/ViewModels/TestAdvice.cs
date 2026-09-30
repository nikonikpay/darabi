using Mazesta.Desktop.Localization; using Mazesta.Diagnostics;
namespace Mazesta.Desktop.ViewModels;

/// <summary>
/// What a technician should make of a failed or inconclusive test: what was seen, what could cause it, and the next test that tells the causes
/// apart. A RAM mismatch does not by itself name a bad module - the memory controller, an XMP setting, the board or the CPU can all cause one -
/// so the advice lists causes and the way to separate them, never a verdict on a single part. A pass needs no advice.
/// </summary>
public static class TestAdvice
{
    public static string? KeyFor(string testId, TestOutcome outcome) => outcome switch
    {
        TestOutcome.Failed => testId switch
        {
            "gpu.vram" => "Advice_Vram",
            "storage.smart" => "Advice_Smart",
            "network.lan" => "Advice_Lan",
            _ when testId.StartsWith("cpu.", StringComparison.Ordinal) => "Advice_Cpu",
            _ when testId.StartsWith("memory.", StringComparison.Ordinal) => "Advice_Memory",
            _ when testId.StartsWith("gpu.", StringComparison.Ordinal) => "Advice_Gpu",
            _ when testId.StartsWith("storage.", StringComparison.Ordinal) => "Advice_Storage",
            _ when testId.StartsWith("network.", StringComparison.Ordinal) => "Advice_Network",
            _ when testId.StartsWith("power.", StringComparison.Ordinal) => "Advice_Power",
            _ when testId.StartsWith("windows.", StringComparison.Ordinal) => "Advice_Windows",
            _ => null,
        },
        TestOutcome.Inconclusive => testId switch
        {
            "cpu.singlecore" => "Advice_Inconclusive_Cores",
            "network.lan" => "Advice_Inconclusive",
            _ when testId.StartsWith("network.", StringComparison.Ordinal) => "Advice_Inconclusive_Network",
            _ => "Advice_Inconclusive",
        },
        TestOutcome.Error => "Advice_Error",
        _ => null,
    };

    public static string? For(string testId, TestOutcome outcome) => KeyFor(testId, outcome) is { } key ? Loc.Get(key) : null;
}
