using System.Diagnostics; using System.Security.Cryptography; using Microsoft.Extensions.Logging; using Microsoft.Win32;
namespace Mazesta.Hardware.Lhm;

public enum PawnIoInstallResult { AlreadyInstalled, Installed, NotElevated, SetupMissing, SetupTampered, Failed }

/// <summary>
/// PawnIO is the kernel driver LibreHardwareMonitor (0.9.5+) reads CPU MSRs, the Ryzen SMU, the Super I/O and SMBus through. Without it
/// every CPU temperature, per-core clock and package power stays empty on every machine, which is what the field reports showed. The
/// driver is not part of Windows, so the app ships the official signed setup next to the exe and installs it silently the first time it
/// runs elevated on a machine that lacks it - the same thing the LibreHardwareMonitor app does, minus the dialog a service shop does not want.
/// </summary>
public static class PawnIoDriver
{
    public const string SetupFileName = "PawnIO_setup.exe";
    /// <summary>SHA-256 of the official PawnIO 2.2.0 setup (github.com/namazso/PawnIO.Setup, signed by namazso.eu). The build downloads the
    /// same file and checks the same hash; checking again here means a replaced setup next to the exe is never run with admin rights.</summary>
    public const string SetupSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

    public static string SetupPath => Path.Combine(AppContext.BaseDirectory, "Redist", SetupFileName);

