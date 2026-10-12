namespace Mazesta.Core.Inventory;

/// <summary>The words a board maker leaves in the BIOS where the maker, model or serial should be: not facts about the machine.</summary>
public static class Placeholder
{
    /// <summary>Whether a maker/model/serial text is empty or such a placeholder ("To be filled by O.E.M.", "System Product Name").</summary>
    public static bool Is(string? s) => string.IsNullOrWhiteSpace(s) || s.Contains("O.E.M.", StringComparison.OrdinalIgnoreCase) || s.Contains("To be filled", StringComparison.OrdinalIgnoreCase)
        || s.Contains("System Product Name", StringComparison.OrdinalIgnoreCase) || s.Contains("System Serial Number", StringComparison.OrdinalIgnoreCase) || s.Contains("Default string", StringComparison.OrdinalIgnoreCase);
}
