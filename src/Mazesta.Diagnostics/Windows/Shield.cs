using System.Security.Cryptography; using System.Text; using System.Text.Json;
namespace Mazesta.Diagnostics.Windows;

public enum ShieldProblem { None, NotFullPath, NotExe, Missing, SystemFolder, TooBroad, NotAccepted, Failed }
public sealed record ShieldResult(ShieldProblem Problem, string? Detail = null) { public bool Ok => Problem == ShieldProblem.None; }

/// <summary>A program the app has blocked in the Windows firewall: the rules it made for it, by direction.</summary>
public sealed record BlockedProgram(string Path, string Name, bool Inbound, bool Outbound);
public sealed record AvProduct(string Name, bool On);
/// <summary>What is known of the antivirus: Windows Defender's state, every product Windows' security centre lists and Defender's exclusions. <see cref="Defender"/> false: Defender cannot be asked.</summary>
public sealed record AvState(bool Defender, bool RealTime, bool? Tamper, IReadOnlyList<AvProduct> Products, IReadOnlyList<string> Exclusions)
{
    /// <summary>The product that actually guards the computer when it is not Windows Defender: Defender's exclusions do not apply to it.</summary>
    public string? Other => Products.FirstOrDefault(p => p.On && !p.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase))?.Name;
}
public sealed record FirewallState(IReadOnlyList<(string Profile, bool On)> Profiles, IReadOnlyList<BlockedProgram> Blocked);

