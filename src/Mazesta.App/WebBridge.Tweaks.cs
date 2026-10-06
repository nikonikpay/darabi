using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Windows;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The Windows tweaks (WinUtil's list, trimmed to what can be undone), Windows Update's three profiles and the DNS resolver. Every change is
    /// one explicit click, is logged, and the page reads the state back from the registry afterwards instead of assuming it took.
    /// </summary>
    private void RegisterTweaks()
    {
        var registry = new WindowsRegistry(); var runner = _sp.GetRequiredService<ICommandRunner>();
        var engine = new TweakEngine(registry, runner);
        bool busy = false;

        string SafeRead(Tweak t)
        {
            try { return engine.Read(t).ToString(); }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { return nameof(TweakState.Unknown); }
        }
        object TweakRow(Tweak t) => new
        {
            id = t.Id, group = t.Group.ToString(), name = Loc.Get(t.NameKey), note = t.NoteKey.Length > 0 ? Loc.Get(t.NoteKey) : "",
            state = SafeRead(t), action = t.IsAction, canUndo = t.CanUndo, restart = t.NeedsRestart,
        };
        object DnsState() => new
        {
            providers = DnsChoice.Providers.Select(p => new { id = p.Key, servers = p.Value }),
            adapters = DnsChoice.Current().Select(a => new { name = a.Name, servers = a.Servers, provider = DnsChoice.Identify(a.Servers) }),
        };
        object State() => new
        {
            busy, tweaks = TweakCatalog.All.Select(TweakRow), presets = TweakCatalog.Presets,
            update = UpdateProfiles.Read(registry).ToString(), dns = DnsState(),
        };

        Method("tweaks.state", _ => State());
        Method("dns.state", _ => DnsState());

        // Ticked tweaks, one after another in the catalog's order (the restore point first), applied or undone; each one's outcome comes back.
        MethodAsync("tweaks.run", async p =>
        {
            if (busy) throw new InvalidOperationException(Loc.Get("Tweaks_Busy"));
            bool undo = Bool(p, "undo");
            var ids = p.TryGetProperty("ids", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array ? a.EnumerateArray().Select(e => e.GetString() ?? "").ToHashSet() : [];
            var results = new List<object>();
            busy = true;
            try
            {
                foreach (var t in TweakCatalog.All.Where(t => ids.Contains(t.Id) && t.Group != TweakGroup.Preference))
                {
                    if (undo && !t.CanUndo) continue;
                    Push("tweakProgress", new { id = t.Id, name = Loc.Get(t.NameKey) });
                    string? error = null, done = null;
                    if (t.Id == "tempFiles")
                    {
                        var (files, bytes, skipped) = await Task.Run(() => TempFiles.Clean(TempFiles.Folders())).ConfigureAwait(true);
                        done = Loc.Format("Tweak_TempFiles_Done", files, (bytes / (1024.0 * 1024)).ToString("F0", System.Globalization.CultureInfo.InvariantCulture), skipped);
                    }
                    else error = await engine.RunAsync(t, !undo, null, CancellationToken.None).ConfigureAwait(true);
                    _log.LogInformation("Tweak {Id} {Direction}: {Result}", t.Id, undo ? "undone" : "applied", error ?? done ?? "ok");
                    results.Add(new { id = t.Id, name = Loc.Get(t.NameKey), error, done, state = SafeRead(t) });
                }
            }
            finally { busy = false; }
            return new { results };
        });

        // A preference switch: applied on the click, like Windows' own settings; the page shows what the registry says afterwards.
        MethodAsync("tweaks.pref", async p =>
        {
            var t = TweakCatalog.Find(Str(p, "id")) is { Group: TweakGroup.Preference } x ? x : throw new ArgumentException("unknown preference");
            bool on = Bool(p, "on");
            string? error = await engine.RunAsync(t, on, null, CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Preference {Id} set {State}: {Result}", t.Id, on ? "on" : "off", error ?? "ok");
            return new { error, state = SafeRead(t) };
        });

        MethodAsync("tweaks.update", async p =>
        {
            var profile = Enum.TryParse<UpdateProfile>(Str(p, "profile"), out var v) && v != UpdateProfile.Custom ? v : throw new ArgumentException("unknown profile");
            string? error = await UpdateProfiles.ApplyAsync(profile, registry, runner, null, CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("Windows Update profile {Profile}: {Result}", profile, error ?? "ok");
            return new { error, update = UpdateProfiles.Read(registry).ToString() };
        });

        // DNS Jumper's test: every resolver on the list and the one in use now, timed on this connection. The page applies the winner only when asked.
        MethodAsync("dns.bench", async _ =>
        {
            var current = DnsChoice.Current().SelectMany(a => a.Servers).Distinct().Where(x => DnsChoice.Identify([x]) == "auto").Take(1).Select(x => ("current", x));
            var scores = await DnsBench.RunAsync(DnsChoice.Providers.Select(p => (p.Key, p.Value[0])).Concat(current), CancellationToken.None).ConfigureAwait(true);
            var best = DnsBench.Best(scores);
            _log.LogInformation("DNS test: {Results}; best {Best}", string.Join(", ", scores.Select(x => $"{x.Provider} {x.Answered}/{x.Asked} {x.MedianMs:F0}ms")), best?.Provider ?? "none");
            return new
            {
                best = best?.Provider, inUse = DnsChoice.Current().Select(a => DnsChoice.Identify(a.Servers)).Distinct().ToArray(),
                scores = scores.OrderBy(x => x.Reliable ? 0 : 1).ThenBy(x => x.MedianMs ?? double.MaxValue)
                    .Select(x => new { provider = x.Provider, server = x.Server, answered = x.Answered, asked = x.Asked, ms = x.MedianMs is { } m ? Math.Round(m, 1) : (double?)null, reliable = x.Reliable }),
            };
        });

        MethodAsync("tweaks.dns", async p =>
        {
            string provider = Str(p, "provider");
            if (provider != "auto" && !DnsChoice.Providers.ContainsKey(provider)) throw new ArgumentException("unknown provider");
            string? error = await DnsChoice.ApplyAsync(provider, runner, CancellationToken.None).ConfigureAwait(true);
            _log.LogInformation("DNS set to {Provider}: {Result}", provider, error ?? "ok");
            return new { error, dns = DnsState() };
        });
    }
}
