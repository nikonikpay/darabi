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
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
