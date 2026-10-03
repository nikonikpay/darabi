namespace Mazesta.Core.Gaming;

/// <summary>A Windows service as the service manager reports it. <see cref="StartType"/> is Windows' own number: 2 automatic, 3 manual, 4 disabled.</summary>
public sealed record ServiceInfo(string Name, string DisplayName, int StartType, bool Running);

/// <summary>What a service was before the game mode touched it, kept in the settings so it can be put back even after a restart.</summary>
public sealed record ServiceSnapshot(string Name, int StartType, bool WasRunning);

/// <summary>What happened to one service: <see cref="Changed"/> when it was stopped, disabled, or put back; <see cref="Error"/> in Windows' words
/// when Windows refused (some of its own services may not be changed, even by an administrator).</summary>
public sealed record ServiceOutcome(string Name, bool Changed, string? Error);

/// <summary>The service manager as the game mode uses it: the seam that lets its rules be tested without touching Windows. Each change returns
/// null when it worked, else what Windows answered.</summary>
public interface IServiceControl
{
    /// <summary>Null when the service is not installed on this Windows.</summary>
    ServiceInfo? Query(string name);
    string? SetStartType(string name, int startType);
    string? Stop(string name);
    string? Start(string name);
}

public enum GameServiceGroup { Network, Background }

/// <param name="DefaultOn">Whether the game mode takes it unless the user unticks it. The print spooler is offered but not taken: someone may
/// be printing.</param>
public sealed record GameService(string Name, GameServiceGroup Group, bool DefaultOn = true);

/// <summary>
/// The game mode of the settings page: while it is on, background services a game does not need are stopped and kept from starting again -
/// the ones that use the connection (Windows Update and its downloaders, telemetry) and the ones that keep the disk and the processor busy
/// (search indexing, SysMain). Nothing is removed: what each service was (its start type, whether it ran) is kept, and switching the mode off
/// puts every one back. A service this Windows does not have, or does not let be changed, is reported as such and left alone.
/// </summary>
public static class GameBoost
{
    public const int Disabled = 4;

    public static IReadOnlyList<GameService> Catalog { get; } =
    [
        new("wuauserv", GameServiceGroup.Network), new("UsoSvc", GameServiceGroup.Network), new("BITS", GameServiceGroup.Network), new("DoSvc", GameServiceGroup.Network),
        new("DiagTrack", GameServiceGroup.Network), new("MapsBroker", GameServiceGroup.Network), new("WerSvc", GameServiceGroup.Network),
        new("WSearch", GameServiceGroup.Background), new("SysMain", GameServiceGroup.Background), new("Spooler", GameServiceGroup.Background, DefaultOn: false),
    ];

    public static IReadOnlyList<string> Defaults => [.. Catalog.Where(s => s.DefaultOn).Select(s => s.Name)];

    /// <summary>The chosen services that are in the catalog (a name from an older or edited settings file that is not is ignored), in its order.</summary>
    public static IReadOnlyList<string> Chosen(IReadOnlyList<string>? saved) => saved is null ? Defaults : [.. Catalog.Select(s => s.Name).Where(n => saved.Contains(n, StringComparer.OrdinalIgnoreCase))];

    /// <summary>Stops and disables each chosen service that is installed and not already disabled-and-stopped. A snapshot is kept for every
    /// service that was changed in any way, even when the other half of the change failed, so leaving the mode undoes exactly what was done.</summary>
    public static (IReadOnlyList<ServiceSnapshot> Saved, IReadOnlyList<ServiceOutcome> Outcomes) Enter(IServiceControl services, IEnumerable<string> names)
    {
        var saved = new List<ServiceSnapshot>(); var outcomes = new List<ServiceOutcome>();
        foreach (string name in names)
        {
            if (services.Query(name) is not { } was) continue;   // not on this Windows
            if (was.StartType == Disabled && !was.Running) { outcomes.Add(new(name, false, null)); continue; }
            string? typeError = was.StartType == Disabled ? null : services.SetStartType(name, Disabled);
            string? stopError = was.Running ? services.Stop(name) : null;
            bool changed = was.StartType != Disabled && typeError is null || was.Running && stopError is null;
            if (changed) saved.Add(new(name, was.StartType, was.Running && stopError is null));
            outcomes.Add(new(name, changed, typeError ?? stopError));
        }
        return (saved, outcomes);
    }

    /// <summary>Puts every service back to the start type it had and starts the ones that were running. A service that fails to come back is
    /// reported and stays in <paramref name="left"/>, so the mode still shows as not fully undone.</summary>
    public static IReadOnlyList<ServiceOutcome> Leave(IServiceControl services, IEnumerable<ServiceSnapshot> saved, out IReadOnlyList<ServiceSnapshot> left)
    {
        var outcomes = new List<ServiceOutcome>(); var remaining = new List<ServiceSnapshot>();
        foreach (var s in saved)
        {
            if (services.Query(s.Name) is not { } now) continue;   // uninstalled meanwhile: nothing to put back
            string? error = now.StartType == s.StartType ? null : services.SetStartType(s.Name, s.StartType);
            if (error is null && s.WasRunning && !now.Running) error = services.Start(s.Name);
            if (error is not null) remaining.Add(s);
            outcomes.Add(new(s.Name, error is null, error));
        }
        left = remaining;
        return outcomes;
    }
}
