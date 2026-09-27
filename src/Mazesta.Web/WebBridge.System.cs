using System.Diagnostics; using System.IO;
using Mazesta.Core.Windows; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows; using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Web;

public sealed partial class WebBridge
{
    /// <summary>
    /// Windows settings the technician changes by hand on a customer's machine: hibernation and Fast Startup, the virtual memory, and the hosts
    /// file. Nothing here runs on its own: each change is one button, says what it did (or Windows' own error), and the page reads the state back
    /// from Windows afterwards rather than assuming it.
    /// </summary>
    private void RegisterSystem()
    {
        var runner = _sp.GetRequiredService<ICommandRunner>();
        long ramMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        string? vmPending = null;   // the setting applied this session, waiting for a restart

        object VmState()
        {
            var wmi = _sp.GetRequiredService<IWmiQuery>();
            PageFileInfo? usage = null; IReadOnlyList<PageFileSettingInfo> settings = [];
            try { usage = WmiPageFile.Read(wmi); settings = WmiPageFileWriter.Settings(); }
            catch (Exception e) when (e is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { _log.LogWarning(e, "Page file read failed"); }
            return new
            {
                ramMb, managed = usage?.SystemManaged, pending = vmPending,
                inUse = usage?.Files.Select(f => new { path = f.Path, sizeMb = f.AllocatedMb, usedMb = f.CurrentMb, peakMb = f.PeakMb }) ?? [],
                settings = settings.Select(s => new { path = s.Path, initialMb = s.InitialMb, maximumMb = s.MaximumMb }),
                drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => new { name = d.Name[..2], label = d.VolumeLabel, freeMb = d.AvailableFreeSpace / (1024 * 1024), totalMb = d.TotalSize / (1024 * 1024) }),
            };
        }
        object PowerState() { var h = HibernateStatus.Read(); return new { hibernate = h.Hibernate, fastStartup = h.FastStartup }; }

        Method("sys.state", _ => new { power = PowerState(), vm = VmState(), hosts = new { path = HostsFile.PathOf(), backup = File.Exists(HostsFile.BackupOf(HostsFile.PathOf())) } });

        MethodAsync("sys.hibernate", async p =>
        {
            bool on = Bool(p, "on");
            string? error = await HibernateStatus.SetAsync(runner, on, CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Hibernation set {State}: {Result}", on ? "on" : "off", error ?? "ok");
            return new { power = PowerState(), error };
        });

        MethodAsync("sys.pagefile", async p =>
        {
            var mode = Str(p, "mode") switch { "custom" => PageFileMode.Custom, "none" => PageFileMode.None, _ => PageFileMode.SystemManaged };
            long.TryParse(Str(p, "initial"), out long initial); long.TryParse(Str(p, "maximum"), out long maximum);
            string drive = Str(p, "drive");
            var plan = new PageFilePlan(mode, drive.Length > 0 ? drive : null, initial, maximum);
            long? free = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith(drive, StringComparison.OrdinalIgnoreCase) && drive.Length > 0)?.AvailableFreeSpace / (1024 * 1024);
            if (plan.Problem(ramMb, free) is { } key) return new { error = Loc.Format(key, Math.Max(ramMb * 3, 4096)), vm = VmState() };
            try { await Task.Run(() => WmiPageFileWriter.Apply(plan)).ConfigureAwait(true); }
            catch (Exception e) when (e is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                _log.LogWarning(e, "Page file change failed"); return new { error = Loc.Format("Tools_Vm_Failed", e.Message), vm = VmState() };
            }
            vmPending = mode switch { PageFileMode.Custom when initial > 0 => $"{plan.Drive![..1]}: {initial}–{maximum} MB", PageFileMode.Custom => $"{plan.Drive![..1]}: {Loc.Get("Tools_Vm_Auto")}", PageFileMode.None => Loc.Get("Tools_Vm_None"), _ => Loc.Get("Tools_Vm_Managed") };
            _log.LogInformation("Page file set to {Plan}", plan);
            return new { vm = VmState() };
        });

        Method("hosts.read", _ =>
        {
            string path = HostsFile.PathOf();
            string text = HostsFile.Read(path);
            return new { path, text, entries = HostsFile.Entries(text), problems = HostsFile.Check(text).Select(l => new { line = l.Number, text = l.Text, problem = l.Problem }), backup = File.Exists(HostsFile.BackupOf(path)) };
        });
        // Saving refuses lines Windows would ignore unless the page says to save anyway; the previous file is kept as the backup either way.
        MethodAsync("hosts.save", async p =>
        {
            string text = Str(p, "text"), path = HostsFile.PathOf();
            var problems = HostsFile.Check(text);
            if (problems.Count > 0 && !Bool(p, "force")) return new { saved = false, problems = problems.Select(l => new { line = l.Number, text = l.Text, problem = l.Problem }) };
            try { HostsFile.Save(path, text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(e, "Hosts file not saved");
                return new { saved = false, error = Loc.Format("Tools_Hosts_SaveFailed", e.Message), problems = Array.Empty<object>() };
            }
            await HostsFile.FlushDnsAsync(runner, CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Hosts file saved ({Entries} entries)", HostsFile.Entries(text));
            return new { saved = true, problems = Array.Empty<object>() };
        });
        Method("hosts.backup", _ =>
        {
            string path = HostsFile.PathOf(), backup = HostsFile.BackupOf(path);
            return File.Exists(backup) ? new { text = File.ReadAllText(backup) } : null;
        });
        Method("hosts.notepad", _ => { Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"), $"\"{HostsFile.PathOf()}\"") { UseShellExecute = false })?.Dispose(); return null; });
    }
}