/// <summary>Which paths may be given to the firewall and the antivirus. Pure, so it is tested without either.</summary>
public static class ShieldPaths
{
    /// <summary>A program for the firewall: a full path to an .exe. The Windows folder holds what the system itself runs, so a program in it is only blocked when the user said so.</summary>
    public static (string? Path, ShieldProblem Problem) Program(string? path, bool mustExist = true, bool allowSystem = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, ShieldProblem.NotFullPath);
        path = path.Trim().Trim('"');
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '<', '>', '|']) >= 0) return (null, ShieldProblem.NotFullPath);
        string full = System.IO.Path.GetFullPath(path);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return (null, ShieldProblem.NotExe);
        if (mustExist && !File.Exists(full)) return (null, ShieldProblem.Missing);
        if (!allowSystem && Inside(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return (full, ShieldProblem.SystemFolder);
        return (full, ShieldProblem.None);
    }

    /// <summary>A file or folder for the antivirus' exclusions. A whole drive, or a folder that holds most of what a drive holds, is refused unless the user said so: an exclusion that wide
    /// leaves the computer unguarded.</summary>
    public static (string? Path, ShieldProblem Problem) Exclusion(string? path, bool mustExist = true, bool allowBroad = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, ShieldProblem.NotFullPath);
        path = path.Trim().Trim('"');
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '<', '>', '|']) >= 0) return (null, ShieldProblem.NotFullPath);
        string full = System.IO.Path.GetFullPath(path); string trimmed = full.Length > 3 ? full.TrimEnd('\\') : full;
        if (mustExist && !File.Exists(trimmed) && !Directory.Exists(trimmed)) return (null, ShieldProblem.Missing);
        if (!allowBroad && IsBroad(trimmed)) return (trimmed, ShieldProblem.TooBroad);
        return (trimmed, ShieldProblem.None);
    }

    internal static bool IsBroad(string full)
    {
        string t = full.TrimEnd('\\');
        if (System.IO.Path.GetPathRoot(full)?.TrimEnd('\\').Equals(t, StringComparison.OrdinalIgnoreCase) == true) return true;   // a whole drive
        string[] wide = [Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users"), Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? ""];
        return wide.Any(w => w.Length > 0 && w.TrimEnd('\\').Equals(t, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Inside(string path, string folder) => folder.Length > 0 && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name part of the firewall rules made for a program: the same program always has the same rules, so blocking twice makes no second pair.</summary>
    public static string RuleId(string path) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..12];
}

/// <summary>
/// The Windows firewall and Windows Defender's exclusions, through PowerShell's own cmdlets (the answers are objects, so a Persian Windows reads like an English one). A program
/// is blocked in both directions by a pair of rules in the group "Mazesta Test"; only those rules are listed and removed, so nothing the user or Windows made is touched.
/// Every change is read back, and what Windows did not take is said so: Tamper Protection or a policy can refuse a Defender exclusion without an error.
/// </summary>
public sealed class ShieldTools(ICommandRunner runner)
{
    public const string RuleGroup = "Mazesta Test", Marker = "MZJSON:";
    private static readonly string Ps = @"WindowsPowerShell\v1.0\powershell.exe";

    /// <summary>The script's text for a path: the path travels as base64 and is decoded inside, so no character of it can be taken for PowerShell's own syntax.</summary>
    internal static string PathLiteral(string path) => $"[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(Encoding.UTF8.GetBytes(path))}'))";

    private const string Prologue = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; ";

    internal static string BlockScript(string path) => Prologue + $"$p={PathLiteral(path)}; $id='{ShieldPaths.RuleId(path)}'; $n=[IO.Path]::GetFileName($p); "
        + $"foreach($d in 'Inbound','Outbound'){{ $r=\"MazestaBlock-$id-$d\"; Get-NetFirewallRule -Name $r -ErrorAction SilentlyContinue | Remove-NetFirewallRule; "
        + $"New-NetFirewallRule -Name $r -DisplayName \"Mazesta Test: block $n ($d)\" -Group '{RuleGroup}' -Direction $d -Action Block -Program $p -Profile Any -Enabled True | Out-Null }}";

    internal static string UnblockScript(string path) => Prologue + $"$id='{ShieldPaths.RuleId(path)}'; foreach($d in 'Inbound','Outbound'){{ Get-NetFirewallRule -Name \"MazestaBlock-$id-$d\" -ErrorAction SilentlyContinue | Remove-NetFirewallRule }}";

    internal const string FirewallScript = Prologue
        + "$rows=@(Get-NetFirewallRule -Group '" + RuleGroup + "' -ErrorAction SilentlyContinue | ForEach-Object { $a=$_ | Get-NetFirewallApplicationFilter; [pscustomobject]@{ n=$_.Name; d=[string]$_.Direction; e=[string]$_.Enabled; a=[string]$_.Action; p=[string]$a.Program } }); "
        + "$prof=@(Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{ n=[string]$_.Name; e=[string]$_.Enabled } }); "
        + "Write-Output ('" + Marker + "' + (ConvertTo-Json -Compress -Depth 4 -InputObject ([pscustomobject]@{ rules=$rows; profiles=$prof })))";

    internal const string AvScript = "$ErrorActionPreference='SilentlyContinue'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; "
        + "$mp=Get-MpComputerStatus; $pref=Get-MpPreference; "
        + "$av=@(Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | ForEach-Object { [pscustomobject]@{ n=[string]$_.displayName; on=((([int]$_.productState) -band 0xF000) -eq 0x1000) } }); "
        + "$ex=@($pref.ExclusionPath | Where-Object { $_ }); "
        + "Write-Output ('" + Marker + "' + (ConvertTo-Json -Compress -Depth 4 -InputObject ([pscustomobject]@{ defender=[bool]$mp; real=[bool]$mp.RealTimeProtectionEnabled; tamper=$mp.IsTamperProtected; products=$av; exclusions=$ex })))";

    internal static string ExcludeScript(string path, bool add) => Prologue + $"$p={PathLiteral(path)}; " + (add ? "Add-MpPreference -ExclusionPath $p" : "Remove-MpPreference -ExclusionPath $p");

    private async Task<(int Code, string Text, string? Json)> RunAsync(string script, CancellationToken ct)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var r = await runner.RunAsync(Ps, "-NoProfile -NonInteractive -EncodedCommand " + encoded, Encoding.UTF8, null, ct).ConfigureAwait(false);
        string? json = r.Output.FirstOrDefault(l => l.StartsWith(Marker, StringComparison.Ordinal))?[Marker.Length..];
        return (r.ExitCode, string.Join(" ", r.Output.Where(l => !l.StartsWith(Marker, StringComparison.Ordinal) && !l.StartsWith("#< CLIXML", StringComparison.Ordinal) && !l.StartsWith('<'))), json);
    }

    /// <summary>The firewall's profiles and the programs blocked from here. Null when Windows could not be asked.</summary>
    public async Task<FirewallState?> FirewallAsync(CancellationToken ct)
    {
        var (_, _, json) = await RunAsync(FirewallScript, ct).ConfigureAwait(false);
        return json is null ? null : ParseFirewall(json);
    }

    internal static FirewallState? ParseFirewall(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var profiles = Items(root, "profiles").Select(p => (Str(p, "n"), Str(p, "e").Equals("True", StringComparison.OrdinalIgnoreCase))).ToList();
            var byPath = new Dictionary<string, (bool In, bool Out)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Items(root, "rules"))
            {
                string program = Str(r, "p"); if (program.Length == 0 || !Str(r, "a").Equals("Block", StringComparison.OrdinalIgnoreCase) || !Str(r, "e").Equals("True", StringComparison.OrdinalIgnoreCase)) continue;
                var was = byPath.GetValueOrDefault(program); bool inbound = Str(r, "d").Equals("Inbound", StringComparison.OrdinalIgnoreCase);
                byPath[program] = (was.In || inbound, was.Out || !inbound);
            }
            return new FirewallState(profiles, [.. byPath.Select(kv => new BlockedProgram(kv.Key, System.IO.Path.GetFileName(kv.Key), kv.Value.In, kv.Value.Out)).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)]);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Blocks a program in both directions, then reads the rules back to be sure both are there.</summary>
    public async Task<ShieldResult> BlockAsync(string? path, bool allowSystem, CancellationToken ct)
    {
        var (full, problem) = ShieldPaths.Program(path, mustExist: true, allowSystem); if (problem != ShieldProblem.None) return new(problem);
        var r = await RunAsync(BlockScript(full!), ct).ConfigureAwait(false);
        if (r.Code != 0) return new(ShieldProblem.Failed, r.Text);
        var now = await FirewallAsync(ct).ConfigureAwait(false);
        return now?.Blocked.Any(b => b.Path.Equals(full, StringComparison.OrdinalIgnoreCase) && b.Inbound && b.Outbound) == true ? new(ShieldProblem.None) : new(ShieldProblem.NotAccepted, r.Text);
    }

    public async Task<ShieldResult> UnblockAsync(string? path, CancellationToken ct)
    {
        var (full, problem) = ShieldPaths.Program(path, mustExist: false, allowSystem: true); if (full is null) return new(problem);
        var r = await RunAsync(UnblockScript(full), ct).ConfigureAwait(false);
        if (r.Code != 0) return new(ShieldProblem.Failed, r.Text);
        var now = await FirewallAsync(ct).ConfigureAwait(false);
        return now?.Blocked.Any(b => b.Path.Equals(full, StringComparison.OrdinalIgnoreCase)) == true ? new(ShieldProblem.NotAccepted, r.Text) : new(ShieldProblem.None);
    }

    public async Task<AvState?> AvAsync(CancellationToken ct)
    {
        var (_, _, json) = await RunAsync(AvScript, ct).ConfigureAwait(false);
        return json is null ? null : ParseAv(json);
    }

    internal static AvState? ParseAv(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            bool defender = root.TryGetProperty("defender", out var d) && d.ValueKind == JsonValueKind.True, real = root.TryGetProperty("real", out var rt) && rt.ValueKind == JsonValueKind.True;
            bool? tamper = root.TryGetProperty("tamper", out var tp) && tp.ValueKind is JsonValueKind.True or JsonValueKind.False ? tp.ValueKind == JsonValueKind.True : null;
            var products = Items(root, "products").Select(p => new AvProduct(Str(p, "n"), p.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True)).Where(p => p.Name.Length > 0).ToList();
            var exclusions = Items(root, "exclusions").Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "").Where(e => e.Length > 0).ToList();
            return new AvState(defender, real, tamper, products, exclusions);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Puts a file or folder in Windows Defender's exclusions and reads them back: Defender does not always say when it refuses.</summary>
    public Task<ShieldResult> ExcludeAsync(string? path, bool allowBroad, CancellationToken ct) => ChangeExclusionAsync(path, add: true, allowBroad, ct);
    public Task<ShieldResult> UnexcludeAsync(string? path, CancellationToken ct) => ChangeExclusionAsync(path, add: false, allowBroad: true, ct);

    private async Task<ShieldResult> ChangeExclusionAsync(string? path, bool add, bool allowBroad, CancellationToken ct)
    {
        var (full, problem) = ShieldPaths.Exclusion(path, mustExist: add, allowBroad); if (problem != ShieldProblem.None) return new(problem);
        var before = await AvAsync(ct).ConfigureAwait(false);
        if (before is null || !before.Defender) return new(ShieldProblem.Failed, "Windows Defender cannot be asked.");
        var r = await RunAsync(ExcludeScript(full!, add), ct).ConfigureAwait(false);
        if (r.Code != 0) return new(ShieldProblem.Failed, r.Text);
        var now = await AvAsync(ct).ConfigureAwait(false);
        bool has = now?.Exclusions.Any(e => e.TrimEnd('\\').Equals(full!.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) == true;
        return has == add ? new(ShieldProblem.None) : new(ShieldProblem.NotAccepted, r.Text);
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) yield break;
        if (v.ValueKind == JsonValueKind.Array) { foreach (var e in v.EnumerateArray()) yield return e; }
        else if (v.ValueKind is JsonValueKind.Object or JsonValueKind.String) yield return v;   // PowerShell writes a list of one as the one
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
