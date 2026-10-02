using Mazesta.Core.Hardware; using Mazesta.Core.Health.Checkup; using Mazesta.Core.Inventory; using Xunit;
namespace Mazesta.Core.Tests;

public class CpuCheckTests
{
    /// <summary>A 60-second run sampled once a second, each quantity from a function of the second.</summary>
    private static Series S(Func<int, double> f, int seconds = 60) => Series.Of(Enumerable.Range(0, seconds).Select(t => ((double)t, f(t))));
    private static CpuRunTrace Run(Func<int, double> clock, Func<int, double>? temp = null, Func<int, double>? power = null, double? tjMax = null, int? baseMhz = null, bool all = true, Func<int, double>? load = null, bool? battery = null)
        => new(all, 60, temp is null ? null : S(temp), S(clock), power is null ? null : S(power), load is null ? null : S(load), tjMax, baseMhz, battery);
    private static Finding One(IReadOnlyList<Finding> f, FindingCode code) => Assert.Single(f, x => x.Code == code);

    [Fact] public void A_clock_held_under_one_gigahertz_is_a_problem_and_nothing_else_is_judged()
    {
        var f = CpuCheck.Evaluate(Run(_ => 798, temp: _ => 60, power: _ => 9, baseMhz: 2400, battery: false));
        var x = Assert.Single(f); Assert.Equal(FindingCode.CpuStuckLowClock, x.Code); Assert.Equal(FindingLevel.Problem, x.Level); Assert.Equal(FindingHint.None, x.Hint);
    }
    [Fact] public void On_battery_the_low_clock_says_so() => Assert.Equal(FindingHint.OnBattery, One(CpuCheck.Evaluate(Run(_ => 700, battery: true)), FindingCode.CpuStuckLowClock).Hint);
    [Fact] public void A_run_the_cpu_did_not_get_to_itself_is_only_noted() => Assert.Equal(FindingCode.CpuNotFullyLoaded, Assert.Single(CpuCheck.Evaluate(Run(_ => 4000, load: _ => 60))).Code);

    // An AMD CPU has no limit of its own to read: a temperature flat at its top while the clock falls is the sign it is holding its ceiling.
    [Fact] public void Amd_held_at_95_with_a_falling_clock_is_a_problem()
    {
        var x = One(CpuCheck.Evaluate(Run(t => 5000 - t * 20, temp: t => Math.Min(95, 70 + t * 5), power: _ => 200)), FindingCode.CpuHeldAtCeiling);
        Assert.Equal(FindingLevel.Problem, x.Level);
    }
    [Fact] public void Amd_held_at_95_with_a_steady_clock_is_by_design()
    {
        var f = CpuCheck.Evaluate(Run(_ => 5000, temp: t => Math.Min(95, 70 + t * 5), power: _ => 200));
        Assert.Equal(FindingLevel.Note, One(f, FindingCode.CpuHotSteady).Level); Assert.DoesNotContain(f, x => x.Level >= FindingLevel.Attention);
    }
    [Fact] public void A_cool_steady_run_without_a_limit_is_fine() => Assert.Equal(FindingLevel.Good, One(CpuCheck.Evaluate(Run(_ => 4200, temp: t => 60 + t % 5, power: _ => 140)), FindingCode.CpuHeatOk).Level);

    [Fact] public void Intel_at_its_tjmax_with_a_falling_clock_is_a_problem()
    {
        var x = One(CpuCheck.Evaluate(Run(t => t < 15 ? 5500 : 4500, temp: t => t < 5 ? 80 : 100, power: _ => 300, tjMax: 100)), FindingCode.CpuAtTjMax);
        Assert.Equal(FindingLevel.Problem, x.Level); Assert.Contains(x.Measures, m => m.Key == "Check_M_TjMax" && m.Value == 100);
    }
    [Fact] public void Touching_tjmax_with_a_steady_clock_is_by_design() => Assert.Equal(FindingLevel.Note, One(CpuCheck.Evaluate(Run(_ => 5500, temp: _ => 99, tjMax: 100)), FindingCode.CpuAtTjMax).Level);

