namespace Mazesta.Core.Rgb;

/// <summary>
/// What a fresh search for the lights should have found, worked out from what the PC has, so the page can say what is missing. "Missing" only means the server listed none:
/// a board or memory without lights is also missing, so the page words it as "not found", never as broken.
/// </summary>
public static class RgbScan
{
    private static readonly string[] Discrete = ["geforce", "rtx", "gtx", "radeon rx", "radeon pro", "arc a", "arc b", "quadro"];

    /// <param name="foundKinds">The kinds of the devices the server listed (<c>Motherboard</c>, <c>Memory</c>, <c>Graphics</c> ...).</param>
    /// <param name="cards">The graphics adapters Windows lists (a card of its own is expected among the lights, a processor's built-in graphics is not).</param>
    public static IReadOnlyList<string> Missing(IEnumerable<string> foundKinds, IEnumerable<string> cards)
    {
        var found = new HashSet<string>(foundKinds, StringComparer.OrdinalIgnoreCase); var missing = new List<string>();
        if (!found.Contains("Motherboard")) missing.Add("Motherboard");
        if (!found.Contains("Memory")) missing.Add("Memory");
        if (!found.Contains("Graphics") && cards.Any(c => Discrete.Any(d => c.Contains(d, StringComparison.OrdinalIgnoreCase)))) missing.Add("Graphics");
        return missing;
    }
}
