using System.Diagnostics; using Mazesta.Persistence;
namespace Mazesta.Tray;

/// <summary>
/// The fan profile in the tray's menu: automatic (the board's own control), the ready-made profiles and the ones the user saved on the fans page, the one in force ticked.
/// Picking one writes a request the app reads (<see cref="FanProfiles.Request"/>); when the app is not running it is started with no window to hold the profile
/// (the tray cannot hold a fan itself: the sensor driver stays closed between checks), and it ends again by itself when the profile goes back to automatic.
/// At sign-in the app is started the same way when a profile was in force at shutdown, so the fans keep their profile across restarts.
/// </summary>
internal sealed class FanMenu
{
    private readonly AppPaths _paths; private readonly Action<string> _startApp; private readonly Action<string, ToolTipIcon> _notify;
    public ToolStripMenuItem Menu { get; }

    public FanMenu(AppPaths paths, Action<string> startApp, Action<string, ToolTipIcon> notify)
    {
        _paths = paths; _startApp = startApp; _notify = notify;
        Menu = new ToolStripMenuItem(TrayText.Fans);
        Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.Reading) { Enabled = false });   // so the arrow shows before the first opening
        Menu.DropDownOpening += (_, _) => Build();
    }

    private void Build()
    {
        Menu.DropDownItems.Clear();
        var doc = FanProfiles.Read(FanProfiles.FileIn(_paths));
        foreach (var name in FanProfiles.Builtin) Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.FanProfile(name), null, (_, _) => Pick(name, TrayText.FanProfile(name))) { Checked = doc.Active == name });
        if (doc.Custom.Count > 0) Menu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var name in doc.Custom.Keys.OrderBy(k => k)) Menu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => Pick(name, name)) { Checked = doc.Active == name });
        if (doc.Active == "custom") Menu.DropDownItems.Add(new ToolStripMenuItem(TrayText.FanCustom) { Enabled = false, Checked = true });
    }

    private void Pick(string name, string shown)
    {
        try { FanProfiles.Request(_paths, name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _notify(e.Message, ToolTipIcon.Warning); return; }
        if (!AppRuns()) _startApp(AppArgs.Background);
        _notify(TrayText.FanRequested(shown), ToolTipIcon.Info);
    }

    /// <summary>At sign-in: a profile that was in force is put back by starting the app without its window.</summary>
    public void ApplyAtStart()
    {
        if (FanProfiles.Read(FanProfiles.FileIn(_paths)).Active is "auto" or "") return;
        if (!AppRuns()) _startApp(AppArgs.Background);
    }

    private static bool AppRuns() { var p = Process.GetProcessesByName("Mazesta").Concat(Process.GetProcessesByName("Mazesta-Admin")).ToList(); foreach (var x in p) x.Dispose(); return p.Count > 0; }
}

/// <summary>The app's start-up argument for "no window" (kept in step with <c>Program.BackgroundArgument</c> of the app).</summary>
internal static class AppArgs { public const string Background = "--background"; }
