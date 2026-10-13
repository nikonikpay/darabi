using Mazesta.Diagnostics.Windows;
namespace Mazesta.App;

/// <summary>
/// <c>--selftest=shield</c>: the firewall and the antivirus exclusions on the real Windows. A copy of this program's own file in a temporary folder is blocked in the firewall, listed, and
/// unblocked; the folder is left out of Windows Defender's scans, listed and put back. Each step is read back from Windows, and everything is removed at the end whatever happens.
/// Also the right-click entries are written, read and removed (the user's own, if any, are restored).
/// </summary>
internal static class SelfTestShield
{
    public static async Task RunAsync(Action<string> say)
    {
        var tools = new ShieldTools(new ProcessCommandRunner()); string dir = Path.Combine(Path.GetTempPath(), "mazesta-shield-test-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "mazesta shield ’test’.exe"); File.Copy(Environment.ProcessPath!, exe);
        try
        {
            var fw = await tools.FirewallAsync(default); say($"firewall: {(fw is null ? "not read" : string.Join(", ", fw.Profiles.Select(p => $"{p.Profile}={(p.On ? "on" : "off")}")))}; already blocked from here: {fw?.Blocked.Count}");
            var block = await tools.BlockAsync(exe, false, default); say($"block: {block.Problem} {block.Detail}");
            var after = await tools.FirewallAsync(default); var mine = after?.Blocked.FirstOrDefault(b => b.Path.Equals(exe, StringComparison.OrdinalIgnoreCase));
            say($"listed: {(mine is null ? "NO" : $"yes, in={mine.Inbound} out={mine.Outbound}")}");
            var again = await tools.BlockAsync(exe, false, default); say($"block again: {again.Problem}; rules for it: {(await tools.FirewallAsync(default))?.Blocked.Count(b => b.Path.Equals(exe, StringComparison.OrdinalIgnoreCase))} entry");
            var un = await tools.UnblockAsync(exe, default); say($"unblock: {un.Problem}; still listed: {(await tools.FirewallAsync(default))?.Blocked.Any(b => b.Path.Equals(exe, StringComparison.OrdinalIgnoreCase))}");

            var av = await tools.AvAsync(default); say($"antivirus: defender={av?.Defender} realtime={av?.RealTime} tamper={av?.Tamper} products=[{string.Join("; ", av?.Products.Select(p => $"{p.Name}:{(p.On ? "on" : "off")}") ?? [])}] exclusions={av?.Exclusions.Count}");
            var ex = await tools.ExcludeAsync(dir, false, default); say($"exclude folder: {ex.Problem} {ex.Detail}");
            var listed = (await tools.AvAsync(default))?.Exclusions.Any(e => e.TrimEnd((char)92).Equals(dir, StringComparison.OrdinalIgnoreCase)); say($"listed: {listed}");
            var gone = await tools.UnexcludeAsync(dir, default); say($"remove exclusion: {gone.Problem}; still listed: {(await tools.AvAsync(default))?.Exclusions.Any(e => e.TrimEnd((char)92).Equals(dir, StringComparison.OrdinalIgnoreCase))}");
            say($"too broad refused: {(await tools.ExcludeAsync(Path.GetPathRoot(dir), false, default)).Problem}");

            var before = (ShieldShellMenu.Registered(true), ShieldShellMenu.Registered(false));
            ShieldShellMenu.Set(true, true, Environment.ProcessPath!, "test label"); ShieldShellMenu.Set(false, true, Environment.ProcessPath!, "test label 2");
            say($"right-click entries: {ShieldShellMenu.Registered(true)?.Command} | {ShieldShellMenu.Registered(false)?.Command} | current={ShieldShellMenu.IsCurrent(true, Environment.ProcessPath!, "test label")}");
            ShieldShellMenu.Set(true, false, "", ""); ShieldShellMenu.Set(false, false, "", "");
            say($"removed: {ShieldShellMenu.Registered(true) is null && ShieldShellMenu.Registered(false) is null}");
            if (before.Item1 is { } a) ShieldShellMenu.Set(true, true, Environment.ProcessPath!, a.Label); if (before.Item2 is { } b) ShieldShellMenu.Set(false, true, Environment.ProcessPath!, b.Label);
        }
        finally
        {
            await tools.UnblockAsync(exe, default); await tools.UnexcludeAsync(dir, default);
            try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
