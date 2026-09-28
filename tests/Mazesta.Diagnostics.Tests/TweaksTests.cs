using Microsoft.Win32; using Xunit; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Diagnostics.Tests;

public class TweaksTests
{
    /// <summary>A registry in a dictionary: what the tweaks read and write, with no Windows underneath.</summary>
    private sealed class FakeRegistry : IRegistryAccess
    {
        public Dictionary<(RegistryHive, string, string), object> Values { get; } = [];
        public object? Get(RegistryHive hive, string path, string name) => Values.GetValueOrDefault((hive, path, name));
        public void Set(RegistryHive hive, string path, string name, object value, RegistryValueKind kind) => Values[(hive, path, name)] = value;
        public void Delete(RegistryHive hive, string path, string name) => Values.Remove((hive, path, name));
        public void DeleteKey(RegistryHive hive, string path) { foreach (var k in Values.Keys.Where(k => k.Item1 == hive && k.Item2.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList()) Values.Remove(k); }
    }
    private static Tweak T(string id) => TweakCatalog.Find(id)!;

    [Fact] public void A_machine_that_never_had_the_values_reads_as_Windows_default()
    {
        var engine = new TweakEngine(new FakeRegistry(), new WindowsToolsTests.FakeRunner());
        Assert.Equal(TweakState.NotApplied, engine.Read(T("telemetry")));
        Assert.Equal(TweakState.Applied, engine.Read(T("snapping")));      // Windows snaps windows unless told not to
        Assert.Equal(TweakState.NotApplied, engine.Read(T("fileExtensions")));
    }

    [Fact] public async Task Apply_then_undo_returns_the_registry_to_where_it_was()
    {
        var reg = new FakeRegistry(); var engine = new TweakEngine(reg, new WindowsToolsTests.FakeRunner());
        Assert.Null(await engine.RunAsync(T("telemetry"), apply: true, null, CancellationToken.None));
        Assert.Equal(TweakState.Applied, engine.Read(T("telemetry")));
        Assert.Null(await engine.RunAsync(T("telemetry"), apply: false, null, CancellationToken.None));
        Assert.Equal(TweakState.NotApplied, engine.Read(T("telemetry")));
        Assert.DoesNotContain(reg.Values.Keys, k => k.Item3 == "AllowTelemetry");   // the policy is removed, not set to some other number
    }

    [Fact] public void Some_values_set_and_others_not_is_Partial()
    {
        var reg = new FakeRegistry(); reg.Set(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
        Assert.Equal(TweakState.Partial, new TweakEngine(reg, new WindowsToolsTests.FakeRunner()).Read(T("gameDvr")));
    }

    [Fact] public async Task The_classic_menu_undo_removes_its_key()
    {
        var reg = new FakeRegistry(); var engine = new TweakEngine(reg, new WindowsToolsTests.FakeRunner());
        await engine.RunAsync(T("classicMenu"), true, null, CancellationToken.None);
        Assert.Equal(TweakState.Applied, engine.Read(T("classicMenu")));
        await engine.RunAsync(T("classicMenu"), false, null, CancellationToken.None);
        Assert.Empty(reg.Values);
    }

    [Fact] public async Task Commands_run_on_apply_and_their_undo_on_undo()
    {
        var runner = new WindowsToolsTests.FakeRunner(); var engine = new TweakEngine(new FakeRegistry(), runner);
        await engine.RunAsync(T("teredo"), true, null, CancellationToken.None); await engine.RunAsync(T("teredo"), false, null, CancellationToken.None);
        Assert.Equal(["netsh.exe interface teredo set state disabled", "netsh.exe interface teredo set state default"], runner.Calls);
        Assert.Equal(TweakState.Unknown, engine.Read(T("teredo")));   // a command's effect is not read back as if it were known
    }

    [Fact] public void Actions_have_no_state_and_no_undo()
    {
        Assert.False(T("restorePoint").CanUndo); Assert.False(T("tempFiles").CanUndo);
        Assert.Equal(TweakState.Unknown, new TweakEngine(new FakeRegistry(), new WindowsToolsTests.FakeRunner()).Read(T("restorePoint")));
    }

    [Fact] public void Every_preset_names_real_tweaks_and_ids_are_unique()
    {
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());
        foreach (var id in TweakCatalog.Presets.Values.SelectMany(x => x)) Assert.NotNull(TweakCatalog.Find(id));
        Assert.All(TweakCatalog.Presets.Values, p => Assert.Equal("restorePoint", p[0]));
    }

    [Fact] public async Task Update_profiles_are_read_back_from_the_policies_they_write()
    {
        var reg = new FakeRegistry(); var runner = new WindowsToolsTests.FakeRunner();
        Assert.Equal(UpdateProfile.Default, UpdateProfiles.Read(reg));
        await UpdateProfiles.ApplyAsync(UpdateProfile.Recommended, reg, runner, null, CancellationToken.None);
        Assert.Equal(UpdateProfile.Recommended, UpdateProfiles.Read(reg));
        await UpdateProfiles.ApplyAsync(UpdateProfile.Disabled, reg, runner, null, CancellationToken.None);
        Assert.Equal(UpdateProfile.Disabled, UpdateProfiles.Read(reg));
        Assert.Contains("sc.exe config wuauserv start= disabled", runner.Calls);
        await UpdateProfiles.ApplyAsync(UpdateProfile.Default, reg, runner, null, CancellationToken.None);
        Assert.Equal(UpdateProfile.Default, UpdateProfiles.Read(reg)); Assert.Empty(reg.Values);
        Assert.Equal("sc.exe config wuauserv start= demand", runner.Calls[^2]);
    }

    [Fact] public void A_policy_set_by_hand_is_Custom_not_a_profile()
    {
        var reg = new FakeRegistry(); reg.Set(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DeferQualityUpdatesPeriodInDays", 10, RegistryValueKind.DWord);
        Assert.Equal(UpdateProfile.Custom, UpdateProfiles.Read(reg));
    }

    [Fact] public void Dns_commands_set_both_servers_or_go_back_to_dhcp()
    {
        var set = DnsChoice.Commands("Ethernet", "cloudflare");
        Assert.Contains("address=1.1.1.1", set[0].Arguments); Assert.Contains("address=1.0.0.1", set[1].Arguments);
        Assert.Contains("source=dhcp", Assert.Single(DnsChoice.Commands("Ethernet", "auto")).Arguments);
        Assert.Equal("google", DnsChoice.Identify(["8.8.8.8"])); Assert.Equal("auto", DnsChoice.Identify(["192.168.1.1"]));
    }
}
