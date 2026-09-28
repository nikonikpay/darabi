using Mazesta.Persistence;
using Mazesta.Tray;
namespace Mazesta.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