    // Intel's default profile on Core 13th/14th gen: a short burst at the higher limit, then the long one holds the P-cores under 5 GHz while the
    // temperature has room. That is a setting, not a fault.
    [Fact] public void A_power_step_with_room_to_spare_is_a_note_not_a_fault()
    {
        var f = CpuCheck.Evaluate(Run(t => t < 30 ? 5300 : 4700, temp: t => t < 30 ? 88 : 78, power: t => t < 30 ? 253 : 188, tjMax: 100, baseMhz: 3000));
        var step = One(f, FindingCode.CpuPowerStep);
        Assert.Equal(FindingLevel.Note, step.Level); Assert.Equal(253, step.Measures.Single(m => m.Key == "Check_M_PowerShort").Value); Assert.Equal(188, step.Measures.Single(m => m.Key == "Check_M_PowerLong").Value);
        Assert.DoesNotContain(f, x => x.Level >= FindingLevel.Attention);
    }
    [Fact] public void Steady_power_with_room_to_spare_says_the_power_limit_sets_the_clock()
        => Assert.Equal(FindingHint.PowerSteady, One(CpuCheck.Evaluate(Run(_ => 4700, temp: _ => 80, power: _ => 253, tjMax: 100)), FindingCode.CpuHeatOk).Hint);

    [Fact] public void An_all_core_clock_well_under_base_is_flagged()
    {
        Assert.Equal(FindingLevel.Attention, One(CpuCheck.Evaluate(Run(_ => 2000, baseMhz: 2600)), FindingCode.CpuBelowBaseClock).Level);
        Assert.Equal(FindingLevel.Problem, One(CpuCheck.Evaluate(Run(_ => 1500, baseMhz: 2600)), FindingCode.CpuBelowBaseClock).Level);
        Assert.DoesNotContain(CpuCheck.Evaluate(Run(_ => 2500, baseMhz: 2600)), x => x.Code == FindingCode.CpuBelowBaseClock);
        Assert.DoesNotContain(CpuCheck.Evaluate(Run(_ => 2000, baseMhz: 2600, all: false)), x => x.Code == FindingCode.CpuBelowBaseClock);
    }
    [Fact] public void Too_few_samples_judge_nothing() => Assert.Empty(CpuCheck.Evaluate(new(true, 5, null, S(_ => 500, 5), null, null, null, null)));
}

public class GpuCheckTests
{
    private static Series S(Func<int, double> f) => Series.Of(Enumerable.Range(0, 60).Select(t => ((double)t, f(t))));
    private static GpuRunTrace Run(GpuThrottleCounts? r = null, GpuLink? link = null, Func<int, double>? core = null, Func<int, double>? spot = null, HardwareVendor vendor = HardwareVendor.Nvidia)
        => new("RTX", vendor, S(_ => 99), core is null ? null : S(core), spot is null ? null : S(spot), S(_ => 350), r, link);

    [Fact] public void A_power_brake_is_a_problem() => Assert.Contains(GpuCheck.Evaluate(Run(new(60, 0, 0, 10, 0, 10, 88, 350))), x => x.Code == FindingCode.GpuPowerBrake && x.Level == FindingLevel.Problem);
    [Fact] public void A_brief_general_slowdown_is_not_a_fault() => Assert.DoesNotContain(GpuCheck.Evaluate(Run(new(60, 0, 0, 2, 0, 0, 88, 350))), x => x.Code == FindingCode.GpuHwSlowdown);
    [Fact] public void Running_at_the_power_limit_is_only_noted()
    {
        var f = GpuCheck.Evaluate(Run(new(60, 55, 0, 0, 0, 0, 88, 350), core: _ => 70));
        Assert.Equal(FindingLevel.Good, Assert.Single(f, x => x.Code == FindingCode.GpuPowerLimited).Level);   // the card working to its own limit is how it is built: a check that held
        Assert.Contains(f, x => x.Code == FindingCode.GpuHeatOk);
    }
    [Fact] public void Reaching_the_temperature_target_needs_attention() => Assert.Equal(FindingLevel.Attention, Assert.Single(GpuCheck.Evaluate(Run(new(60, 0, 30, 0, 0, 0, 83, 350), core: _ => 83)), x => x.Code == FindingCode.GpuThermalSlowdown).Level);
    [Fact] public void A_wide_hotspot_gap_is_flagged_with_a_looser_line_for_amd()
    {
        Assert.Contains(GpuCheck.Evaluate(Run(core: _ => 70, spot: _ => 97)), x => x.Code == FindingCode.GpuHotspotGap);
        Assert.DoesNotContain(GpuCheck.Evaluate(Run(core: _ => 70, spot: _ => 97, vendor: HardwareVendor.Amd)), x => x.Code == FindingCode.GpuHotspotGap);
        Assert.DoesNotContain(GpuCheck.Evaluate(Run(core: _ => 70, spot: _ => 85)), x => x.Code == FindingCode.GpuHotspotGap);
    }
    [Fact] public void A_link_narrower_than_card_and_slot_allow_needs_attention() => Assert.Contains(GpuCheck.Evaluate(Run(link: new(4, 8, 4, 16, 4, 16, true))), x => x.Code == FindingCode.GpuLinkNarrow);
    [Fact] public void A_narrow_link_with_the_slot_unknown_is_a_note() => Assert.Equal(FindingLevel.Note, Assert.Single(GpuCheck.Evaluate(Run(link: new(1, 8, 3, 16, null, null, false))), x => x.Code == FindingCode.GpuLinkBelowCard).Level);
    [Fact] public void A_narrow_slot_is_a_note() => Assert.Equal(FindingLevel.Note, Assert.Single(GpuCheck.Evaluate(Run(link: new(4, 8, 4, 16, 4, 8, true))), x => x.Code == FindingCode.GpuSlotNarrow).Level);
    [Fact] public void A_generation_read_at_rest_is_not_judged() => Assert.Contains(GpuCheck.Evaluate(Run(link: new(1, 16, 4, 16, 4, 16, false))), x => x.Code == FindingCode.GpuLinkOk);
    [Fact] public void A_slow_generation_under_load_needs_attention() => Assert.Contains(GpuCheck.Evaluate(Run(link: new(3, 16, 4, 16, 4, 16, true))), x => x.Code == FindingCode.GpuLinkSlowGen);
}

