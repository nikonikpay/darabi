using System.Text.RegularExpressions;
namespace Mazesta.Core.Hardware;

/// <summary>
/// Whether an adapter is the processor's own graphics. Its "dedicated" memory says nothing: an AMD APU's is the share of RAM the firmware sets
/// aside (2-8 GB, up to 96 GB on Ryzen AI Max), more than the RTX 4050 next to it has, so ranking by memory put the tests on the wrong GPU of a
/// hybrid laptop. DirectX's unified-memory flag marks them; should a driver not set it, they are known by name - an AMD "Radeon ... Graphics" that
/// is not an RX or Pro card, and every Intel adapter but the Arc A/B cards.
/// </summary>
public static partial class GpuKind
{
    public static bool Integrated(string name, bool unifiedMemory)
    {
        if (unifiedMemory) return true;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) && !AmdCard().IsMatch(name);
        return name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && !IntelCard().IsMatch(name);
    }

    [GeneratedRegex(@"\b(RX|Pro|FirePro|Instinct)\b", RegexOptions.IgnoreCase)] private static partial Regex AmdCard();
    [GeneratedRegex(@"\bArc\b.*\b[AB]\d{3}", RegexOptions.IgnoreCase)] private static partial Regex IntelCard();
}
