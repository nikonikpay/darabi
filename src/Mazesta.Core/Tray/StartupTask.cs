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

    /// <summary>Registers the task from <see cref="TaskXml"/> written to <paramref name="xmlPath"/>.</summary>
    public static string RegisterArguments(string xmlPath) => $"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F";

    /// <summary>The task as Task Scheduler XML (save it as UTF-16): at <paramref name="user"/>'s logon, highest privileges. Not the plain
    /// <c>/SC ONLOGON</c> line, whose defaults are "start only on AC power", "stop when on battery" and a 72-hour limit: on a laptop the tray
    /// did not start on battery and was killed when the charger came out, and everywhere it was killed after three days.</summary>
    public static string TaskXml(string exePath, string user) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Escape(user)}</UserId></LogonTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>{Escape(exePath)}</Command></Exec></Actions>
        </Task>
        """;

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s);

    public static string QueryArguments() => $"/Query /TN \"{TaskName}\" /FO LIST";

    public static string DeleteArguments() => $"/Delete /TN \"{TaskName}\" /F";

    /// <summary>True when `schtasks /Query` output names this task. A task that was never
    /// registered (or was removed) prints an error line instead, on stdout or stderr depending on
    /// the Windows version, never the task name.</summary>
    public static bool IsRegistered(string queryOutput) => queryOutput.Contains(TaskName, StringComparison.OrdinalIgnoreCase);
}