    /// <summary>Read fresh every call: LHM's own <c>PawnIo.IsInstalled</c> is fixed at type load, so it stays false after an install in
    /// this process. The service key covers setups that register the driver without the uninstall entry.</summary>
    public static bool IsInstalled()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var uninstall = hklm.OpenSubKey(UninstallKey); using var service = hklm.OpenSubKey(ServiceKey);
        // A removal that stopped half way leaves the registry keys without the driver file: that is not an installed driver, and counting it
        // as one is why a reinstall of the app never put PawnIO back.
        if (service is not null) return DriverFileExists(service.GetValue("ImagePath") as string);
        return uninstall?.GetValue("DisplayVersion") is string;
    }

    /// <summary>The service's <c>ImagePath</c> (<c>\SystemRoot\System32\…</c>, <c>\??\C:\…</c> or a plain path) names a file that is there; a path in a form not recognised counts as there.</summary>
    public static bool DriverFileExists(string? imagePath) => FilePath(imagePath) is not { } p || File.Exists(p);

    private static string? FilePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string p = imagePath.Trim().Trim('"');
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) return p[4..];
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p[12..]);
        return Path.IsPathRooted(p) ? p : null;
    }

    /// <summary>The version the setup next to the exe installs; the library's CPU modules are built against it.</summary>
    public static readonly Version Carried = new(2, 2, 0);

    /// <summary>The version the installed-programs list records (the official setup writes it); null when none is recorded.</summary>
    public static Version? ListedVersion()
    {
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); using var k = hklm.OpenSubKey(UninstallKey);
                if (Version.TryParse(k?.GetValue("DisplayVersion") as string, out var v)) return v;
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Whether running the carried setup over a driver that reads nothing can help: it was never recorded (what a field report of a 14600K showed: running,
    /// no version, every CPU, board and memory sensor empty) or an older one is recorded.</summary>
    public static bool WorthSetup(Version? listed) => listed is null || listed < Carried;

    /// <summary>For an installed driver whose sensors read nothing: runs the carried setup over it (once a day) when <see cref="WorthSetup"/>; true when the driver is recorded current afterwards, so the sensors are worth opening again.</summary>
    public static bool Repair(bool elevated, ILogger log, string? setupPath = null)
    {
        if (!elevated || !IsInstalled() || !WorthSetup(ListedVersion())) return false;
        log.LogWarning("The PawnIO driver reads nothing and is recorded as {Version}; running the carried {Carried} setup over it", ListedVersion()?.ToString() ?? "no version", Carried);
        return RunCarried(setupPath ?? SetupPath, () => WorthSetup(ListedVersion()), log) == PawnIoInstallResult.Installed;
    }

    /// <summary>The owner's rule: every install or update of the app puts the carried driver in again, even a current one. Due when the driver is missing, recorded
    /// older than the carried one, or was last put in by another version of the app (the installer also empties Redist, where that note is kept).</summary>
    public static bool IsDue(bool installed, Version? listed, string? doneBy, string app) => !installed || listed is { } v && v < Carried || doneBy != app;

    private static string AppVersion => typeof(PawnIoDriver).Assembly.GetName().Version?.ToString() ?? "";
    private static string Beside(string setupPath, string name) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(setupPath))!, name);
    private static string? DoneBy(string setupPath)
    {
        try { string file = Beside(setupPath, "pawnio-setup.done"); return File.Exists(file) ? File.ReadAllText(file).Trim() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static PawnIoInstallResult EnsureInstalled(bool elevated, ILogger log, string? setupPath = null)
    {
        string path = setupPath ?? SetupPath;
        bool Due() => IsDue(IsInstalled(), ListedVersion(), DoneBy(path), AppVersion);
        if (!elevated) return IsInstalled() ? PawnIoInstallResult.AlreadyInstalled : PawnIoInstallResult.NotElevated;
        return RunCarried(path, Due, log);   // "due" is asked inside the gate, so a caller also waits out a setup another is running
    }

    /// <summary>One setup at a time on the machine: the app and its tray can both get here after an update, and two runs would take each other's driver out.</summary>
    private static PawnIoInstallResult RunCarried(string setupPath, Func<bool> due, ILogger log)
    {
        using var gate = new Mutex(false, @"Global\Mazesta.PawnIoSetup");
        try { if (!gate.WaitOne(TimeSpan.FromMinutes(7))) return PawnIoInstallResult.Failed; }
        catch (AbandonedMutexException) { }   // the holder ended without letting go: the gate is ours now
        try { return due() ? RunLocked(setupPath, log) : PawnIoInstallResult.AlreadyInstalled; }
        finally { gate.ReleaseMutex(); }
    }

    private static PawnIoInstallResult RunLocked(string setupPath, ILogger log)
    {
        bool before = IsInstalled();
        if (!File.Exists(setupPath)) { log.LogWarning("{Setup} is missing; the PawnIO driver is {State}", setupPath, before ? "left as it is" : "not installed, so CPU sensors will be empty"); return PawnIoInstallResult.SetupMissing; }
        string hash; using (var fs = File.OpenRead(setupPath)) hash = Convert.ToHexString(SHA256.HashData(fs));
        if (!hash.Equals(SetupSha256, StringComparison.OrdinalIgnoreCase)) { log.LogError("{Setup} has hash {Hash}, not the official {Expected}; not running it", setupPath, hash, SetupSha256); return PawnIoInstallResult.SetupTampered; }
        // A setup that Windows blocks (the driver blocklist, Smart App Control) fails the same way every time: it is tried once a day, not at every start and every tray check.
        // The note sits beside the setup file in Redist, so installing or updating the app (which replaces that folder) clears it and tries again at once.
        string marker = Beside(setupPath, "pawnio-failed.flag");
        if (File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < TimeSpan.FromDays(1)) { log.LogWarning("PawnIO setup failed less than a day ago; not retried yet"); return before ? PawnIoInstallResult.AlreadyInstalled : PawnIoInstallResult.Failed; }
        var (code, removed) = Reinstall(args => Run(setupPath, args, log));
        bool installed = IsInstalled(), ok = code == RebootRequired || code == 0 && installed && !WorthSetup(ListedVersion());
        if (code == RebootRequired) log.LogWarning("The PawnIO setup asks for a Windows restart before the driver works");
        try
        {
            if (ok) { File.WriteAllText(Beside(setupPath, "pawnio-setup.done"), AppVersion); File.Delete(marker); return PawnIoInstallResult.Installed; }
            // A driver this run took out and could not put back is tried again at the next start, not tomorrow.
            if (!removed || installed) File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.LogWarning(e, "The PawnIO setup's note could not be written beside {Setup}", setupPath); return ok ? PawnIoInstallResult.Installed : PawnIoInstallResult.Failed; }
        log.LogError("The PawnIO setup did not take (exit {Code}); driver installed: {Installed}. A Windows restart, or removing the old driver, may be needed", code, installed);
        // An old driver that stays old (in use until a restart) still serves what it can: not a failure.
        return before && installed ? PawnIoInstallResult.AlreadyInstalled : PawnIoInstallResult.Failed;
    }

    private const int AlreadyThere = 183, RebootRequired = 3010;   // the setup's exit codes are Windows error numbers

    /// <summary>The setup leaves a driver of its own version alone (-install ends with 183, seen with 2.2.0 over 2.2.0), so putting it in again means taking it out first.
    /// Returns the last exit code (null: it did not run or did not end) and whether the driver was taken out.</summary>
    public static (int? Code, bool Removed) Reinstall(Func<string, int?> run)
    {
        int? code = run("-install -silent");
        if (code != AlreadyThere) return (code, false);
        int? gone = run("-uninstall -silent");
        return gone is 0 or RebootRequired ? (run("-install -silent"), true) : (gone, false);
    }

    /// <summary>The banner's "install by hand": opens the carried official setup with its own window (its Install button), after the same hash check; true when it was opened.
    /// Works where the silent install fails, which is what running the file from the program's folder by hand showed.</summary>
    public static bool LaunchSetup(ILogger log, string? setupPath = null)
    {
        setupPath ??= SetupPath;
        if (!File.Exists(setupPath)) { log.LogWarning("{Setup} is missing", setupPath); return false; }
        string hash; using (var fs = File.OpenRead(setupPath)) hash = Convert.ToHexString(SHA256.HashData(fs));
        if (!hash.Equals(SetupSha256, StringComparison.OrdinalIgnoreCase)) { log.LogError("{Setup} has hash {Hash}, not the official one; not running it", setupPath, hash); return false; }
        try { using var p = Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(setupPath))! }); return p is not null; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { log.LogError(e, "The PawnIO setup could not be opened"); return false; }
    }

    /// <summary>Programs that read the same CPU registers or the Ryzen's SMU through a driver of their own and can leave them unreadable for a second reader (or crash holding them); named in the log when the CPU sensors come back empty.</summary>
    private static readonly string[] Competing = ["HWiNFO64", "HWiNFO32", "AMD Ryzen Master", "AMDRyzenMasterDriver", "ArmouryCrate.Service", "AsusCertService", "iCUE", "MSIAfterburner", "CPUZ_x64", "CPU-Z", "OCCT", "RTSS"];

    /// <summary>The driver's state, written for the log when the CPU sensors read nothing: the service (installed, running, stopped), its version, and which tools that might be in the way are running.</summary>
    public static string Describe()
    {
        string service = ServiceState();
        var running = Competing.Where(n => { var p = Process.GetProcessesByName(n); foreach (var x in p) x.Dispose(); return p.Length > 0; }).ToList();
        return $"PawnIO service: {service}; version: {ListedVersion()?.ToString() ?? "unknown"}; driver file: {DriverFile()}; tools that may hold the CPU registers: {(running.Count > 0 ? string.Join(", ", running) : "none running")}";
    }

    /// <summary>The file the driver's service loads and that file's own version, for the log.</summary>
    private static string DriverFile()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); using var service = hklm.OpenSubKey(ServiceKey);
            if (FilePath(service?.GetValue("ImagePath") as string) is not { } path) return "unknown";
            return File.Exists(path) ? $"{path} ({FileVersionInfo.GetVersionInfo(path).FileVersion ?? "no version"})" : $"{path} (missing)";
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return "unreadable"; }
    }

    /// <summary>"RUNNING", "STOPPED" … from <c>sc query</c>, or "unknown"; the state of a driver is not in the registry.</summary>
    private static string ServiceState()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", "query PawnIO") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (p is null) return "unknown";
            string text = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
            foreach (var state in new[] { "RUNNING", "STOPPED", "START_PENDING", "STOP_PENDING" }) if (text.Contains(state, StringComparison.Ordinal)) return state;
            return text.Contains("1060", StringComparison.Ordinal) ? "not installed" : "unknown";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return "unknown"; }
    }

    /// <summary>Starts the driver's service when it is installed but stopped (Windows can leave it so after a failed start); true when it was stopped and is now running.</summary>
    public static bool StartIfStopped(ILogger log)
    {
        if (ServiceState() != "STOPPED") return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", "start PawnIO") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            p?.WaitForExit(10000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { log.LogWarning(e, "The PawnIO service could not be started"); return false; }
        bool running = ServiceState() == "RUNNING";
        log.LogInformation("The PawnIO service was stopped; started it: {Running}", running);
        return running;
    }

    private static int? Run(string setupPath, string arguments, ILogger log)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return null;
            if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { log.LogError("PawnIO setup {Arguments} did not finish within 2 minutes", arguments); return null; }
            log.LogInformation("PawnIO setup {Arguments} from {Setup} ended with {Code}", arguments, setupPath, p.ExitCode);
            return p.ExitCode;
        }
        catch (Exception ex) { log.LogError(ex, "PawnIO setup could not run"); return null; }
    }
}
