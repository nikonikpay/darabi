using System.Text; using System.Text.Json; using Mazesta.Core.Windows; using Microsoft.Win32;
namespace Mazesta.Diagnostics.Windows;

/// <summary>A scheduled task that is allowed to wake the computer to run (its "Wake the computer to run this task" setting).</summary>
public sealed record WakeTask(string Path, string Name);
/// <summary>A network adapter's Wake-on-LAN switches: a magic packet, and a pattern (any packet that matches a filter). Null when the driver does not say.</summary>
public sealed record WakeNic(string Name, string Description, bool? Magic, bool? Pattern);

/// <param name="Timers">What <c>powercfg /waketimers</c> lists right now, as Windows words it; <paramref name="LastWake"/> is <c>powercfg /lastwake</c>, likewise untouched.</param>
public sealed record WakeSnapshot(IReadOnlyList<WakeDevice> Devices, int? TimersAc, int? TimersDc, int? Maintenance, IReadOnlyList<WakeTask> Tasks,
    IReadOnlyList<WakeNic> Nics, IReadOnlyList<string> Timers, IReadOnlyList<string> LastWake, string? Error);

/// <summary>What "turn everything off" changed, kept so "put it back" restores exactly that and nothing the owner had off already.</summary>
public sealed record WakeUndo(List<string> Devices, List<WakeTask> Tasks, int? TimersAc, int? TimersDc, int? Maintenance, List<WakeNic> Nics);

/// <summary>
/// What can wake a sleeping computer, and the switches for it: devices (<c>powercfg /devicedisablewake</c>), the wake timers setting of the active
/// power plan, Windows' maintenance wake, scheduled tasks that wake it, and the network adapters' Wake-on-LAN. Every change is read back by the
/// caller afterwards; a failure returns the tool's own words. A name or path from the page is matched against what Windows lists before it is used.
/// </summary>
public static class WakeControl
{
    private const string MaintenanceKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance";
    private const string NicRead = "Get-NetAdapter -Physical | ForEach-Object { try { $p = Get-NetAdapterPowerManagement -Name $_.Name -ErrorAction Stop } catch { $p = $null }; [pscustomobject]@{ name=$_.Name; description=$_.InterfaceDescription; magic=[string]$p.WakeOnMagicPacket; pattern=[string]$p.WakeOnPattern } }";
    private const string TaskRead = "Get-ScheduledTask | Where-Object { $_.Settings.WakeToRun -and $_.State -ne 'Disabled' } | ForEach-Object { [pscustomobject]@{ path=$_.TaskPath; name=$_.TaskName } }";

    public static async Task<WakeSnapshot> ReadAsync(ICommandRunner runner, CancellationToken ct)
    {
        var programmable = Pc(runner, "/devicequery wake_programmable", ct); var armed = Pc(runner, "/devicequery wake_armed", ct);
        var timers = Pc(runner, "/waketimers", ct); var last = Pc(runner, "/lastwake", ct); var query = Pc(runner, "/query SCHEME_CURRENT SUB_SLEEP RTCWAKE", ct);
        var nics = Ps(runner, NicRead, true, ct); var tasks = Ps(runner, TaskRead, true, ct);
        await Task.WhenAll(programmable, armed, timers, last, query, nics, tasks).ConfigureAwait(false);
        string? error = programmable.Result.ExitCode != 0 ? Text(programmable.Result) : null;
        var nicList = Json(nics.Result, e => new WakeNic(e.Str("name"), e.Str("description"), e.Mode("magic"), e.Mode("pattern")));
        var (ac, dc) = WakeSources.TimerSetting(query.Result.Output);
        return new(WakeSources.Devices(programmable.Result.Output, armed.Result.Output, nicList.Select(n => n.Description)), ac, dc, MaintenanceWake(),
            Json(tasks.Result, e => new WakeTask(e.Str("path"), e.Str("name"))), nicList, timers.Result.Output, last.Result.Output, error);
    }

