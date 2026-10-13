using System.Windows.Forms; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
namespace Mazesta.App;

/// <summary>
/// What Explorer's right-click entries run (<c>Mazesta.exe --fw-block "file.exe"</c> / <c>--av-exclude "folder"</c>, see <see cref="ShieldShellMenu"/>): no window of the app, no services - the
/// one change, then a box that says what was done or why it was not. A program inside Windows' own folder, or a folder as wide as a drive, is asked about first.
/// </summary>
internal static class ShieldCli
{
    /// <summary>What the tray's menu starts: <c>--av-stop</c> (the antivirus off until the computer restarts), <c>--fw-off</c>, <c>--fw-on</c>.</summary>
    public const string AvStopArgument = "--av-stop", FirewallOffArgument = "--fw-off", FirewallOnArgument = "--fw-on";

    public static bool Is(string[] args) => args.Length == 2 && args[0] is ShieldShellMenu.FirewallArgument or ShieldShellMenu.AntivirusArgument || args.Length == 1 && args[0] is AvStopArgument or FirewallOffArgument or FirewallOnArgument;

    /// <summary>Windows Security's page of the Defender settings, where Tamper Protection is switched off by hand: no program may do it.</summary>
    public static void OpenSecurity()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("windowsdefender://threatsettings") { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>The program a shortcut points to (empty when it points to nothing the shell can name, which the block then refuses as "not a program").</summary>
    private static string ShortcutTarget(string lnk)
    {
        try { dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!; return (string)shell.CreateShortcut(lnk).TargetPath; }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or NullReferenceException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return ""; }
    }

    private static int Protection(string argument)
    {
        var tools = new ShieldTools(new ProcessCommandRunner()); string title = Loc.Get("Shield_Title");
        bool firewall = argument != AvStopArgument, on = argument == FirewallOnArgument;
        if (!on && MessageBox.Show(Loc.Get(firewall ? "Shield_Ask_FwOff" : "Shield_Ask_AvOff"), title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 1;
        var r = firewall ? tools.FirewallSwitchAsync(on, default).GetAwaiter().GetResult() : tools.AvStopAsync(permanent: false, default).GetAwaiter().GetResult();
        if (r.Ok) { MessageBox.Show(Loc.Get(firewall ? (on ? "Shield_Done_FwOn" : "Shield_Done_FwOff") : "Shield_Done_AvOff"), title, MessageBoxButtons.OK, MessageBoxIcon.Information); return 0; }
        bool tamper = !firewall && r.Problem == ShieldProblem.NotAccepted; string why = tamper ? Loc.Get("Shield_Err_AvTamper") : Problem(r);
        if (tamper && MessageBox.Show(why + Environment.NewLine + Environment.NewLine + Loc.Get("Shield_Open_Security"), title, MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes) OpenSecurity();
        else if (!tamper) MessageBox.Show(why, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }

    public static int Run(string[] args, string language)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Loc.SetLanguage(language);
        if (args.Length == 1) return Protection(args[0]);
        bool firewall = args[0] == ShieldShellMenu.FirewallArgument; string path = args[1].EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(args[1]) : args[1]; var tools = new ShieldTools(new ProcessCommandRunner());
        var result = firewall ? tools.BlockAsync(path, allowSystem: false, default).GetAwaiter().GetResult() : tools.ExcludeAsync(path, allowBroad: false, default).GetAwaiter().GetResult();
        if (result.Problem is ShieldProblem.SystemFolder or ShieldProblem.TooBroad)
        {
            if (MessageBox.Show(Loc.Format(result.Problem == ShieldProblem.SystemFolder ? "Shield_Confirm_System" : "Shield_Confirm_Broad", path), Loc.Get("Shield_Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 1;
            result = firewall ? tools.BlockAsync(path, allowSystem: true, default).GetAwaiter().GetResult() : tools.ExcludeAsync(path, allowBroad: true, default).GetAwaiter().GetResult();
        }
        string name = Path.GetFileName(path.TrimEnd((char)92)); if (name.Length == 0) name = path;
        MessageBox.Show(result.Ok ? Loc.Format(firewall ? "Shield_Done_Block" : "Shield_Done_Exclude", name) : Problem(result), Loc.Get("Shield_Title"), MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        return result.Ok ? 0 : 1;
    }

    /// <summary>The words for a refused change (also the page's).</summary>
    public static string Problem(ShieldResult r) => r.Problem switch
    {
        ShieldProblem.NotFullPath => Loc.Get("Shield_Err_NotFullPath"), ShieldProblem.NotExe => Loc.Get("Shield_Err_NotExe"), ShieldProblem.Missing => Loc.Get("Shield_Err_Missing"),
        ShieldProblem.NotAccepted => Loc.Format("Shield_Err_NotAccepted", r.Detail ?? ""), _ => Loc.Format("Shield_Err_Failed", r.Detail ?? ""),
    };
}
