using Mazesta.Core.Hardware; using Xunit;
namespace Mazesta.Core.Tests;
public class SensorGroupingTests
{
    private static readonly HardwareId Hw = new("cpu/x");
    private static SensorDefinition S(string name, SensorKind kind, int i) => new(SensorId.Create(Hw, $"{kind}/{i}".ToLowerInvariant()), Hw, name, kind, Units.ForKind(kind), SensorRole.None, i);
    private static IReadOnlyDictionary<string, SensorSection?> Classify(params SensorDefinition[] s) => SensorGrouping.Classify(s).ToDictionary(kv => s.Single(x => x.Id == kv.Key).Name + "|" + s.Single(x => x.Id == kv.Key).Kind, kv => kv.Value);

    [Theory]
    [InlineData("Core #3", "Core")]
    [InlineData("Core #3 (Effective)", "Core Effective")]
    [InlineData("Core #12 VID", "Core VID")]
    [InlineData("CCD1 (Tdie)", "CCD Tdie")]
    [InlineData("CPU Core #17", "CPU Core")]
    [InlineData("+3.3V", "+3.3V")]
    [InlineData("Temperature #4", "")]     // only the kind's own word: generic bucket
    [InlineData("Fan #2", "")]
    public void Family_strips_the_instance_number_but_nothing_else(string name, string family)
        => Assert.Equal(family, SensorGrouping.Family(S(name, SensorKind.Clock, 0)));

    [Fact] public void Numbered_instances_become_one_section_and_loose_sensors_of_the_kind_share_the_bucket()
    {
        var r = Classify(S("Bus Speed", SensorKind.Clock, 0), S("Cores (Average)", SensorKind.Clock, 1), S("Core #1", SensorKind.Clock, 2), S("Core #2", SensorKind.Clock, 3), S("Core #3", SensorKind.Clock, 6), S("Core #3 (Effective)", SensorKind.Clock, 7),
                         S("Core #1 (Effective)", SensorKind.Clock, 4), S("Core #2 (Effective)", SensorKind.Clock, 5));
        Assert.Equal(new SensorSection(SensorKind.Clock, "Core"), r["Core #1|Clock"]);
        Assert.Equal(new SensorSection(SensorKind.Clock, "Core Effective"), r["Core #2 (Effective)|Clock"]);
        Assert.Equal(new SensorSection(SensorKind.Clock, ""), r["Bus Speed|Clock"]);     // two loose clocks -> "Clocks"
        Assert.Equal(new SensorSection(SensorKind.Clock, ""), r["Cores (Average)|Clock"]);
    }
    [Fact] public void A_sensor_with_nothing_to_group_with_stays_flat()
    {
        var r = Classify(S("CPU Total", SensorKind.Load, 0), S("Core (Tctl/Tdie)", SensorKind.Temperature, 1));
        Assert.Null(r["CPU Total|Load"]); Assert.Null(r["Core (Tctl/Tdie)|Temperature"]);
    }
    [Fact] public void Named_fans_join_the_generic_fans_bucket_with_the_numbered_ones()
    {
        var r = Classify(S("CPU Fan", SensorKind.Fan, 0), S("CPU_OPT Fan", SensorKind.Fan, 1), S("Fan #1", SensorKind.Fan, 2), S("Fan #3", SensorKind.Fan, 3));
        Assert.All(r.Values, v => Assert.Equal(new SensorSection(SensorKind.Fan, ""), v));
    }
    [Fact] public void Different_kinds_never_share_a_section()
    {
        var r = Classify(S("Fan #1", SensorKind.Fan, 0), S("Fan #2", SensorKind.Fan, 1), S("Fan #1", SensorKind.Control, 2), S("Fan #2", SensorKind.Control, 3));
        Assert.Equal(SensorKind.Control, r["Fan #1|Control"]!.Value.Kind); Assert.Equal(SensorKind.Fan, r["Fan #1|Fan"]!.Value.Kind);
    }

    [Fact] public void A_fan_and_its_duty_cycle_control_sit_in_the_generic_fans_and_controls_buckets()
    {
        var r = Classify(S("CPU Fan", SensorKind.Fan, 0), S("Fan #1", SensorKind.Fan, 1), S("CPU Fan Control", SensorKind.Control, 2), S("Fan #1 Control", SensorKind.Control, 3), S("Fan #3 Control", SensorKind.Control, 4));
        Assert.Equal(new SensorSection(SensorKind.Control, ""), r["Fan #1 Control|Control"]);
        Assert.Equal(new SensorSection(SensorKind.Control, ""), r["CPU Fan Control|Control"]);
        Assert.Equal(new SensorSection(SensorKind.Fan, ""), r["CPU Fan|Fan"]);
    }
    [Fact] public void A_family_of_two_is_not_worth_a_header_and_falls_into_the_kind_bucket()
    {
        var r = Classify(S("CCD1 (Tdie)", SensorKind.Temperature, 0), S("CCD2 (Tdie)", SensorKind.Temperature, 1), S("Core (Tctl/Tdie)", SensorKind.Temperature, 2));
        Assert.All(r.Values, v => Assert.Equal(new SensorSection(SensorKind.Temperature, ""), v));
    }
}