    public static async Task<string?> SetDeviceAsync(ICommandRunner runner, string name, bool on, CancellationToken ct)
    {
        name = WakeSources.Clean(name);
        var list = await Pc(runner, "/devicequery wake_programmable", ct).ConfigureAwait(false);
        if (name.Contains('"') || !list.Output.Any(l => string.Equals(WakeSources.Clean(l), name, StringComparison.OrdinalIgnoreCase))) return "unknown device";
        return Failed(await Pc(runner, $"{(on ? "/deviceenablewake" : "/devicedisablewake")} \"{name}\"", ct).ConfigureAwait(false));
    }

    /// <summary>"Allow wake timers" of the active power plan, plugged in and on battery: 0 off, 1 on, 2 important ones only.</summary>
    public static async Task<string?> SetTimersAsync(ICommandRunner runner, int ac, int dc, CancellationToken ct)
    {
        if (ac is < 0 or > 2 || dc is < 0 or > 2) return "bad value";
        foreach (string args in new[] { $"/setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE {ac}", $"/setdcvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE {dc}", "/setactive SCHEME_CURRENT" })
            if (Failed(await Pc(runner, args, ct).ConfigureAwait(false)) is { } e) return e;
        return null;
    }

    public static int? MaintenanceWake() { using var k = Registry.LocalMachine.OpenSubKey(MaintenanceKey); return k?.GetValue("WakeUp") is int v ? v : null; }
    public static void SetMaintenanceWake(int value) { using var k = Registry.LocalMachine.CreateSubKey(MaintenanceKey); k.SetValue("WakeUp", value, RegistryValueKind.DWord); }

    /// <summary>Turning a task's wake off is only done to a task Windows lists as waking; putting it back (from the undo file) skips that check, the names are quoted.</summary>
    public static async Task<string?> SetTaskAsync(ICommandRunner runner, WakeTask task, bool on, CancellationToken ct)
    {
        if (!on && !Json(await Ps(runner, TaskRead, true, ct).ConfigureAwait(false), e => new WakeTask(e.Str("path"), e.Str("name"))).Contains(task)) return "unknown task";
        string p = Q(task.Path), n = Q(task.Name);
        return Failed(await Ps(runner, $"$t=Get-ScheduledTask -TaskPath '{p}' -TaskName '{n}'; $s=$t.Settings; $s.WakeToRun=${(on ? "true" : "false")}; Set-ScheduledTask -TaskPath '{p}' -TaskName '{n}' -Settings $s | Out-Null", false, ct).ConfigureAwait(false));
    }

    public static async Task<string?> SetNicAsync(ICommandRunner runner, string name, bool magic, bool pattern, CancellationToken ct)
    {
        if (!Json(await Ps(runner, NicRead, true, ct).ConfigureAwait(false), e => e.Str("name")).Contains(name)) return "unknown adapter";
        return Failed(await Ps(runner, $"Set-NetAdapterPowerManagement -Name '{Q(name)}' -WakeOnMagicPacket {(magic ? "Enabled" : "Disabled")} -WakeOnPattern {(pattern ? "Enabled" : "Disabled")}", false, ct).ConfigureAwait(false));
    }

