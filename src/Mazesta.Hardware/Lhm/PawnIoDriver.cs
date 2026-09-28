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
        return uninstall?.GetValue("DisplayVersion") is string || service is not null;
    }

    public static PawnIoInstallResult EnsureInstalled(bool elevated, ILogger log, string? setupPath = null)
    {
        if (IsInstalled()) return PawnIoInstallResult.AlreadyInstalled;
        if (!elevated) return PawnIoInstallResult.NotElevated;
        setupPath ??= SetupPath;
        if (!File.Exists(setupPath)) { log.LogWarning("PawnIO is not installed and {Setup} is missing; CPU sensors will be empty", setupPath); return PawnIoInstallResult.SetupMissing; }
        string hash; using (var fs = File.OpenRead(setupPath)) hash = Convert.ToHexString(SHA256.HashData(fs));
        if (!hash.Equals(SetupSha256, StringComparison.OrdinalIgnoreCase)) { log.LogError("{Setup} has hash {Hash}, not the official {Expected}; not running it", setupPath, hash, SetupSha256); return PawnIoInstallResult.SetupTampered; }
        try
        {
            log.LogInformation("PawnIO is not installed; installing it silently from {Setup}", setupPath);
            using var p = Process.Start(new ProcessStartInfo(setupPath, "-install -silent") { UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return PawnIoInstallResult.Failed;
            if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { log.LogError("PawnIO setup did not finish within 2 minutes"); return PawnIoInstallResult.Failed; }
            bool ok = IsInstalled();
            log.Log(ok ? LogLevel.Information : LogLevel.Error, "PawnIO setup exited with {Code}; driver installed: {Ok}", p.ExitCode, ok);
            return ok ? PawnIoInstallResult.Installed : PawnIoInstallResult.Failed;
        }
        catch (Exception ex) { log.LogError(ex, "PawnIO setup could not run"); return PawnIoInstallResult.Failed; }
    }
}
