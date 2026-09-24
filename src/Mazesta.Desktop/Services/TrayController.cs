using System.Diagnostics; using System.IO;
using Mazesta.Core.Tray; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.Services;

/// <summary>What Windows says about the tray right now. Both facts are asked of the system, never remembered: a stored flag
/// would drift when the task is removed or the process is closed from outside the app.</summary>
public sealed record TrayState(bool Registered, bool Running, string? Error = null);

/// <summary>Controls the tray monitor that ships next to the app: whether it starts at logon (a Task Scheduler task) and whether it runs now.
/// Every method returns null on success or a message for the technician; nothing is registered or started except by an explicit call.</summary>
public interface ITrayController
{
    TrayState Query();
    string? Enable();
    string? Disable();
    string? Restart();
}

public sealed class TrayController : ITrayController
{
    private const string ProcessName = "MazestaTray";
    private static string ExePath => Path.Combine(AppContext.BaseDirectory, ProcessName + ".exe");

    public TrayState Query()
    {
        try { return new(IsRegistered(), IsRunning()); }
        catch (Exception e) { return new(false, false, e.Message); }
    }

    public string? Enable()
    {
        if (!File.Exists(ExePath)) return Loc.Get("Settings_Tray_ExeNotFound");
        try
        {
            var (code, output) = RunSchtasks(StartupTask.RegisterArguments(ExePath));
            if (code != 0) return output.Trim();
            return IsRunning() ? null : Start();
        }
        catch (Exception e) { return e.Message; }
    }

    public string? Disable()
    {
        try
        {
            if (IsRegistered())
            {
                var (code, output) = RunSchtasks(StartupTask.DeleteArguments());
                if (code != 0) return output.Trim();
            }
            Stop();
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public string? Restart()
    {
        try { if (!IsRunning()) return null; Stop(); return Start(); }
        catch (Exception e) { return e.Message; }
    }

    private static bool IsRegistered() => StartupTask.IsRegistered(RunSchtasks(StartupTask.QueryArguments()).Output);

    private static bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    private static string? Start()
    {
        if (!File.Exists(ExePath)) return Loc.Get("Settings_Tray_ExeNotFound");
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
        return null;
    }

    private static void Stop()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName)) using (p) { p.Kill(); p.WaitForExit(3000); }
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("schtasks.exe did not start");
        var error = p.StandardError.ReadToEndAsync();   // both pipes drained concurrently, or a full stderr buffer would block the process
        string output = p.StandardOutput.ReadToEnd() + error.GetAwaiter().GetResult();
        p.WaitForExit();
        return (p.ExitCode, output);
    }
}
