namespace Mazesta.Core.Tray;

/// <summary>
/// How the tray and the app talk about the overlay, with two named events and nothing else (no pipe, no port): the tray sets
/// <see cref="Toggle"/> to show or hide it, and reads <see cref="Shown"/>, which the app keeps set while the overlay is on screen. When the app is
/// not running the tray starts it with <see cref="Argument"/>: it then opens no window, only the overlay, and ends when the overlay is hidden.
/// Both processes run elevated in the same session, so session-local names reach each other.
/// </summary>
public static class OverlaySignals
{
    public const string Toggle = @"Local\Mazesta.Overlay.Toggle";
    public const string Shown = @"Local\Mazesta.Overlay.Shown";
    public const string Argument = "--overlay";
    public const string TrayProcess = "MazestaTray";
}
