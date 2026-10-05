namespace Mazesta.Core.Providers;

/// <summary>One fan output of the board as the sensor driver exposes it: the duty it is at, the speed the fan reports (when a fan is wired to it) and whether this app holds it.</summary>
public sealed record FanChannel(string Id, string Name, string Part, double? Percent, double? Rpm, bool Manual, int MinPercent, int MaxPercent);

/// <summary>A sensor provider that can also set the duty of the board's fan outputs. Only the outputs the driver can really write are listed: a board whose chip is not
/// supported lists none, and nothing is guessed.</summary>
public interface IFanControlSource
{
    IReadOnlyList<FanChannel> Fans();
    /// <summary>Holds the output at this duty (a percentage, kept within what the output takes). False if the output is not known or refused.</summary>
    bool SetManual(string id, double percent);
    /// <summary>Gives the output back to the board's own (BIOS) control.</summary>
    bool SetAuto(string id);
}
