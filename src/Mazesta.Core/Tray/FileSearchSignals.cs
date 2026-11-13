namespace Mazesta.Core.Tray;

/// <summary>
/// How the app reaches the tray's quick file search, with two named events (like <see cref="OverlaySignals"/>): <see cref="Show"/> opens its
/// window, <see cref="Settings"/> says the switch or the shortcut changed in appconfig.json, so the tray reads them again without a restart.
/// </summary>
public static class FileSearchSignals
{
    public const string Show = @"Local\Mazesta.FileSearch.Show";
    public const string Settings = @"Local\Mazesta.FileSearch.Settings";
    public const string DefaultKey = "Ctrl+Alt+F";
    /// <summary>Opens the tray's per-program volume panel.</summary>
    public const string Mixer = @"Local\Mazesta.Mixer.Show";
    /// <summary>Starts the tray's screen colour picker.</summary>
    public const string ColorPick = @"Local\Mazesta.Color.Pick";
    /// <summary>Opens the tray's disk-space window.</summary>
    public const string Space = @"Local\Mazesta.Space.Show";
}
