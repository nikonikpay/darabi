using System.Diagnostics;
namespace Mazesta.Hardware.Rgb;

/// <summary>
/// The makers' own lighting programs (Armoury Crate / Aura, MSI Center / Mystic Light, Gigabyte RGB Fusion, ASRock Polychrome, Lian Li L-Connect, Corsair iCUE and the like, with
/// their services). They hold the same lights and start each other again, so stopping them once is not enough: while this app sets colours their services are also set to
/// "disabled" (so neither a recovery rule nor a companion service can start them), their programs are ended once when the lights are taken over, and everything is put back as it was when
/// the app lets go. What was changed is written to a small file, so a crash cannot leave a service disabled: the next start puts it back. The names are what those programs are
/// called on a normal install; one named differently is simply not found, and the page then says that colours may not stick.
/// </summary>
public sealed class RgbConflicts
{
    private sealed record Maker(string Name, string[] Processes, string[] Services);
    private static readonly Maker[] Makers =
    [
        new("ASUS Armoury Crate / Aura", ["ArmouryCrate.UserSessionHelper", "ArmouryCrate.Service", "ArmourySocketServer", "AacAmbientLighting", "LightingService", "ArmouryCrate", "ArmourySwAgent", "AuraService", "ROGLiveService"], ["LightingService", "ArmouryCrateService", "AuraService"]),
        new("MSI Center / Mystic Light", ["MysticLight", "LEDKeeper2", "MSI Center", "MSI_Central_Service", "Dragon Center", "MSI.CentralServer"], ["MSI_Central_Service", "LEDKeeper2", "MSI Mystic Light Service"]),
        new("Gigabyte RGB Fusion", ["RGBFusion", "GigabyteUpdateService", "RGBFusionService", "GCC", "GigabyteControlCenter"], ["GigabyteUpdateService", "GBTUpdateService"]),
        new("ASRock Polychrome / RGB LED", ["ASRock Polychrome RGB", "ASRRGBLED", "Polychrome", "ASRockRGBLED"], ["ASRRGBLEDService", "ASRockRGBLEDService"]),
        new("Lian Li L-Connect", ["L-Connect 3", "LConnect3", "L-Connect", "LianLi.LConnect"], ["LConnectService", "L-Connect Service", "LianLiService"]),
        new("Corsair iCUE", ["iCUE", "Corsair.Service", "CueLLAccessService", "iCUEDevicePluginHost"], ["CorsairService", "CorsairLLAService", "Corsair Service"]),
        new("NZXT CAM", ["NZXT CAM", "NZXT.CAM"], ["NZXT Service", "NZXTCAMService"]),
        new("SignalRGB", ["SignalRgb", "SignalRgbLauncher"], ["SignalRgbService"]),
        new("Razer Synapse / Chroma", ["Razer Synapse 3", "Razer Synapse", "RazerCentral", "Razer Central", "RzSDKService", "RazerAppEngine", "Razer Chroma SDK Server", "Razer Chroma SDK Service", "RzSDKServer"], ["RzActionSvc", "RazerCentralService", "Razer Synapse Service", "Razer Chroma SDK Service", "Razer Chroma SDK Server", "Razer Game Manager Service", "Razer Game Manager Service 3"]),
        new("Logitech G HUB / Gaming Software", ["lghub", "lghub_agent", "lghub_updater", "lghub_system_tray", "LCore", "LGS", "logi_lamparray_service"], ["LGHUBUpdaterService", "logi_lamparray_service"]),
        new("Redragon", ["Redragon Gaming Mouse", "Redragon Gaming Keyboard", "Redragon Software", "Redragon"], ["RedragonService"]),
        new("SteelSeries GG / HyperX NGENUITY", ["SteelSeriesGG", "SteelSeriesGGClient", "SteelSeriesEngine", "SteelSeriesEngine3", "NGENUITY"], ["SteelSeriesGGUpdateServiceProxy", "HyperX NGENUITY Service"]),
        new("Cooler Master MasterPlus", ["MasterPlus", "CMRGBService"], ["CMRGBService"]),
        new("Thermaltake TT RGB Plus", ["TT RGB Plus", "TTRGBPlus"], ["TTRGBPlusService"]),
    ];

    private sealed record Stopped(string Maker, string? Exe, string? Service, string? StartType, bool WasRunning = true);
    private readonly List<Stopped> _stopped = []; private readonly object _gate = new();
    private readonly string? _journal;

    /// <param name="journal">Where the changed services are written down (null: not kept, as in tests).</param>
    public RgbConflicts(string? journal = null) { _journal = journal; }

    /// <summary>The makers' programs that are running now (their names), nothing is changed.</summary>
    public IReadOnlyList<string> Running() => [.. Makers.Where(m => m.Processes.Any(p => Process.GetProcessesByName(p).Length > 0) || m.Services.Any(IsRunning)).Select(m => m.Name)];

    /// <summary>Names of the makers' programs this object has stopped and not yet put back.</summary>
    public IReadOnlyList<string> StoppedNow { get { lock (_gate) return [.. _stopped.Select(s => s.Maker).Distinct()]; } }

