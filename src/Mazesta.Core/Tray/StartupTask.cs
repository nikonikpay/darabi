namespace Mazesta.Core.Tray;

/// <summary>
/// Builds the schtasks.exe command line to run the tray monitor at logon with the signed-in user's
/// highest available privileges, and to query or remove that task. This type only builds argument
/// strings - actually running schtasks is the caller's job, and it must follow an explicit user
/// action (a button click), never something the app does on its own the first time it starts.
/// </summary>
public static class StartupTask
{
    public const string TaskName = "MazestaTray";

    /// <summary>schtasks needs the /TR value quoted, and if the executable's own path has spaces
    /// (the default install path does), the inner path needs its own escaped quotes too.</summary>
    public static string RegisterArguments(string exePath) => $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";

    public static string QueryArguments() => $"/Query /TN \"{TaskName}\" /FO LIST";

    public static string DeleteArguments() => $"/Delete /TN \"{TaskName}\" /F";

    /// <summary>True when `schtasks /Query` output names this task. A task that was never
    /// registered (or was removed) prints an error line instead, on stdout or stderr depending on
    /// the Windows version, never the task name.</summary>
    public static bool IsRegistered(string queryOutput) => queryOutput.Contains(TaskName, StringComparison.OrdinalIgnoreCase);
}
