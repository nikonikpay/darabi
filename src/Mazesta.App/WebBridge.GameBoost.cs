using Mazesta.Core.Gaming; using Mazesta.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The game mode of the settings page: which services it takes (the user's ticks, kept in the settings), whether it is on (the settings hold
    /// what each stopped service was), and the switch. Every service's state is read from Windows for the page, never assumed, and what Windows
    /// answered to each change is shown. Turning the mode off puts back exactly what turning it on changed.
    /// </summary>
    private void RegisterGameBoost()
    {
        var services = _sp.GetRequiredService<IServiceControl>();
        bool busy = false; IReadOnlyList<ServiceOutcome> last = [];

        object Row(GameService s, IReadOnlyList<string> chosen)
        {
            ServiceInfo? info = null; string? unreadable = null;
            try { info = services.Query(s.Name); } catch (System.ComponentModel.Win32Exception e) { unreadable = e.Message; }
            return new
            {
                name = s.Name, group = s.Group.ToString(), display = info?.DisplayName, note = Loc.Get("GameBoost_Svc_" + s.Name), chosen = chosen.Contains(s.Name),
                present = info is not null, running = info?.Running, disabled = info is null ? (bool?)null : info.StartType == GameBoost.Disabled, error = unreadable,
            };
        }
        object State()
        {
            var chosen = GameBoost.Chosen(_config.GameModeServices);
            return new
            {
                on = _config.GameModeSaved is { Count: > 0 }, busy, held = _config.GameModeSaved?.Select(s => s.Name) ?? [],
                services = GameBoost.Catalog.Select(s => Row(s, chosen)).ToList(),
                results = last.Select(o => new { name = o.Name, changed = o.Changed, error = o.Error }),
            };
        }

        MethodAsync("gameboost.state", async _ => await Task.Run(State).ConfigureAwait(true));
        Method("gameboost.tick", p =>
        {
            string name = Str(p, "name");
            if (GameBoost.Catalog.All(s => s.Name != name)) throw new ArgumentException("unknown service");
            var chosen = GameBoost.Chosen(_config.GameModeServices).ToList();
            if (Bool(p, "on")) { if (!chosen.Contains(name)) chosen.Add(name); } else chosen.Remove(name);
            _config.GameModeServices = chosen; _store.Save(_config);
            return null;
        });
        MethodAsync("gameboost.switch", async p =>
        {
            if (busy) throw new InvalidOperationException(Loc.Get("Tweaks_Busy"));
            bool on = Bool(p, "on");
            busy = true;
            try
            {
                if (on && _config.GameModeSaved is not { Count: > 0 })
                {
                    var (saved, outcomes) = await Task.Run(() => GameBoost.Enter(services, GameBoost.Chosen(_config.GameModeServices))).ConfigureAwait(true);
                    _config.GameModeSaved = [.. saved]; last = outcomes;
                }
                else if (!on && _config.GameModeSaved is { Count: > 0 } held)
                {
                    IReadOnlyList<ServiceSnapshot> left = [];
                    last = await Task.Run(() => GameBoost.Leave(services, held, out left)).ConfigureAwait(true);
                    _config.GameModeSaved = left.Count > 0 ? [.. left] : null;
                }
                _store.Save(_config);
                _log.LogInformation("Game mode {State}: {Outcomes}", on ? "on" : "off", string.Join(", ", last.Select(o => $"{o.Name} {(o.Error is null ? o.Changed ? "changed" : "as it was" : "failed: " + o.Error)}")));
            }
            finally { busy = false; }
            return await Task.Run(State).ConfigureAwait(true);
        });
    }
}
