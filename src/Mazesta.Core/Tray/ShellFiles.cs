namespace Mazesta.Core.Tray;

/// <summary>
/// How Explorer's right-click entries for the file tools (what holds it open, make pictures smaller, rename these) reach the app. Explorer starts
/// <c>MazestaTray.exe --shell &lt;tool&gt; "&lt;path&gt;"</c> once per selected file, without administrator rights (no UAC prompt): each start drops
/// its path in <see cref="Inbox"/>, the user's own folder, and ends. The running tray (elevated) gathers a selection's files there, writes them as one
/// request to <see cref="FileName"/> beside its log, and wakes the app with <see cref="Event"/> (or starts it), which opens that box with them.
/// </summary>
public static class ShellFiles
{
    public const string Argument = "--shell";
    public const string Event = @"Local\Mazesta.App.ShellFiles";
    public const string FileName = "shell-request.json";
    public const string Lock = "lock", Resize = "resize", Rename = "rename";
    public static readonly string[] Tools = [Lock, Resize, Rename];

    public sealed record Request(string Tool, List<string> Paths);

    /// <summary>Where the handoff drops a path: the signed-in user's own local folder, writable without administrator rights.</summary>
    public static string Inbox => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mazesta", "shell");
}
