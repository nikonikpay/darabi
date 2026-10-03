using Microsoft.Win32; using Xunit; using Mazesta.Diagnostics.Windows;
namespace Mazesta.Diagnostics.Tests;

public class NetRepairTests
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private sealed class Registry : IRegistryAccess
    {
        public Dictionary<string, object> Values { get; } = [];
        public object? Get(RegistryHive hive, string path, string name) => hive == RegistryHive.CurrentUser && path == Key ? Values.GetValueOrDefault(name) : null;
        public void Set(RegistryHive hive, string path, string name, object value, RegistryValueKind kind) => Values[name] = value;
        public void Delete(RegistryHive hive, string path, string name) => Values.Remove(name);
        public void DeleteKey(RegistryHive hive, string path) => Values.Clear();
    }

    private static NetCheck[] Steps(params (string Id, NetCheckResult Result)[] s) => [.. s.Select(x => new NetCheck(x.Id, x.Result, x.Id == NetRepair.AdapterStep && x.Result == NetCheckResult.Failed ? "no connected adapter" : null))];
    private const NetCheckResult Ok = NetCheckResult.Ok, Failed = NetCheckResult.Failed, Skipped = NetCheckResult.Skipped;

    [Fact] public void The_first_step_that_fails_names_where_the_connection_stops()
    {
        Assert.Equal(NetVerdict.NoAdapter, NetRepair.Diagnose(Steps(("adapter", Failed), ("gateway", Skipped), ("internet", Skipped))));
        Assert.Equal(NetVerdict.NoAddress, NetRepair.Diagnose([new("adapter", Failed, "Wi-Fi: 169.254.12.7")]));
        Assert.Equal(NetVerdict.NoGateway, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Failed), ("internet", Failed))));
        Assert.Equal(NetVerdict.NoInternet, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Ok), ("internet", Failed))));
        Assert.Equal(NetVerdict.DnsFails, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Ok), ("internet", Ok), ("dns", Failed), ("web", Skipped))));
        Assert.Equal(NetVerdict.WebBlocked, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Ok), ("internet", Ok), ("dns", Ok), ("web", Failed), ("proxy", Skipped))));
        Assert.Equal(NetVerdict.ProxyFails, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Ok), ("internet", Ok), ("dns", Ok), ("web", Ok), ("proxy", Failed))));
    }

    [Fact] public void A_router_that_ignores_echoes_is_no_fault_while_the_internet_answers()
        => Assert.Equal(NetVerdict.Connected, NetRepair.Diagnose(Steps(("adapter", Ok), ("gateway", Failed), ("internet", Ok), ("dns", Ok), ("web", Ok), ("proxy", Ok))));

    [Fact] public void The_reset_is_never_ticked_for_the_user_and_a_working_connection_gets_no_repair()
    {
        var set = new ProxySettings(true, "127.0.0.1:8080", null); var none = new ProxySettings(false, null, null);
        Assert.Equal(["proxy", "dns", "flush"], NetRepair.Suggested(NetVerdict.WebBlocked, set));
        Assert.Equal(["dns", "flush"], NetRepair.Suggested(NetVerdict.DnsFails, none));
        Assert.Equal(["proxy", "flush"], NetRepair.Suggested(NetVerdict.ProxyFails, set));
        Assert.Empty(NetRepair.Suggested(NetVerdict.Connected, set));
        Assert.All(Enum.GetValues<NetVerdict>(), v => Assert.DoesNotContain("reset", NetRepair.Suggested(v, set)));
    }

    [Fact] public async Task Clearing_the_proxy_switches_it_off_removes_server_and_script_and_says_what_was_there()
    {
        var reg = new Registry(); reg.Values["ProxyEnable"] = 1; reg.Values["ProxyServer"] = "10.0.0.5:3128"; reg.Values["AutoConfigURL"] = "http://x/proxy.pac";
        var runner = new WindowsToolsTests.FakeRunner();
        Assert.True(NetRepair.ReadProxy(reg).HasData);
        var (before, error) = await NetRepair.ClearProxyAsync(reg, runner, CancellationToken.None);
        Assert.Null(error); Assert.Equal(new ProxySettings(true, "10.0.0.5:3128", "http://x/proxy.pac"), before);
        Assert.Equal(new ProxySettings(false, null, null), NetRepair.ReadProxy(reg)); Assert.False(NetRepair.ReadProxy(reg).HasData);
        Assert.Equal(["netsh.exe winhttp reset proxy"], runner.Calls);
    }

    [Fact] public void A_server_left_behind_with_the_proxy_off_still_counts_as_data()
        => Assert.True(new ProxySettings(false, "127.0.0.1:1080", null).HasData);

    [Fact] public async Task The_reset_runs_winsock_then_the_ip_stack()
    {
        var runner = new WindowsToolsTests.FakeRunner("ok");
        var (error, output) = await NetRepair.ResetStackAsync(runner, CancellationToken.None);
        Assert.Null(error); Assert.Equal(["netsh.exe winsock reset", "netsh.exe int ip reset"], runner.Calls); Assert.Equal(2, output.Count);
    }
}
