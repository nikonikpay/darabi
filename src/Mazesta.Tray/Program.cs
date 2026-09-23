using Mazesta.Persistence;
using Mazesta.Tray;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One tray per user session: a second launch just exits.
        using var single = new Mutex(true, @"Local\MazestaTray", out bool first);
        if (!first) return;
        // Same appconfig.json the Desktop app writes: the tray never edits it, only reads the
        // interval settings, so a corrupt or missing file just falls back to their defaults.
        var paths = AppPaths.Detect();
        var config = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator(AppConfig.Migrations), AppConfig.CurrentSchemaVersion, NullLogger.Instance).Load().Value;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(TimeSpan.FromSeconds(config.TrayFirstCheckSeconds), TimeSpan.FromMinutes(config.TrayIdleIntervalMinutes), TimeSpan.FromSeconds(config.TrayWatchIntervalSeconds)));
    }
}