public class MemoryCheckTests
{
    private static MemoryModuleInfo Dimm(string slot, int mts, string part = "F4-3600C16-8GVKC", int type = 26) => new(slot, 8L << 30, "G.Skill", part, mts, mts, SmbiosType: type);
    private static SpdModule Spd(int slot, int? xmp) => new(slot, "DDR4", null, null, null, 8, 1, 8, 8, 4, false, null,
        [new("JEDEC", 2666, 19, 19, 19, 43, 61, 1.2)], xmp is { } x ? [new("XMP 1", x, 16, 19, 19, 39, 58, 1.35)] : [], xmp is null ? null : "2.0");

    [Fact] public void Xmp_left_off_needs_attention()
    {
        var x = Assert.Single(MemoryCheck.Evaluate([Dimm("DIMM_A2", 2666), Dimm("DIMM_B2", 2666)], [Spd(1, 3600), Spd(3, 3600)]), f => f.Code == FindingCode.RamXmpOff);
        Assert.Equal(3600, x.Measures.Single(m => m.Key == "Check_M_RamXmp").Value);
    }
    [Fact] public void Xmp_on_is_good() => Assert.Contains(MemoryCheck.Evaluate([Dimm("DIMM_A2", 3600), Dimm("DIMM_B2", 3600)], [Spd(1, 3600), Spd(3, 3600)]), f => f.Code == FindingCode.RamXmpOn);
    [Fact] public void Ddr5_rating_is_said_to_be_unread() => Assert.Contains(MemoryCheck.Evaluate([Dimm("DIMM_A2", 4800, type: 34)], []), f => f.Code == FindingCode.RamProfileUnread);
    [Fact] public void One_module_runs_in_one_channel() => Assert.Contains(MemoryCheck.Evaluate([Dimm("DIMM_A2", 3200)], []), f => f.Code == FindingCode.RamSingleChannel);
    [Fact] public void Two_modules_in_one_channel_need_attention()
    {
        Assert.Contains(MemoryCheck.Evaluate([Dimm("DIMM_A1", 3200), Dimm("DIMM_A2", 3200)], []), f => f.Code == FindingCode.RamSameChannel);
        Assert.DoesNotContain(MemoryCheck.Evaluate([Dimm("DIMM_A2", 3200), Dimm("DIMM_B2", 3200)], []), f => f.Code == FindingCode.RamSameChannel);
        Assert.DoesNotContain(MemoryCheck.Evaluate([Dimm("DIMM 0", 3200), Dimm("DIMM 1", 3200)], []), f => f.Code == FindingCode.RamSameChannel);
    }
    [Theory, InlineData("DIMM_A1", 'A'), InlineData("DIMMB2", 'B'), InlineData("DDR4_B1", 'B'), InlineData("ChannelA-DIMM0", 'A'), InlineData("P0 CHANNEL B", 'B'), InlineData("Controller1-ChannelB-DIMM0", 'B')]
    public void Channel_letters_are_read_from_common_slot_names(string slot, char channel) => Assert.Equal(channel, MemoryCheck.Channel(slot));
    [Theory, InlineData("DIMM 0"), InlineData("BANK 0"), InlineData("Bottom-Slot 1(left)")] public void Unknown_slot_names_give_no_channel(string slot) => Assert.Null(MemoryCheck.Channel(slot));
    [Fact] public void Mixed_modules_are_noted() => Assert.Contains(MemoryCheck.Evaluate([Dimm("DIMM_A2", 3200), Dimm("DIMM_B2", 3200, "CMK16GX4M2B3200C16")], []), f => f.Code == FindingCode.RamMixed);
}

