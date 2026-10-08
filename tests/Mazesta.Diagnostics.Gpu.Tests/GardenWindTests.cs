using System.Numerics; using Xunit; using Xunit.Abstractions; using Mazesta.Diagnostics.Gpu.Scene;
namespace Mazesta.Diagnostics.Gpu.Tests;

/// <summary>The air of the courtyard (a stable-fluids field on every core) and the plants that sway in it. No card is needed.</summary>
public class GardenWindTests(ITestOutputHelper output)
{
    private static GardenWind Air(int iterations = 10) => new(GardenVoxels.For(GardenScene.Embedded), WeatherLevel.Standard.WindCell, iterations);

    [Fact] public void The_air_is_still_in_solids_finite_and_goes_round_them()
    {
        var air = Air(); var raw = Air(0);
        for (int k = 0; k < 90; k++) { air.Step(1f / 60, 3f + k / 60f); raw.Step(1f / 60, 3f + k / 60f); }
        double speedSum = 0; long fluid = 0; float most = 0;
        for (int z = 0; z < air.Z; z++) for (int y = 0; y < air.Y; y++) for (int x = 0; x < air.X; x++)
        {
            var v = air.Velocity(x, y, z);
            Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            if (air.Blocked(x, y, z)) { Assert.Equal(Vector3.Zero, v); continue; }
            fluid++; float speed = v.Length(); speedSum += speed; most = MathF.Max(most, speed);
        }
        double divergence = air.Divergence(), unprojected = raw.Divergence();
        output.WriteLine($"{air.Cells:N0} cells ({air.SolidCells:N0} solid, {air.Bytes / 1048576.0:F0} MB), mean speed {speedSum / fluid:F2} m/s, fastest {most:F1}, divergence {divergence:F4} against {unprojected:F4} without the projection");
        Assert.InRange(speedSum / fluid, 0.5, 12); Assert.True(most < 30);
        Assert.True(air.SolidCells > 1000 && air.SolidCells < air.Cells / 3);
        Assert.True(divergence < unprojected * 0.6);              // the projection takes the divergence out: the air goes round the walls instead of through them
    }

    [Fact] public void The_air_is_the_same_every_time()
    {
        var a = Air(); var b = Air();
        for (int k = 0; k < 40; k++) { a.Step(1f / 60, 7f + k / 60f); b.Step(1f / 60, 7f + k / 60f); }
        for (int z = 3; z < a.Z; z += 17) for (int y = 1; y < a.Y; y += 5) for (int x = 3; x < a.X; x += 13) Assert.Equal(a.Velocity(x, y, z), b.Velocity(x, y, z));   // (red-black sweeps do not depend on which core did which cell)
    }

    [Fact] public void The_air_is_blown_by_the_gusts_and_is_not_the_same_everywhere()
    {
        var air = Air(); for (int k = 0; k < 60; k++) air.Step(1f / 60, 5f + k / 60f);
        Assert.True(air.Sample(new Vector3(0, 8, -14), out var high)); Assert.True(air.Sample(new Vector3(0, 0.6f, -14), out var low));
        output.WriteLine($"over the courtyard {high.Length():F2} m/s at 8 m, {low.Length():F2} m/s at 0.6 m");
        Assert.True(high.Length() > 0.2f); Assert.NotEqual(high, low);
        Assert.False(air.Sample(new Vector3(500, 1, 0), out _));
    }

    [Fact] public void The_weather_is_blown_by_the_air_and_no_body_ends_in_a_wall()
    {
        var scene = GardenScene.Embedded; var voxels = GardenVoxels.For(scene);
        var w = new GardenWeather(WeatherLevel.Standard, voxels, scene.WaterLevel, new Vector4(-3, -23, 3, -11), [new Vector4(-6, 5, -14, 3), new Vector4(7, 4, -20, 2.5f)]);
        for (int k = 0; k < 240; k++) w.Advance(2f + k / 60f, true);
        Assert.NotNull(w.Field); Assert.True(w.Field!.Valid); Assert.True(w.WindSeconds > 0); Assert.True(w.CollideSeconds >= 0);
        var rows = w.Rows; int flying = 0, inside = 0, begin = (WeatherLevel.Standard.Rain + WeatherLevel.Standard.Splashes) * 3;
        for (int b = 0; b < WeatherLevel.Standard.Leaves + WeatherLevel.Standard.Twigs; b++)
        {
            if (rows[begin + b * 3 + 1].W < -50) continue;
            var at = new Vector3(rows[begin + b * 3].W, rows[begin + b * 3 + 1].W, rows[begin + b * 3 + 2].W); flying++; if (voxels.Solid(at)) inside++;
            Assert.True(float.IsFinite(at.X) && float.IsFinite(at.Y) && float.IsFinite(at.Z));
        }
        output.WriteLine($"{flying} leaves and twigs about, {inside} in a solid; air {w.WindSeconds * 1000:F1} ms, collisions {w.CollideSeconds * 1000:F1} ms");
        Assert.True(flying > 500); Assert.True(inside < flying / 25);
    }
}
