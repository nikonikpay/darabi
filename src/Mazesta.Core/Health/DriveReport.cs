using System.Globalization; using Mazesta.Core.Providers;
namespace Mazesta.Core.Health;

public enum DriveLevel { Good, Watch, Fail }

/// <summary>One row of a drive's parameter table. <see cref="Name"/> and <see cref="Why"/> are string keys the UI words (a flagged row opens to its
/// <see cref="Why"/>); <see cref="Id"/> is the ATA attribute number in hex, null for an NVMe log field. <see cref="Value"/> is Latin text.</summary>
public sealed record DriveParam(string? Id, string Name, string Value, DriveLevel Level = DriveLevel.Good, string? Why = null, int? Current = null, int? Worst = null, int? Threshold = null);

/// <summary>A finding, worded by the UI from <c>DriveIssue_</c> + <see cref="Code"/> with <see cref="Count"/> as its number.</summary>
public sealed record DriveIssue(string Code, DriveLevel Level, double? Count = null);

/// <summary>
/// A drive's health the way a technician reads it in Hard Disk Sentinel or CrystalDiskInfo, as data: the health in percent, the temperature and its level,
/// the findings in words and every parameter the drive reported with the faulty ones marked. One reading for the storage page, the dashboard, the checkup
/// and the tray. The findings follow <see cref="DriveAttention"/> and the SMART test: a set NVMe critical-warning bit (but the temperature one), media
/// errors and an ATA attribute at its threshold fail; Windows' warning, wear, a hard disk's bad sectors and heat are cautions. An NVMe error-log entry
/// or an unsafe shutdown is history, not a fault (a healthy drive has some), so it is shown and never flagged.
/// </summary>
public sealed record DriveReport(int? HealthPercent, DriveLevel Level, double? TempC, DriveLevel? TempLevel, double TempWatchC, double TempFailC,
    IReadOnlyList<DriveIssue> Issues, IReadOnlyList<DriveParam> Params)
{
    /// <summary>Flash runs hotter than a platter: an SSD is amber from 60 °C and red from 70 °C (the tray's default SSD alert), a hard disk from 50 and 55 °C.</summary>
    public const double SsdWatchC = 60, SsdFailC = 70, HddWatchC = 50, HddFailC = 55;

    /// <summary>The amber and red limits for a drive. The drive's own warning threshold (NVMe WCTEMP, where it starts to throttle) lowers red when it is
    /// lower than ours, never raises it; amber stays at least 5 °C under red.</summary>
    public static (double Watch, double Fail) TempBands(string? media, double? driveWarnC)
    {
        var (watch, fail) = media == "HDD" ? (HddWatchC, HddFailC) : (SsdWatchC, SsdFailC);
        if (driveWarnC is > 0 and var w && w < fail) fail = w;
        return (Math.Min(watch, fail - 5), fail);
    }

    public static DriveLevel? TempLevelOf(double? c, double watch, double fail) => c is not { } t ? null : t >= fail ? DriveLevel.Fail : t >= watch ? DriveLevel.Watch : DriveLevel.Good;

    public static DriveReport Of(DriveHealth d)
    {
        var issues = new List<DriveIssue>(); var rows = new List<DriveParam>();
        int? health = DriveAttention.HealthPercent(d.WearPercent);
        double? temp = d.Nvme?.TemperatureC ?? d.TemperatureC;   // the log is read now; Windows' counter can be minutes old
        var (watch, fail) = TempBands(d.Media, d.WarnTempC);
        var tl = TempLevelOf(temp, watch, fail);

        if (d.Status == "Unhealthy") issues.Add(new("WindowsUnhealthy", DriveLevel.Fail));
        else if (d.Status == "Warning") issues.Add(new("WindowsWarning", DriveLevel.Watch));
        if (health < DriveAttention.FailBelow) issues.Add(new("Worn", DriveLevel.Fail, health));
        else if (health < DriveAttention.WatchBelow) issues.Add(new("Wearing", DriveLevel.Watch, health));
        if (d.Nvme is { } l)
        {
            for (int bit = 0; bit < 6; bit++) if ((l.CriticalWarning & (1 << bit)) != 0) issues.Add(new($"NvmeBit{bit}", bit == 1 ? DriveLevel.Watch : DriveLevel.Fail));
            if (l.MediaErrors > 0) issues.Add(new("MediaErrors", DriveLevel.Fail, l.MediaErrors));
            if (l.CriticalTempMinutes > 0) issues.Add(new("CriticalTempTime", DriveLevel.Watch, l.CriticalTempMinutes));
            Nvme(l, rows, health, temp, tl, d.WarnTempC, d.CritTempC);
        }
        else if (d.BusType == "NVMe" && (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0) is > 0 and var e) issues.Add(new("MediaErrors", DriveLevel.Fail, e));
        foreach (var a in d.Smart ?? []) if (a.Failing) issues.Add(new("AttrFailing", DriveLevel.Fail, a.Id));
        if (d.Media == "HDD" && d.BadSectors > 0) issues.Add(new("BadSectors", DriveLevel.Watch, d.BadSectors));
        if (tl is DriveLevel.Fail) issues.Add(new("Hot", DriveLevel.Fail, Math.Round(temp!.Value)));
        else if (tl is DriveLevel.Watch) issues.Add(new("Warm", DriveLevel.Watch, Math.Round(temp!.Value)));
        if (d.Smart is { Count: > 0 } smart) Ata(smart, rows, d.Media == "HDD");

        var level = issues.Count == 0 ? DriveLevel.Good : issues.Max(i => i.Level);
        return new(health, level, temp, tl, watch, fail, [.. issues.OrderByDescending(i => i.Level)], rows);
    }

    private static void Nvme(NvmeHealthLog l, List<DriveParam> rows, int? health, double? temp, DriveLevel? tl, double? warnC, double? critC)
    {
        DriveLevel bits = l.CriticalWarning == 0 ? DriveLevel.Good : (l.CriticalWarning & ~2) == 0 ? DriveLevel.Watch : DriveLevel.Fail;
        rows.Add(new(null, "DriveNvme_CriticalWarning", $"0x{l.CriticalWarning:X2}", bits, bits == DriveLevel.Good ? null : "DriveWhy_CriticalWarning"));
        if (temp is { } t) rows.Add(new(null, "DriveNvme_Temperature", C(t), tl ?? DriveLevel.Good, tl is DriveLevel.Watch or DriveLevel.Fail ? "DriveWhy_Temp" : null));
        bool spareLow = l.AvailableSparePercent < l.SpareThresholdPercent;
        rows.Add(new(null, "DriveNvme_Spare", $"{l.AvailableSparePercent} %", spareLow ? DriveLevel.Fail : DriveLevel.Good, spareLow ? "DriveWhy_Spare" : null));
        rows.Add(new(null, "DriveNvme_SpareThreshold", $"{l.SpareThresholdPercent} %"));
        var used = health < DriveAttention.FailBelow ? DriveLevel.Fail : health < DriveAttention.WatchBelow ? DriveLevel.Watch : DriveLevel.Good;
        rows.Add(new(null, "DriveNvme_Used", $"{l.PercentageUsed} %", used, used == DriveLevel.Good ? null : "DriveWhy_Used"));
        rows.Add(new(null, "DriveNvme_DataRead", Bytes(l.BytesRead)));
        rows.Add(new(null, "DriveNvme_DataWritten", Bytes(l.BytesWritten)));
        rows.Add(new(null, "DriveNvme_HostReads", N(l.HostReadCommands)));
        rows.Add(new(null, "DriveNvme_HostWrites", N(l.HostWriteCommands)));
        rows.Add(new(null, "DriveNvme_BusyTime", $"{N(l.ControllerBusyMinutes)} min"));
        rows.Add(new(null, "DriveNvme_PowerCycles", N(l.PowerCycles)));
        rows.Add(new(null, "DriveNvme_PowerOnHours", $"{N(l.PowerOnHours)} h"));
        rows.Add(new(null, "DriveNvme_UnsafeShutdowns", N(l.UnsafeShutdowns)));
        rows.Add(new(null, "DriveNvme_MediaErrors", N(l.MediaErrors), l.MediaErrors > 0 ? DriveLevel.Fail : DriveLevel.Good, l.MediaErrors > 0 ? "DriveWhy_MediaErrors" : null));
        rows.Add(new(null, "DriveNvme_ErrorLog", N(l.ErrorLogEntries)));
        rows.Add(new(null, "DriveNvme_WarnTempTime", $"{N(l.WarningTempMinutes)} min", l.WarningTempMinutes > 0 ? DriveLevel.Watch : DriveLevel.Good, l.WarningTempMinutes > 0 ? "DriveWhy_TempTime" : null));
        rows.Add(new(null, "DriveNvme_CritTempTime", $"{N(l.CriticalTempMinutes)} min", l.CriticalTempMinutes > 0 ? DriveLevel.Watch : DriveLevel.Good, l.CriticalTempMinutes > 0 ? "DriveWhy_TempTime" : null));
        rows.Add(new(null, "DriveNvme_Tmt1Count", N(l.Tmt1Count)));
        rows.Add(new(null, "DriveNvme_Tmt2Count", N(l.Tmt2Count)));
        rows.Add(new(null, "DriveNvme_Tmt1Time", $"{N(l.Tmt1Seconds)} s"));
        rows.Add(new(null, "DriveNvme_Tmt2Time", $"{N(l.Tmt2Seconds)} s"));
        if (warnC is { } w) rows.Add(new(null, "DriveNvme_WarnThreshold", C(w)));
        if (critC is { } c) rows.Add(new(null, "DriveNvme_CritThreshold", C(c)));
    }

    /// <summary>The ATA attributes named by their standard meaning; any other id is the maker's own (the UI says "vendor specific"), never given a guessed name.</summary>
    public static readonly IReadOnlySet<int> NamedAttributes = new HashSet<int> { 1, 3, 4, 5, 7, 9, 10, 12, 187, 188, 190, 192, 193, 194, 196, 197, 198, 199 };

    private static void Ata(IReadOnlyList<SmartAttribute> smart, List<DriveParam> rows, bool hdd)
    {
        foreach (var a in smart.OrderBy(a => a.Id))
        {
            long count = (long)(a.Raw & 0xFFFF);
            string value = a.Id is 190 or 194 ? $"{a.Raw & 0xFF} °C" : a.Raw.ToString(CultureInfo.InvariantCulture);
            var (level, why) = a.Failing ? (DriveLevel.Fail, "DriveWhy_AtaThreshold")
                : hdd && a.Id is 5 or 196 or 197 or 198 && count > 0 ? (DriveLevel.Watch, "DriveWhy_Sectors")
                : a.Id == 199 && count > 0 ? (DriveLevel.Watch, "DriveWhy_Crc") : (DriveLevel.Good, (string?)null);
            rows.Add(new(a.Id.ToString("X2", CultureInfo.InvariantCulture), NamedAttributes.Contains(a.Id) ? $"DriveAttr_{a.Id}" : "DriveAttr_Vendor", value, level, why, a.Current, a.Worst, a.Threshold));
        }
    }

    private static string N(ulong v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string C(double c) => $"{c.ToString("F0", CultureInfo.InvariantCulture)} °C";
    private static string Bytes(double b) => b >= 1e12 ? $"{(b / 1e12).ToString("0.##", CultureInfo.InvariantCulture)} TB" : $"{(b / 1e9).ToString("0.#", CultureInfo.InvariantCulture)} GB";
}