public class PlatformAndPeerCheckTests
{
    [Fact] public void A_power_plan_capped_under_100_percent_needs_attention() => Assert.Equal(FindingLevel.Attention, Assert.Single(PlatformCheck.Power(new(true, false, 99, 2))).Level);
    [Fact] public void Boost_turned_off_in_the_plan_needs_attention() => Assert.Contains(PlatformCheck.Power(new(true, false, 100, 0)), f => f.Code == FindingCode.PowerPlanNoBoost);
    [Fact] public void A_plain_plan_is_good() => Assert.Equal(FindingCode.PowerPlanOk, Assert.Single(PlatformCheck.Power(new(true, true, 100, 2))).Code);
    [Fact] public void Running_on_battery_is_noted() => Assert.Contains(PlatformCheck.Power(new(false, true, 100, 2)), f => f.Code == FindingCode.PowerOnBattery);
    [Fact] public void A_drive_at_x2_of_its_x4_needs_attention() => Assert.Equal(FindingCode.DriveLinkNarrow, Assert.Single(PlatformCheck.Drives([new("SSD", 4, 2, 4, 4, 4, 4)])).Code);
    [Fact] public void A_narrow_drive_link_with_the_slot_unknown_is_a_note() => Assert.Equal(FindingCode.DriveLinkBelowDrive, Assert.Single(PlatformCheck.Drives([new("SSD", 3, 2, 3, 4, null, null)])).Code);
    [Fact] public void A_gen4_drive_in_a_gen3_slot_is_a_note() => Assert.Equal(FindingCode.DriveSlotLimited, Assert.Single(PlatformCheck.Drives([new("SSD", 3, 4, 4, 4, 3, 4)])).Code);

    private static PeerStanding Standing(double mine, int systems = 5, RunConditions? ours = null, RunConditions? theirs = null)
        => new("CPU multi", HardwareKind.Cpu, mine, 100, systems, true, "GFLOPS", ours ?? new(null, null, null), theirs);
    [Fact] public void Few_systems_are_too_few_to_judge() => Assert.Equal(FindingCode.BenchFewPeers, PeerCheck.Evaluate(Standing(50, systems: 2)).Code);
    [Fact] public void Within_ten_percent_is_normal() => Assert.Equal(FindingLevel.Good, PeerCheck.Evaluate(Standing(91)).Level);
    [Fact] public void Well_below_is_a_problem() => Assert.Equal(FindingLevel.Problem, PeerCheck.Evaluate(Standing(70)).Level);
    [Fact] public void Lower_but_at_less_power_without_more_heat_is_a_setting()
    {
        var f = PeerCheck.Evaluate(Standing(85, ours: new(188, 78, 4700), theirs: new(253, 92, 5300)));
        Assert.Equal(FindingHint.LessPowerThanPeers, f.Hint); Assert.Equal(FindingLevel.Note, f.Level);
    }
    [Fact] public void Lower_and_hotter_points_to_cooling()
    {
        var f = PeerCheck.Evaluate(Standing(75, ours: new(200, 100, 4300), theirs: new(210, 85, 5000)));
        Assert.Equal(FindingHint.HotterThanPeers, f.Hint); Assert.Equal(FindingLevel.Problem, f.Level);
    }
    [Fact] public void Lower_is_better_benchmarks_are_signed_the_right_way() => Assert.Equal(FindingLevel.Good, PeerCheck.Evaluate(new("lat", HardwareKind.Memory, 95, 100, 5, false, "ns", new(null, null, null), null)).Level);
}

