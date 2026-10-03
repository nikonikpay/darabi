using Xunit; using Mazesta.Core.Gaming;
namespace Mazesta.Core.Tests;

public class GameBoostTests
{
    private sealed class Fake : IServiceControl
    {
        public Dictionary<string, (int Start, bool Running)> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Locked { get; } = [];
        public List<string> Calls { get; } = [];
        public ServiceInfo? Query(string name) => Services.TryGetValue(name, out var s) ? new(name, name + " service", s.Start, s.Running) : null;
        public string? SetStartType(string name, int startType) { Calls.Add($"type {name} {startType}"); if (Locked.Contains(name)) return "Access is denied"; Services[name] = (startType, Services[name].Running); return null; }
        public string? Stop(string name) { Calls.Add($"stop {name}"); if (Locked.Contains(name)) return "Access is denied"; Services[name] = (Services[name].Start, false); return null; }
        public string? Start(string name) { Calls.Add($"start {name}"); Services[name] = (Services[name].Start, true); return null; }
    }

    [Fact] public void On_stops_and_disables_what_is_there_and_off_puts_each_one_back_as_it_was()
    {
        var sc = new Fake(); sc.Services["wuauserv"] = (3, true); sc.Services["WSearch"] = (2, true); sc.Services["BITS"] = (3, false); sc.Services["SysMain"] = (4, false);
        var before = sc.Services.ToDictionary(x => x.Key, x => x.Value);
        var (saved, outcomes) = GameBoost.Enter(sc, ["wuauserv", "WSearch", "BITS", "SysMain", "MapsBroker"]);
        Assert.Equal([new("wuauserv", 3, true), new("WSearch", 2, true), new("BITS", 3, false)], saved);   // SysMain was already off; MapsBroker is not installed
        Assert.All(["wuauserv", "WSearch", "BITS", "SysMain"], n => Assert.Equal((GameBoost.Disabled, false), sc.Services[n]));
        Assert.Equal(4, outcomes.Count); Assert.False(outcomes.Single(o => o.Name == "SysMain").Changed);
        var back = GameBoost.Leave(sc, saved, out var left);
        Assert.Empty(left); Assert.All(back, o => Assert.True(o.Changed));
        Assert.Equal(before, sc.Services);
    }

    [Fact] public void A_service_Windows_does_not_let_be_changed_is_reported_and_not_kept()
    {
        var sc = new Fake(); sc.Services["DoSvc"] = (2, true); sc.Locked.Add("DoSvc");
        var (saved, outcomes) = GameBoost.Enter(sc, ["DoSvc"]);
        Assert.Empty(saved); Assert.Equal(new ServiceOutcome("DoSvc", false, "Access is denied"), Assert.Single(outcomes));
        Assert.Equal((2, true), sc.Services["DoSvc"]);
    }

    [Fact] public void The_spooler_is_offered_but_not_taken_by_default_and_unknown_names_are_dropped()
    {
        Assert.DoesNotContain("Spooler", GameBoost.Defaults); Assert.Contains("wuauserv", GameBoost.Defaults);
        Assert.Equal(["wuauserv", "Spooler"], GameBoost.Chosen(["spooler", "NotAService", "wuauserv"]));
        Assert.Equal(GameBoost.Defaults, GameBoost.Chosen(null)); Assert.Empty(GameBoost.Chosen([]));
    }
}