    /// <summary>Turns off every way the computer can be woken except its own power button (and lid): devices, wake timers, maintenance, tasks, Wake-on-LAN.
    /// Returns what it changed, and the failures with the name of what failed.</summary>
    public static async Task<(WakeUndo Undo, List<string> Errors)> DisableAllAsync(ICommandRunner runner, WakeSnapshot s, CancellationToken ct)
    {
        var undo = new WakeUndo([], [], null, null, null, []); var errors = new List<string>();
        void Fail(string what, string? e) { if (e is not null) errors.Add($"{what}: {e}"); }
        foreach (var d in s.Devices.Where(d => d.Armed)) { string? e = await SetDeviceAsync(runner, d.Name, false, ct).ConfigureAwait(false); if (e is null) undo.Devices.Add(d.Name); else Fail(d.Name, e); }
        if (s.TimersAc != 0 || s.TimersDc != 0)
        {
            string? e = await SetTimersAsync(runner, 0, 0, ct).ConfigureAwait(false);
            if (e is null) undo = undo with { TimersAc = s.TimersAc, TimersDc = s.TimersDc }; else Fail("wake timers", e);
        }
        if (s.Maintenance != 0) { try { SetMaintenanceWake(0); undo = undo with { Maintenance = s.Maintenance ?? 1 }; } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { Fail("maintenance", e.Message); } }
        foreach (var t in s.Tasks) { string? e = await SetTaskAsync(runner, t, false, ct).ConfigureAwait(false); if (e is null) undo.Tasks.Add(t); else Fail(t.Name, e); }
        foreach (var n in s.Nics.Where(n => n.Magic != false || n.Pattern != false))
        {
            string? e = await SetNicAsync(runner, n.Name, false, false, ct).ConfigureAwait(false);
            if (e is null) undo.Nics.Add(n); else Fail(n.Name, e);
        }
        return (undo, errors);
    }

    public static async Task<List<string>> RestoreAsync(ICommandRunner runner, WakeUndo u, CancellationToken ct)
    {
        var errors = new List<string>();
        void Fail(string what, string? e) { if (e is not null) errors.Add($"{what}: {e}"); }
        foreach (string d in u.Devices) Fail(d, await SetDeviceAsync(runner, d, true, ct).ConfigureAwait(false));
        if (u.TimersAc is { } ac && u.TimersDc is { } dc) Fail("wake timers", await SetTimersAsync(runner, ac, dc, ct).ConfigureAwait(false));
        if (u.Maintenance is { } m) { try { SetMaintenanceWake(m); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException) { Fail("maintenance", e.Message); } }
        foreach (var t in u.Tasks) Fail(t.Name, await SetTaskAsync(runner, t, true, ct).ConfigureAwait(false));
        foreach (var n in u.Nics) Fail(n.Name, await SetNicAsync(runner, n.Name, n.Magic != false, n.Pattern != false, ct).ConfigureAwait(false));
        return errors;
    }

    // ——— the tools ———
    private static string Q(string s) => s.Replace("'", "''", StringComparison.Ordinal);
    private static Task<CommandResult> Pc(ICommandRunner r, string args, CancellationToken ct) => r.RunAsync("powercfg.exe", args, WindowsTool.Oem, null, ct);
    /// <summary>PowerShell with the script passed encoded (no quoting to get wrong); a reading comes back as one JSON array on one line.</summary>
    private static Task<CommandResult> Ps(ICommandRunner r, string script, bool json, CancellationToken ct)
    {
        string body = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + (json ? $"ConvertTo-Json -Compress -InputObject @(& {{ {script} }})" : script);
        return r.RunAsync(@"WindowsPowerShell\v1.0\powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(body)), Encoding.UTF8, null, ct);
    }
    private static string Text(CommandResult r) => string.Join(Environment.NewLine, r.Output).Trim() is { Length: > 0 } o ? o : $"exit code {r.ExitCode}";
    private static string? Failed(CommandResult r) => r.ExitCode == 0 ? null : Text(r);

    private static List<T> Json<T>(CommandResult r, Func<JsonElement, T> make)
    {
        if (r.ExitCode != 0) return [];
        try
        {
            using var doc = JsonDocument.Parse(r.Output.FirstOrDefault(l => l.StartsWith('[')) ?? "[]");   // PowerShell may add progress XML lines around the JSON
            return doc.RootElement.ValueKind == JsonValueKind.Array ? [.. doc.RootElement.EnumerateArray().Select(make)] : [make(doc.RootElement)];
        }
        catch (JsonException) { return []; }
    }
    private static string Str(this JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    /// <summary>The adapter's power-management mode: "Enabled" true, "Disabled" false, anything else (empty, not supported) unknown.</summary>
    private static bool? Mode(this JsonElement e, string name) => e.Str(name) switch { "Enabled" => true, "Disabled" => false, _ => null };
}
