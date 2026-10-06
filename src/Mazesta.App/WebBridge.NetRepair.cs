using System.Text.Json; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The connection troubleshooter of the Windows tools page. "netfix.check" only reads and probes; "netfix.run" does the repairs the user
    /// ticked, in a fixed order (proxy, DNS, DNS cache, and last the stack reset), and says what each one answered. The proxy settings that were
    /// removed are returned and logged, so they can be put back by hand.
    /// </summary>
    private void RegisterNetRepair()
    {
        var registry = new WindowsRegistry(); var runner = _sp.GetRequiredService<ICommandRunner>();
        bool busy = false;

        MethodAsync("netfix.check", async _ =>
        {
            var checks = await NetRepair.CheckAsync(registry, CancellationToken.None).ConfigureAwait(true);
            var verdict = NetRepair.Diagnose(checks); var proxy = NetRepair.ReadProxy(registry);
            _log.LogInformation("Connection check: {Verdict}; {Steps}", verdict, string.Join(" | ", checks.Select(c => $"{c.Id} {c.Result} {c.Detail}")));
            return new
            {
                verdict = verdict.ToString(), checks = checks.Select(c => new { id = c.Id, result = c.Result.ToString(), detail = c.Detail }),
                proxy = new { set = proxy.HasData, enabled = proxy.Enabled, server = proxy.Server, script = proxy.AutoConfigUrl }, suggested = NetRepair.Suggested(verdict, proxy),
            };
        });

        MethodAsync("netfix.run", async p =>
        {
            if (busy) throw new InvalidOperationException(Loc.Get("Tweaks_Busy"));
            var steps = p.TryGetProperty("steps", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(e => e.GetString() ?? "").ToHashSet() : [];
            string dns = Str(p, "dns") == "google" ? "google" : "auto";
            var results = new List<object>(); bool restart = false;
            busy = true;
            try
            {
                if (steps.Contains(NetRepair.FixProxy))
                {
                    var (before, error) = await NetRepair.ClearProxyAsync(registry, runner, CancellationToken.None).ConfigureAwait(true);
                    NetRepair.AnnounceProxyChange();
                    _log.LogInformation("Proxy settings cleared (were: enabled {Enabled}, server {Server}, script {Script}): {Result}", before.Enabled, before.Server, before.AutoConfigUrl, error ?? "ok");
                    results.Add(new { id = NetRepair.FixProxy, error, done = before.HasData ? Loc.Format("NetFix_Proxy_Was", before.Server ?? "—", before.AutoConfigUrl ?? "—") : Loc.Get("NetFix_Proxy_Empty") });
                }
                if (steps.Contains(NetRepair.FixDns))
                {
                    string? error = await DnsChoice.ApplyAsync(dns, runner, CancellationToken.None).ConfigureAwait(true);
                    _log.LogInformation("Connection repair: DNS set to {Provider}: {Result}", dns, error ?? "ok");
                    results.Add(new { id = NetRepair.FixDns, error, done = Loc.Get(dns == "google" ? "NetFix_Dns_Google_Done" : "NetFix_Dns_Auto_Done") });
                }
                if (steps.Contains(NetRepair.FixFlush))
                {
                    string? error = await NetRepair.FlushDnsAsync(runner, CancellationToken.None).ConfigureAwait(true);
                    _log.LogInformation("Connection repair: DNS cache emptied: {Result}", error ?? "ok");
                    results.Add(new { id = NetRepair.FixFlush, error, done = Loc.Get("NetFix_Flush_Done") });
                }
                if (steps.Contains(NetRepair.FixReset))
                {
                    var (error, output) = await NetRepair.ResetStackAsync(runner, CancellationToken.None).ConfigureAwait(true);
                    _log.LogInformation("Connection repair: Winsock and IP reset: {Result}; {Output}", error ?? "ok", string.Join(" / ", output));
                    restart = error is null;
                    results.Add(new { id = NetRepair.FixReset, error, done = Loc.Get("NetFix_Reset_Done"), output });
                }
            }
            finally { busy = false; }
            return new { results, restart };
        });
    }
}
