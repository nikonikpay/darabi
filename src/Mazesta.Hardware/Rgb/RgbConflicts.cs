using System.Diagnostics;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// The makers' own lighting programs (Armoury Crate / Aura, MSI Center / Mystic Light, Gigabyte Control Center / RGB Fusion, Corsair iCUE and their
/// services). They hold the same lights, so while this app sets colours they are stopped, and put back when it lets go. The lists are what those programs
/// are called on a normal install; one named differently is simply not found, and the page then says that colours may not stick.
/// </summary>
public sealed class RgbConflicts
{
    private sealed record Maker(string Name, string[] Processes, string[] Services);
    private static readonly Maker[] Makers =
    [
        new("ASUS Armoury Crate / Aura", ["ArmouryCrate.UserSessionHelper", "ArmouryCrate.Service", "ArmourySocketServer", "AacAmbientLighting", "LightingService", "ArmouryCrate"], ["LightingService", "ArmouryCrateService"]),
        new("MSI Center / Mystic Light", ["MysticLight", "LEDKeeper2", "MSI Center", "MSI_Central_Service", "Dragon Center"], ["MSI_Central_Service", "LEDKeeper2"]),
        new("Gigabyte RGB Fusion", ["RGBFusion", "GigabyteUpdateService", "RGBFusionService"], ["GigabyteUpdateService"]),
        new("Corsair iCUE", ["iCUE", "Corsair.Service", "CueLLAccessService"], ["CorsairService", "CorsairLLAService", "Corsair Service"]),
    ];

    private sealed record Stopped(string Maker, string? Exe, string? Service);
    private readonly List<Stopped> _stopped = [];

    /// <summary>The makers' programs that are running now (their names), nothing is changed.</summary>
    public IReadOnlyList<string> Running() => [.. Makers.Where(m => m.Processes.Any(p => Process.GetProcessesByName(p).Length > 0) || m.Services.Any(IsRunning)).Select(m => m.Name)];

    /// <summary>Names of the makers' programs this object has stopped and not yet put back.</summary>
    public IReadOnlyList<string> StoppedNow => [.. _stopped.Select(s => s.Maker).Distinct()];

    /// <summary>Stops every running one, remembering how to start it again. Returns the makers' names that were stopped.</summary>
    public IReadOnlyList<string> StopAll()
    {
        foreach (var m in Makers)
        {
            foreach (var s in m.Services.Where(IsRunning)) { Sc("stop", s); _stopped.Add(new(m.Name, null, s)); }
            foreach (var name in m.Processes)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        string? exe = null; try { exe = p.MainModule?.FileName; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                        try { p.Kill(true); p.WaitForExit(3000); _stopped.Add(new(m.Name, exe, null)); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    }
                }
        }
        return StoppedNow;
    }

    /// <summary>Starts again what <see cref="StopAll"/> stopped: services first, then programs, each once.</summary>
    public void RestoreAll()
    {
        var work = _stopped.ToList(); _stopped.Clear();
        foreach (var s in work.Where(w => w.Service is not null).Select(w => w.Service!).Distinct()) Sc("start", s);
        foreach (var exe in work.Where(w => w.Exe is not null).Select(w => w.Exe!).Distinct(StringComparer.OrdinalIgnoreCase))
            try { if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static bool IsRunning(string service)
    {
        try { return Run("query", service).Contains("RUNNING", StringComparison.Ordinal); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
    private static void Sc(string verb, string service)
    {
        try { Run(verb, service); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    private static string Run(string verb, string service)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{verb} \"{service}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }) ?? throw new InvalidOperationException("sc.exe");
        string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); return output;
    }
}
