namespace Mazesta.Core.Fans;

/// <summary>What duty the graphics card's fans are to be held at for the temperatures read now: the user's curve on the chosen temperature, full speed when the core or the hot spot
/// is at its guard, never outside what the card takes, and slowing down only a few percent a time so a fan does not hunt with the temperature.</summary>
public static class GpuFanPlan
{
    public const double CoreGuard = 85, HotGuard = 100; public const int StepDown = 3;

    /// <param name="source">"core" or "hot": the temperature the curve is read at.</param>
    /// <param name="last">The duty held now, if any.</param>
    /// <returns>Null when there is nothing to go by: the chosen temperature is not read and neither guard is reached.</returns>
    public static int? Duty(IReadOnlyList<FanPoint> points, string source, double? core, double? hot, int min, int max, int? last)
    {
        double? temp = source == "hot" ? hot : core; bool guarded = core >= CoreGuard || hot >= HotGuard;
        if (temp is null && !guarded) return null;
        int target = (int)Math.Clamp(Math.Round(guarded ? 100 : FanCurve.Evaluate(points, temp!.Value)), min, max);
        return last is { } l && target < l ? Math.Max(target, l - StepDown) : target;
    }
}