public class CpuSpecTests
{
    private static Series S(Func<int, double> f) => Series.Of(Enumerable.Range(0, 60).Select(t => ((double)t, f(t))));
    private static readonly CpuSpec Ryzen = new("RYZEN 9 7950X", HardwareVendor.Amd, "Desktop", 16, 32, 4500, 5700, 170, null, 95, "https://www.amd.com/x");
    private static readonly CpuSpec Raptor = new("I9-13900K", HardwareVendor.Intel, "Desktop", 24, 32, 3000, 5400, 125, 253, 100, "https://www.intel.com/x");
    private static CpuRunTrace Run(CpuSpec spec, Func<int, double> clock, Func<int, double>? temp = null, Func<int, double>? power = null, bool all = true)
        => new(all, 60, temp is null ? null : S(temp), S(clock), power is null ? null : S(power), null, null, null, null, spec);

    [Theory]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700KF", "I7-13700KF")]
    [InlineData("Intel(R) Core(TM) i9-10900K CPU @ 3.70GHz", "I9-10900K")]
    [InlineData("Intel(R) Core(TM) Ultra 7 265K", "ULTRA 7 265K")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", "ULTRA 7 155H")]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", "RYZEN 9 7950X")]
    [InlineData("AMD Ryzen 5 PRO 3400G with Radeon Vega Graphics", "RYZEN 5 PRO 3400G")]
    [InlineData("AMD Ryzen 7 5800X3D 8-Core Processor", "RYZEN 7 5800X3D")]
    public void The_model_number_is_read_from_the_name_windows_gives(string name, string key) => Assert.Equal(key, CpuSpecs.Key(name));
    [Fact] public void A_name_without_a_model_number_finds_nothing() { Assert.Null(CpuSpecs.Key("Intel(R) Pentium(R) CPU G4560")); Assert.Null(CpuSpecs.Find(null)); }

    [Fact] public void The_table_holds_the_published_figures()
    {
        Assert.True(CpuSpecs.Count > 100);
        var i9 = CpuSpecs.Find("13th Gen Intel(R) Core(TM) i9-13900K")!;
        Assert.Equal((3000, 5400, 125, 253, 100), (i9.BaseMhz, i9.BoostMhz, i9.BasePowerW, i9.TurboPowerW, i9.TjMaxC)); Assert.StartsWith("https://www.intel.com/", i9.Source);
        var r9 = CpuSpecs.Find("AMD Ryzen 9 7950X 16-Core Processor")!;
        Assert.Equal((4500, 5700, 170, 95), (r9.BaseMhz, r9.BoostMhz, r9.BasePowerW, r9.TjMaxC)); Assert.Equal(90, CpuSpecs.Find("AMD Ryzen 7 5800X 8-Core Processor")!.TjMaxC);
    }

    // AMD reports no limit of its own; its published Tjmax is used, and holding it without losing clock is how these processors are built to run.
    [Fact] public void Amd_at_its_published_tjmax_with_a_steady_clock_is_a_note_with_its_source()
    {
        var x = Assert.Single(CpuCheck.Evaluate(Run(Ryzen, _ => 5100, temp: t => Math.Min(95, 70 + t * 5))), f => f.Code == FindingCode.CpuAtTjMax);
        Assert.Equal(FindingLevel.Note, x.Level); Assert.Equal(Ryzen.Source, x.Source); Assert.Contains(x.Measures, m => m.Key == "Check_M_TjMaxSpec" && m.Value == 95);
    }
    [Fact] public void A_single_thread_well_under_the_published_boost_needs_attention()
    {
        var x = Assert.Single(CpuCheck.Evaluate(Run(Ryzen, _ => 4400, all: false)), f => f.Code == FindingCode.CpuBelowBoost);
        Assert.Equal(FindingLevel.Attention, x.Level);
        Assert.DoesNotContain(CpuCheck.Evaluate(Run(Ryzen, _ => 5550, all: false)), f => f.Code == FindingCode.CpuBelowBoost);
    }
    [Theory]
    [InlineData(125, FindingHint.PowerAtBaseSpec, FindingLevel.Note)]
    [InlineData(253, FindingHint.PowerAtTurboSpec, FindingLevel.Note)]
    [InlineData(320, FindingHint.PowerAboveSpec, FindingLevel.Attention)]
    public void The_power_an_intel_run_settles_at_names_the_setting(double watts, FindingHint hint, FindingLevel level)
    {
        var x = Assert.Single(CpuCheck.Evaluate(Run(Raptor, _ => 4900, temp: _ => 80, power: _ => watts)), f => f.Code == FindingCode.CpuPowerLimit);
        Assert.Equal((hint, level), (x.Hint, x.Level));
    }
    [Fact] public void Amd_power_is_not_judged_against_its_tdp() => Assert.DoesNotContain(CpuCheck.Evaluate(Run(Ryzen, _ => 5000, temp: _ => 80, power: _ => 230)), f => f.Code == FindingCode.CpuPowerLimit);
    // The owner's RTX 3090 after a day's use: 52 receiver errors and 2 bad TLPs since start-up, as HWiNFO showed them, is a clean link.
    [Theory, InlineData(54, null, FindingCode.GpuPcieErrorsOk, FindingLevel.Good), InlineData(1500, 0.0, FindingCode.GpuPcieErrorsMany, FindingLevel.Attention),
        InlineData(30000, 0.0, FindingCode.GpuPcieErrorsMany, FindingLevel.Problem), InlineData(3, 1.0, FindingCode.GpuPcieErrorsFatal, FindingLevel.Problem)]
    public void Pcie_errors_since_start_are_judged_by_count(double total, double? fatal, FindingCode code, FindingLevel level)
    { var f = Assert.Single(GpuCheck.PcieErrors(new(total, fatal), "RTX")); Assert.Equal((code, level), (f.Code, f.Level)); }
    [Fact] public void Without_counters_pcie_is_not_judged() => Assert.Empty(GpuCheck.PcieErrors(new(null, null), "RTX"));
    [Theory, InlineData(2, FindingCode.GpuPcieCleanUnderLoad, FindingLevel.Good), InlineData(40, FindingCode.GpuPcieErrorsUnderLoad, FindingLevel.Attention), InlineData(900, FindingCode.GpuPcieErrorsUnderLoad, FindingLevel.Problem)]
    public void Pcie_errors_added_under_load(double added, FindingCode code, FindingLevel level)
    {
        var f = Assert.Single(GpuCheck.Evaluate(new GpuRunTrace("RTX", HardwareVendor.Nvidia, null, null, null, null, null, null, added)));
        Assert.Equal((code, level), (f.Code, f.Level));
    }
    private static Mazesta.Core.Providers.DriveHealth Drive(string? status, int? wear, long? mediaErrors = null, string bus = "NVMe") => new("SSD", null, status, wear, 40, 60, mediaErrors, 0, 1000, bus);
    [Theory, InlineData("Healthy", 5, FindingCode.DriveHealthy, FindingLevel.Good), InlineData("Healthy", 75, FindingCode.DriveWorn, FindingLevel.Attention),
        InlineData("Healthy", 95, FindingCode.DriveWorn, FindingLevel.Problem), InlineData("Warning", 5, FindingCode.DriveUnhealthy, FindingLevel.Attention),
        InlineData("Unhealthy", 5, FindingCode.DriveUnhealthy, FindingLevel.Problem), InlineData("Healthy", null, FindingCode.DriveHealthy, FindingLevel.Good)]
    public void Drive_health_is_judged_by_its_own_report(string status, int? wear, FindingCode code, FindingLevel level)
    { var f = Assert.Single(DriveCheck.Evaluate([Drive(status, wear)])); Assert.Equal((code, level), (f.Code, f.Level)); }
    [Fact] public void Nvme_media_errors_need_attention() => Assert.Equal(FindingCode.DriveUnhealthy, Assert.Single(DriveCheck.Evaluate([Drive("Healthy", 5, 3)])).Code);
    [Fact] public void A_drive_that_reports_nothing_says_nothing() => Assert.Empty(DriveCheck.Evaluate([Drive(null, null, bus: "SATA")]));
    [Fact] public void A_first_run_is_not_judged() => Assert.Null(SelfCheck.Evaluate("CPU", HardwareKind.Cpu, 1000, [], true, "pts"));
    [Theory, InlineData(980, FindingCode.BenchAsBefore, FindingLevel.Good), InlineData(850, FindingCode.BenchSlowerThanBefore, FindingLevel.Attention), InlineData(700, FindingCode.BenchSlowerThanBefore, FindingLevel.Problem)]
    public void A_run_is_judged_against_the_machines_own_earlier_runs(double mine, FindingCode code, FindingLevel level)
    { var f = SelfCheck.Evaluate("CPU", HardwareKind.Cpu, mine, [1000, 990, 1010], true, "pts")!; Assert.Equal((code, level), (f.Code, f.Level)); }}