    /// <summary>Stops every running one and disables its service, remembering how to put it back. Done once (a disabled service cannot be started again, so nothing needs watching: no timer,
    /// no load). Returns the makers' names that were stopped.</summary>
    public IReadOnlyList<string> StopAll() { Sweep(first: true); return StoppedNow; }

    private void Sweep(bool first)
    {
        foreach (var m in Makers)
        {
            foreach (var s in m.Services)
            {
                string? type = StartType(s); if (type is null) continue;   // not installed
                bool running = IsRunning(s);
                if (!running && type == "disabled") continue;
                if (first || running)
                {
                    lock (_gate) if (!_stopped.Any(x => x.Service == s)) _stopped.Add(new(m.Name, null, s, type == "disabled" ? JournalType(s) : type, running));
                    Write();
                    if (type != "disabled") Sc("config", s, "start= disabled");
                    if (running) Sc("stop", s);
                }
            }
            foreach (var name in m.Processes)
                foreach (var p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        string? exe = null; try { exe = p.MainModule?.FileName; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                        try { p.Kill(true); p.WaitForExit(3000); lock (_gate) if (!_stopped.Any(x => x.Exe is not null && string.Equals(x.Exe, exe, StringComparison.OrdinalIgnoreCase))) _stopped.Add(new(m.Name, exe, null, null)); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    }
                }
        }
    }

    /// <summary>Puts back what <see cref="StopAll"/> changed: the services' start types, then the services that were running, then the programs, each once.</summary>
    public void RestoreAll()
    {
        List<Stopped> work; lock (_gate) { work = [.. _stopped]; _stopped.Clear(); }
        foreach (var w in Read()) if (!work.Any(x => x.Service == w.Service)) work.Add(w);   // what an earlier run left behind
        PutBack(work);
        TryDelete();
    }

    /// <summary>A crash or a kill can end the app before it let go: services it disabled are put back here, at the next start, unless the process that disabled them still runs.</summary>
    public void RestoreStale()
    {
        var left = Read(out int owner); if (left.Count == 0 || owner == Environment.ProcessId) return;
        try { using var p = Process.GetProcessById(owner); if (!p.HasExited) return; } catch (ArgumentException) { }
        PutBack(left); TryDelete();
    }

    private static void PutBack(List<Stopped> work)
    {
        foreach (var w in work.Where(w => w.Service is not null)) if (w.StartType is { } t && t != "disabled") Sc("config", w.Service!, "start= " + t);
        foreach (var s in work.Where(w => w.Service is not null && w.WasRunning).Select(w => w.Service!).Distinct()) Sc("start", s);
        foreach (var exe in work.Where(w => w.Exe is not null).Select(w => w.Exe!).Distinct(StringComparer.OrdinalIgnoreCase))
            try { if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // The journal is plain text (first line the owner's process id, then "maker|service|start type|was running"), so it needs no serializer.
    private void Write()
    {
        if (_journal is null) return;
        try
        {
            List<string> lines; lock (_gate) lines = [Environment.ProcessId.ToString(), .. _stopped.Where(s => s.Service is not null).Select(s => $"{s.Maker}|{s.Service}|{s.StartType}|{s.WasRunning}")];
            Directory.CreateDirectory(Path.GetDirectoryName(_journal)!); File.WriteAllLines(_journal, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private List<Stopped> Read() => Read(out _);
    private List<Stopped> Read(out int owner)
    {
        owner = 0; if (_journal is null || !File.Exists(_journal)) return [];
        try
        {
            var lines = File.ReadAllLines(_journal); int.TryParse(lines.FirstOrDefault(), out owner);
            return [.. lines.Skip(1).Select(l => l.Split('|')).Where(p => p.Length == 4 && p[1].Length > 0).Select(p => new Stopped(p[0], null, p[1], p[2].Length > 0 ? p[2] : null, p[3] == "True"))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
    private string? JournalType(string service) => Read().FirstOrDefault(x => x.Service == service)?.StartType;
    private void TryDelete() { try { if (_journal is not null) File.Delete(_journal); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    private static bool IsRunning(string service)
    {
        try { return Run("query", service).Contains("RUNNING", StringComparison.Ordinal); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>The service's start type as <c>sc config</c> spells it ("auto", "demand", "delayed-auto", "disabled"), or null when there is no such service.</summary>
    private static string? StartType(string service)
    {
        try
        {
            string o = Run("qc", service); if (!o.Contains("START_TYPE", StringComparison.Ordinal)) return null;
            return o.Contains("DELAYED", StringComparison.Ordinal) ? "delayed-auto" : o.Contains("DISABLED", StringComparison.Ordinal) ? "disabled" : o.Contains("DEMAND_START", StringComparison.Ordinal) ? "demand"
                : o.Contains("AUTO_START", StringComparison.Ordinal) ? "auto" : o.Contains("BOOT_START", StringComparison.Ordinal) ? "boot" : o.Contains("SYSTEM_START", StringComparison.Ordinal) ? "system" : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
    private static void Sc(string verb, string service, string? args = null)
    {
        try { Run(verb, service, args); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
    private static string Run(string verb, string service, string? args = null)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{verb} \"{service}\"" + (args is null ? "" : " " + args)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }) ?? throw new InvalidOperationException("sc.exe");
        string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); return output;
    }

}
