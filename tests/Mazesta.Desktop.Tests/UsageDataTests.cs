using Mazesta.Core.Tuning; using Mazesta.Desktop.Services; using Xunit;
namespace Mazesta.Desktop.Tests;

public sealed class UsageDataTests
{
    [Fact] public void A_figure_that_was_not_measured_is_left_out_not_written_as_zero()
    {
        var m = UsageData.Measurement(new LoadMeasurement(1665, null, 71.234, null, 100, 0, false))!;
        Assert.Equal(1665, (double)m["clockMHz"]!); Assert.Equal(71.23, (double)m["tempC"]!);
        Assert.Null(m["powerW"]); Assert.Null(m["volts"]); Assert.Null(m["peakClockMHz"]);
        Assert.Null(UsageData.Measurement(null));
    }

    [Fact] public void An_automatic_search_is_kept_with_why_it_failed_and_the_stock_and_tuned_figures()
    {
        var stock = new LoadMeasurement(1665, 348, 82, 83, 10600, 0, false, 1965, 350, 0.859);
        var tuned = new LoadMeasurement(1665, 266, 75, 75, 10565, 0, false);
        var o = UsageData.Auto("Undervolt", "RTX 3090", new(AutoTuneVerdict.NoImprovement, "Tuning_Out_ConfirmFailed", null, stock, tuned, Detail: "x"));
        Assert.Equal("NoImprovement", (string)o["verdict"]!); Assert.Equal("Tuning_Out_ConfirmFailed", (string)o["reason"]!);
        Assert.Equal(348, (double)o["stock"]!["powerW"]!); Assert.Equal(266, (double)o["tuned"]!["powerW"]!); Assert.Equal(0.859, (double)o["stock"]!["volts"]!);
        Assert.Null(o["settings"]); Assert.Null(o["sceneStock"]);
    }

    [Fact] public void No_name_path_or_text_is_among_the_keys_of_a_scene_event()
    {
        var o = UsageData.Scene("RTX 3090", new(135, 0, 1965, null, null), new LoadMeasurement(1960, 300, 70, 71, 118.5, 0, false), null);
        Assert.Equal(["gpu", "settings", "clean", "result"], o.Select(kv => kv.Key));
    }
}
