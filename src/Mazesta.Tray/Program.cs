using Mazesta.Persistence;
using Mazesta.Tray;
namespace Mazesta.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Explorer's right-click entry for one file: started without administrator rights (the manifest asks for none), it hands the path over and ends
        if (args.Length == 3 && args[0] == Mazesta.Core.Tray.ShellFiles.Argument) { Shell.ShellInbox.Send(args[1], args[2]); return; }
        // Every other start needs the rights for the sensors and the drives' tables: the logon task and the app start it elevated; a plain double-click asks once.
        if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { }   // declined
            return;
        }
        // One tray per user session: a second launch just exits.
        using var single = new Mutex(true, @"Local\MazestaTray", out bool first);
        if (!first) return;
        // Same appconfig.json the Desktop app writes; read-only (see TrayIntervals), so a corrupt or missing file just means the defaults.
        var paths = AppPaths.Detect();
        var intervals = TrayIntervals.Read(paths.ConfigFile);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(intervals, TrayCheckLog.FileIn(paths), paths));
    }
}
