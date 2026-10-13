using System.Diagnostics; using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The Firewall and antivirus page: programs blocked in the Windows firewall (both directions, by a pair of rules of this app's own), files and folders left out of Windows Defender's
    /// scans, and the two right-click entries of Explorer that do the same without opening the app. The page can only name a path (typed, picked in a dialog or taken from the running
    /// programs); what is allowed is decided in <see cref="ShieldPaths"/>, and every change is read back from Windows before the page is told it worked.
    /// </summary>
    private void RegisterShield()
    {
        var tools = new ShieldTools(_sp.GetRequiredService<ICommandRunner>()); string exe = Environment.ProcessPath ?? "";
        string Label(bool firewall) => Loc.Get(firewall ? "Shield_Menu_FwLabel" : "Shield_Menu_AvLabel");

        // Both entries are there from the first start (the owner never found them behind a switch that began off); a switch turned off stays off, which the marker file remembers.
        string decided = Path.Combine(_paths.ConfigDir, "shield-menu-set.txt");
        // An entry made by another copy of the app, or in another language, is made again for this one: it always starts the program that is running.
        object Menu()
        {
            if (!File.Exists(decided))
            {
                try { foreach (bool fw in new[] { true, false }) ShieldShellMenu.Set(fw, true, exe, Label(fw)); Directory.CreateDirectory(_paths.ConfigDir); File.WriteAllText(decided, "1"); }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { _log.LogWarning(e, "The right-click entries were not added"); }
            }
            foreach (bool fw in new[] { true, false })
                if (ShieldShellMenu.Registered(fw) is not null && !ShieldShellMenu.IsCurrent(fw, exe, Label(fw))) ShieldShellMenu.Set(fw, true, exe, Label(fw));
            return new { firewall = ShieldShellMenu.Registered(true) is not null, antivirus = ShieldShellMenu.Registered(false) is not null };
        }
        object Answer(ShieldResult r) => r.Ok ? (object)new { ok = true } : new
        {
            ok = false, ask = r.Problem is ShieldProblem.SystemFolder or ShieldProblem.TooBroad, error = ShieldCli.Problem(r),
            confirm = r.Problem == ShieldProblem.SystemFolder ? Loc.Get("Shield_Confirm_System_Page") : r.Problem == ShieldProblem.TooBroad ? Loc.Get("Shield_Confirm_Broad_Page") : null,
        };

        Method("shield.menu", _ => Menu());
        Menu();   // the entries exist from the first start, whether or not the page is ever opened
        Method("shield.menuSet", p =>
        {
            bool fw = Str(p, "kind") == "firewall", on = Bool(p, "on");
            try { ShieldShellMenu.Set(fw, on, exe, Label(fw)); Directory.CreateDirectory(_paths.ConfigDir); File.WriteAllText(decided, "1"); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { _log.LogWarning(e, "The right-click entry was not changed"); return new { error = e.Message }; }
            _log.LogInformation("Right-click entry {Kind}: {State}", fw ? "firewall" : "antivirus", on ? "added" : "removed"); return Menu();
        });

        MethodAsync("shield.firewall", async _ =>
        {
            var s = await tools.FirewallAsync(CancellationToken.None).ConfigureAwait(true);
            return s is null ? new { ok = false } : (object)new { ok = true, profiles = s.Profiles.Select(x => new { name = x.Profile, on = x.On }), blocked = s.Blocked.Select(b => new { path = b.Path, name = b.Name, inbound = b.Inbound, outbound = b.Outbound, ours = b.Ours }) };
        });
        MethodAsync("shield.av", async _ =>
        {
            var s = await tools.AvAsync(CancellationToken.None).ConfigureAwait(true);
            return s is null ? new { ok = false } : (object)new
            {
                ok = true, defender = s.Defender, realTime = s.RealTime, stopped = s.Stopped, restore = s.RestoreAtStart, policy = s.Policy, tamper = s.Tamper, other = s.Other, products = s.Products.Select(x => new { name = x.Name, on = x.On }),
                exclusions = s.Exclusions.Select(x => new { path = x, folder = Directory.Exists(x), exists = Directory.Exists(x) || File.Exists(x) }),
            };
        });

        // The programs that run now, one row for each file: the usual way to find what to block. Windows' own folder is left out (a system program is blocked by choosing its file).
        MethodAsync("shield.processes", _ => Task.Run<object?>(() =>
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        string? file = p.MainModule?.FileName;
                        if (file is null || file.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        found[file] = found.GetValueOrDefault(file) + 1;
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                }
            }
            return found.Select(kv => new { path = kv.Key, name = Path.GetFileName(kv.Key), count = kv.Value }).OrderBy(x => x.name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }));

        // A dialog for a file or a folder; null when the user closes it. The bridge runs on the window's thread, which is where a dialog has to be shown.
        Method("shield.pick", p =>
        {
            string kind = Str(p, "kind"), title = Loc.Get(kind == "folder" ? "Shield_Pick_Folder" : "Shield_Pick_File");
            if (kind == "folder")
            {
                using var d = new System.Windows.Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, ShowNewFolderButton = false };
                return new { path = d.ShowDialog(Desktop.Composition.UiDispatcher.Owner) == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null };
            }
            using var f = new System.Windows.Forms.OpenFileDialog { Title = title, CheckFileExists = true, Filter = kind == "exe" ? Loc.Get("Shield_Pick_ExeFilter") + "|*.exe" : Loc.Get("Shield_Pick_AnyFilter") + "|*.*", DereferenceLinks = true };
            return new { path = f.ShowDialog(Desktop.Composition.UiDispatcher.Owner) == System.Windows.Forms.DialogResult.OK ? f.FileName : null };
        });

        MethodAsync("shield.block", async p =>
        {
            var r = await tools.BlockAsync(Str(p, "path"), Bool(p, "force"), CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Firewall block {Path}: {Result} {Detail}", Str(p, "path"), r.Problem, r.Detail); return Answer(r);
        });
        MethodAsync("shield.unblock", async p =>
        {
            var r = await tools.UnblockAsync(Str(p, "path"), CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Firewall unblock {Path}: {Result} {Detail}", Str(p, "path"), r.Problem, r.Detail); return Answer(r);
        });
        // The firewall and the antivirus switched off and on. The page asks the user first; the answer says what Windows really did (Tamper Protection refuses without an error).
        MethodAsync("shield.fwSwitch", async p =>
        {
            bool on = Bool(p, "on"); var r = await tools.FirewallSwitchAsync(on, CancellationToken.None).ConfigureAwait(true);
            _log.LogWarning("Firewall switched {State}: {Result} {Detail}", on ? "on" : "off", r.Problem, r.Detail); return Answer(r);
        });
        MethodAsync("shield.avStop", async p =>
        {
            bool permanent = Bool(p, "permanent"); var r = await tools.AvStopAsync(permanent, CancellationToken.None).ConfigureAwait(true);
            _log.LogWarning("Antivirus stopped ({Kind}): {Result} {Detail}", permanent ? "for good" : "until restart", r.Problem, r.Detail);
            return r.Problem == ShieldProblem.NotAccepted ? new { ok = false, ask = false, error = Loc.Get("Shield_Err_AvTamper"), security = true } : Answer(r);
        });
        MethodAsync("shield.avStart", async _ =>
        {
            var r = await tools.AvStartAsync(CancellationToken.None).ConfigureAwait(true); _log.LogWarning("Antivirus started: {Result} {Detail}", r.Problem, r.Detail); return Answer(r);
        });
        Method("shield.openSecurity", _ => { ShieldCli.OpenSecurity(); return new { ok = true }; });

        MethodAsync("shield.exclude", async p =>
        {
            var r = await tools.ExcludeAsync(Str(p, "path"), Bool(p, "force"), CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Antivirus exclusion {Path}: {Result} {Detail}", Str(p, "path"), r.Problem, r.Detail); return Answer(r);
        });
        MethodAsync("shield.unexclude", async p =>
        {
            var r = await tools.UnexcludeAsync(Str(p, "path"), CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Antivirus exclusion removed {Path}: {Result} {Detail}", Str(p, "path"), r.Problem, r.Detail); return Answer(r);
        });
    }
}
