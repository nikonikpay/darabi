namespace Mazesta.Core.Fans;

/// <summary>One point of a fan curve: at this temperature (°C) the fan runs at this duty (%).</summary>
public readonly record struct FanPoint(double Temp, double Percent);

/// <summary>A fan curve as the boards' own fan programs draw it: points of temperature against duty, joined by straight lines, flat before the first and after the last.</summary>
public static class FanCurve
{
    public const int MinPoints = 2, MaxPoints = 8; public const double MinTemp = 20, MaxTemp = 110;

    public static double Evaluate(IReadOnlyList<FanPoint> points, double temp)
    {
        if (points.Count == 0) return 100;
        if (temp <= points[0].Temp) return points[0].Percent;
        for (int i = 1; i < points.Count; i++)
        {
            if (temp > points[i].Temp) continue;
            var (a, b) = (points[i - 1], points[i]);
            return b.Temp <= a.Temp ? b.Percent : a.Percent + (b.Percent - a.Percent) * (temp - a.Temp) / (b.Temp - a.Temp);
        }
        return points[^1].Percent;
    }

    /// <summary>The curve cleaned up: temperatures in range and in order, duties within 0 to 100, no more than <see cref="MaxPoints"/>. Null if too few points are left to be a curve.</summary>
    public static IReadOnlyList<FanPoint>? Clean(IEnumerable<FanPoint> points)
    {
        var list = points.Select(p => new FanPoint(Math.Round(Math.Clamp(p.Temp, MinTemp, MaxTemp)), Math.Round(Math.Clamp(p.Percent, 0, 100)))).OrderBy(p => p.Temp).ToList();
        var cleaned = new List<FanPoint>();
        foreach (var p in list) { if (cleaned.Count > 0 && cleaned[^1].Temp == p.Temp) cleaned[^1] = p; else cleaned.Add(p); }
        // A hotter point never runs slower than a cooler one: a curve that dips would let a fan slow down as the part gets hotter.
        for (int i = 1; i < cleaned.Count; i++) if (cleaned[i].Percent < cleaned[i - 1].Percent) cleaned[i] = cleaned[i] with { Percent = cleaned[i - 1].Percent };
        return cleaned.Count is >= MinPoints and <= MaxPoints ? cleaned : null;
    }

    /// <summary>The ready-made curves, by name; "full" is a constant 100 %.</summary>
    public static IReadOnlyList<FanPoint>? Preset(string name) => name switch
    {
        "silent" => [new(30, 20), new(50, 25), new(65, 40), new(75, 65), new(85, 100)],
        "standard" => [new(30, 30), new(50, 40), new(65, 60), new(75, 80), new(85, 100)],
        "performance" => [new(30, 45), new(45, 60), new(60, 80), new(70, 100)],
        "full" => [new(20, 100), new(100, 100)],
        _ => null,
    };
}
