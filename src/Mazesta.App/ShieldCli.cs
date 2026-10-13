using System.Windows.Forms; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
namespace Mazesta.App;

/// <summary>
/// What Explorer's right-click entries run (<c>Mazesta.exe --fw-block "file.exe"</c> / <c>--av-exclude "folder"</c>, see <see cref="ShieldShellMenu"/>): no window of the app, no services - the
/// one change, then a box that says what was done or why it was not. A program inside Windows' own folder, or a folder as wide as a drive, is asked about first.
/// </summary>
internal static class ShieldCli
{
    public static bool Is(string[] args) => args.Length == 2 && args[0] is ShieldShellMenu.FirewallArgument or ShieldShellMenu.AntivirusArgument;

    public static int Run(string[] args, string language)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Loc.SetLanguage(language);
        bool firewall = args[0] == ShieldShellMenu.FirewallArgument; string path = args[1]; var tools = new ShieldTools(new ProcessCommandRunner());
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
