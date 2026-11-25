using System.Globalization; using Mazesta.Core.Health; using Mazesta.Core.Providers; using Mazesta.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.App;

public sealed partial class WebBridge
{
    private Task<object>? _drives; private DateTimeOffset _drivesAt;

    /// <summary>"drives.health": each drive's <see cref="DriveReport"/>, worded in the user's language - its health and temperature with their levels,
    /// the findings as sentences and the parameter table with a flagged row's explanation. Read off the UI thread (WMI and the NVMe log take a second or
    /// two) and kept for a minute, so the dashboard and the storage page share one read.</summary>
    private void RegisterDrives()
    {
        MethodAsync("drives.health", async p =>
        {
            if (_drives is null || Str(p, "fresh") is "true" or "True" || DateTimeOffset.UtcNow - _drivesAt > TimeSpan.FromMinutes(1))
            {
                var provider = _sp.GetRequiredService<IDriveHealthProvider>();
                _drivesAt = DateTimeOffset.UtcNow; _drives = Task.Run(() => (object)new { drives = provider.Read().Select(Drive).ToList() });
            }
            return await _drives.ConfigureAwait(true);
        });
    }

    internal static object Drive(DriveHealth d)
    {
        var r = DriveReport.Of(d);
        string Name(DriveParam x) => x.Name == "DriveAttr_Vendor" ? Loc.Format(x.Name, x.Id ?? "") : Loc.Get(x.Name);
        string Hex(double? id) => ((int)(id ?? 0)).ToString("X2", CultureInfo.InvariantCulture);
        string Issue(DriveIssue i) => i.Code == "AttrFailing"
            ? Loc.Format("DriveIssue_AttrFailing", r.Params.FirstOrDefault(x => x.Id == Hex(i.Count)) is { } a ? Name(a) : Hex(i.Count), Hex(i.Count))
            : Loc.Format("DriveIssue_" + i.Code, (i.Count ?? 0).ToString("0", CultureInfo.InvariantCulture));
        return new
        {
            name = d.Name, label = d.Label, letters = d.Letters, media = d.Media, bus = d.BusType, health = r.HealthPercent, level = r.Level.ToString(),
            verdict = Loc.Get("DriveLevel_" + r.Level), temp = r.TempC is { } t ? Math.Round(t) : (double?)null, tempLevel = r.TempLevel?.ToString(), watchC = r.TempWatchC, failC = r.TempFailC,
            issues = r.Issues.Select(i => new { text = Issue(i), level = i.Level.ToString() }),
            backup = r.Issues.Any(i => i.Level == DriveLevel.Fail && i.Code is not ("Hot" or "Warm")),
            ata = d.Smart is { Count: > 0 },
            @params = r.Params.Select(x => new { id = x.Id, name = Name(x), value = x.Value, level = x.Level.ToString(), why = x.Why is { } w ? Loc.Get(w) : null, cur = x.Current, worst = x.Worst, thr = x.Threshold }),
        };
    }
}
