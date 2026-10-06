using System.IO; using Mazesta.Core.Crashes; using Mazesta.Desktop.Localization; using Mazesta.Diagnostics.Crashes;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    /// <summary>
    /// The blue screens Windows kept a record of, for the Windows tools page and the assistant: each with its stop code, Microsoft's name for
    /// it, its four parameters and what they say, and the usual causes of that code, most likely first. The causes are the reference's, not a
    /// finding about this computer: the page words them as "likely" and says what to check. Read on request only.
    /// </summary>
    private void RegisterCrashes() => MethodAsync("crashes.read", async _ => CrashRows(await Task.Run(CrashReader.Read).ConfigureAwait(true), 50));

    private static object CrashRows(CrashHistory h, int limit)
    {
        static string When(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy/MM/dd HH:mm", Loc.Culture);
        return new
        {
            total = h.Crashes.Count, dumpFiles = h.DumpFiles, dumpsReadable = h.DumpsReadable, logReadable = h.LogReadable, logSince = h.LogSince is { } s ? When(s) : null,
            crashes = h.Crashes.Take(limit).Select(c =>
            {
                var info = BugCheckCatalog.Find(c.Code);
                return new
                {
                    at = When(c.At), atIsRestart = c.AtIsRestart, code = $"0x{c.Code:X8}", name = info?.Name,
                    parameters = c.Parameters?.Select(p => $"0x{p:X}"), parametersMean = BugCheckCatalog.ParametersKey(c.Code) is { } key ? Loc.Get(key) : null,
                    notes = BugCheckCatalog.Notes(c.Code, c.Parameters).Select(Loc.Get),
                    causes = (info?.Causes ?? []).Select(x => new { id = x.ToString(), title = Loc.Get($"Bsod_Cause_{x}"), check = Loc.Get($"Bsod_Cause_{x}_Check") }),
                    dump = c.DumpFile is { } f ? Path.GetFileName(f) : null, uptimeMinutes = c.Uptime is { } up ? Math.Round(up.TotalMinutes) : (double?)null,
                };
            }),
            powerLosses = h.PowerLosses.Count, lastPowerLoss = h.PowerLosses.Count > 0 ? When(h.PowerLosses[0].At) : null,
            powerLossTimes = h.PowerLosses.Take(10).Select(l => When(l.At)),
        };
    }
}
