using Mazesta.Core.Hardware; using Mazesta.Core.Providers; using static Mazesta.Core.Health.Checkup.Measures;
namespace Mazesta.Core.Health.Checkup;

/// <summary>
/// Each drive's health as the drive itself reports it: Windows' SMART verdict and NVMe media errors (<see cref="DriveAttention"/>, the same rule as
/// the SMART test and the tray), then the life its wear counter leaves. A drive that reports neither a verdict nor a wear counter says nothing.
/// </summary>
public static class DriveCheck
{
    /// <summary>Life left at or below these is worth planning a replacement for, and then urgent (makers rate the drive's endurance by it).</summary>
    public const int LifeAttention = 30, LifeProblem = 10;

    public static IReadOnlyList<Finding> Evaluate(IEnumerable<DriveHealth> drives) => [.. drives.Select(One).OfType<Finding>()];

    private static Finding? One(DriveHealth d)
    {
        int? life = DriveAttention.HealthPercent(d.WearPercent);
        var m = new List<Measure>();
        if (life is { } l) m.Add(M("Check_M_DriveLife", l, Percent));
        if (d.PowerOnHours is { } h) m.Add(M("Check_M_PowerOnHours", h, Hours));
        if (d.TemperatureMaxC is { } t) m.Add(M("Check_M_DriveTempMax", t, Celsius));
        long errors = (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0);
        if (d.BusType == "NVMe" && errors > 0) m.Add(M("Check_M_MediaErrors", errors, None));
        Finding F(FindingCode c, FindingLevel lv) => new(c, lv, HardwareKind.Storage, m, d.Name);
        if (d.Status == "Unhealthy") return F(FindingCode.DriveUnhealthy, FindingLevel.Problem);
        if (DriveAttention.Needs(d)) return F(FindingCode.DriveUnhealthy, FindingLevel.Attention);
        if (life <= LifeProblem) return F(FindingCode.DriveWorn, FindingLevel.Problem);
        if (life <= LifeAttention) return F(FindingCode.DriveWorn, FindingLevel.Attention);
        return d.Status == "Healthy" || life is not null ? F(FindingCode.DriveHealthy, FindingLevel.Good) : null;
    }
}
