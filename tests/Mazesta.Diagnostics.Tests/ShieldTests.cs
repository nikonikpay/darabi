using System.Text; using Mazesta.Diagnostics.Windows; using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class ShieldTests
{
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Fact] public void A_program_for_the_firewall_is_a_full_path_to_an_exe_that_exists()
    {
        string self = Path.Combine(AppContext.BaseDirectory, "testhost.exe");
        Assert.Equal(ShieldProblem.NotFullPath, ShieldPaths.Program("notepad.exe").Problem); Assert.Equal(ShieldProblem.NotFullPath, ShieldPaths.Program(@"\\server\share\a.exe").Problem);
        Assert.Equal(ShieldProblem.NotFullPath, ShieldPaths.Program(@"C:\Games\*.exe").Problem); Assert.Equal(ShieldProblem.NotFullPath, ShieldPaths.Program("").Problem);
        Assert.Equal(ShieldProblem.NotExe, ShieldPaths.Program(Path.Combine(Windows, "win.ini")).Problem);
        Assert.Equal(ShieldProblem.Missing, ShieldPaths.Program(Path.Combine(AppContext.BaseDirectory, "nothing-here.exe")).Problem);
        Assert.Equal(ShieldProblem.None, ShieldPaths.Program(self, mustExist: false).Problem); Assert.Equal(ShieldProblem.None, ShieldPaths.Program($"\"{self}\"", mustExist: false).Problem);
    }

    [Fact] public void A_program_of_windows_itself_is_only_blocked_when_the_user_said_so()
    {
        string notepad = Path.Combine(Windows, "System32", "notepad.exe");
        Assert.Equal(ShieldProblem.SystemFolder, ShieldPaths.Program(notepad, mustExist: false).Problem); Assert.Equal(ShieldProblem.None, ShieldPaths.Program(notepad, mustExist: false, allowSystem: true).Problem);
    }

    [Fact] public void An_exclusion_as_wide_as_a_drive_or_a_main_folder_is_asked_about()
    {
        string root = Path.GetPathRoot(Windows)!;
        Assert.Equal(ShieldProblem.TooBroad, ShieldPaths.Exclusion(root).Problem); Assert.Equal(ShieldProblem.TooBroad, ShieldPaths.Exclusion(Windows).Problem);
        Assert.Equal(ShieldProblem.TooBroad, ShieldPaths.Exclusion(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Problem);
        Assert.Equal(ShieldProblem.None, ShieldPaths.Exclusion(root, allowBroad: true).Problem);
        Assert.Equal(ShieldProblem.None, ShieldPaths.Exclusion(AppContext.BaseDirectory).Problem); Assert.Equal(ShieldProblem.Missing, ShieldPaths.Exclusion(Path.Combine(AppContext.BaseDirectory, "gone")).Problem);
    }

    [Fact] public void A_path_travels_to_powershell_as_text_that_cannot_be_taken_for_code()
    {
        string odd = @"C:\Games\Bob’s 'Game' $(calc) `x`\a;b.exe", literal = ShieldTools.PathLiteral(odd);
        Assert.DoesNotContain("Bob", literal); Assert.DoesNotContain("calc", literal);
        string b64 = literal[(literal.IndexOf("('") + 2)..literal.IndexOf("')")];
        Assert.Equal(odd, Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        Assert.Equal(ShieldPaths.RuleId(odd), ShieldPaths.RuleId(odd.ToUpperInvariant())); Assert.NotEqual(ShieldPaths.RuleId(odd), ShieldPaths.RuleId(odd + "2"));
        Assert.Contains("-Direction $d -Action Block", ShieldTools.BlockScript(odd)); Assert.Contains("Remove-NetFirewallRule", ShieldTools.UnblockScript(odd));
    }

    [Fact] public void The_firewall_answer_is_read_even_when_powershell_writes_one_item_as_an_object()
    {
        string json = """{"rules":[{"n":"a","d":"Inbound","e":"True","a":"Block","p":"C:\\G\\x.exe"},{"n":"b","d":"Outbound","e":"True","a":"Block","p":"c:\\g\\X.EXE"},{"n":"c","d":"Outbound","e":"False","a":"Block","p":"C:\\G\\off.exe"},{"n":"d","d":"Outbound","e":"True","a":"Allow","p":"C:\\G\\ok.exe"}],"profiles":{"n":"Public","e":"False"}}""";
        var s = ShieldTools.ParseFirewall(json)!;
        var one = Assert.Single(s.Blocked); Assert.True(one.Inbound && one.Outbound); Assert.Equal("x.exe", one.Name);
        Assert.Equal([("Public", false)], s.Profiles); Assert.Null(ShieldTools.ParseFirewall("not json"));
    }

    [Fact] public void The_antivirus_answer_names_the_product_that_really_guards_the_computer()
    {
        string json = """{"defender":true,"real":false,"tamper":true,"products":[{"n":"Windows Defender","on":false},{"n":"Kaspersky Free","on":true}],"exclusions":"C:\\Tools"}""";
        var s = ShieldTools.ParseAv(json)!;
        Assert.True(s.Defender); Assert.False(s.RealTime); Assert.True(s.Tamper); Assert.Equal("Kaspersky Free", s.Other); Assert.Equal([@"C:\Tools"], s.Exclusions);
        Assert.Null(ShieldTools.ParseAv("{")); Assert.Null(ShieldTools.ParseAv("""{"defender":true,"products":[{"n":"Windows Defender","on":true}],"exclusions":[]}""")!.Other);
    }

    /// <summary>A computer that does what it is told: the rules a script makes are the rules the next listing shows.</summary>
    private sealed class FakeWindows : ICommandRunner
    {
        public readonly HashSet<string> Rules = new(StringComparer.OrdinalIgnoreCase); public readonly List<string> Excluded = []; public bool RefuseExclusions, Stopped, RefuseStop, TaskExists, FirewallOn = true;
        public Task<CommandResult> RunAsync(string file, string arguments, Encoding output, Action<string>? line, CancellationToken ct)
        {
            string script = Encoding.Unicode.GetString(Convert.FromBase64String(arguments[(arguments.LastIndexOf(' ') + 1)..]));
            if (script.Contains("New-NetFirewallRule")) { string id = System.Text.RegularExpressions.Regex.Match(script, "\\$id='([0-9A-F]+)'").Groups[1].Value; Rules.Add(id); return Ok(); }
            if (script.Contains("Remove-NetFirewallRule") && !script.Contains("New-NetFirewallRule")) { Rules.Remove(System.Text.RegularExpressions.Regex.Match(script, "\\$id='([0-9A-F]+)'").Groups[1].Value); return Ok(); }
            if (script.Contains("Set-NetFirewallProfile")) { FirewallOn = script.Contains("-Enabled True"); return Ok(); }
            if (script.Contains("Get-NetFirewallRule -Action Block -Enabled"))
                return Ok(ShieldTools.Marker + "{\"rules\":[" + string.Join(",", Rules.Select(id => $"{{\"n\":\"MazestaBlock-{id}-Inbound\",\"d\":\"Inbound\",\"e\":\"True\",\"a\":\"Block\",\"p\":\"{Known[id]}\"}},{{\"n\":\"MazestaBlock-{id}-Outbound\",\"d\":\"Outbound\",\"e\":\"True\",\"a\":\"Block\",\"p\":\"{Known[id]}\"}}")) + "],\"profiles\":[{\"n\":\"Public\",\"e\":\"" + (FirewallOn ? "True" : "False") + "\"}]}");
            if (script.Contains("Add-MpPreference") || script.Contains("Remove-MpPreference"))
            {
                if (RefuseExclusions) return Ok();   // Defender says nothing and does nothing
                string path = Encoding.UTF8.GetString(Convert.FromBase64String(System.Text.RegularExpressions.Regex.Match(script, "FromBase64String\\('([^']+)'").Groups[1].Value));
                if (script.Contains("Add-MpPreference")) Excluded.Add(path); else Excluded.Remove(path); return Ok();
            }
            if (script.Contains("Set-MpPreference"))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(script, "(?<!Un)Register-ScheduledTask")) TaskExists = true;
                if (script.Contains("Unregister-ScheduledTask")) TaskExists = false;
                if (!RefuseStop) Stopped = script.Contains("-DisableRealtimeMonitoring $true"); return Ok();
            }
            if (script.Contains("Unregister-ScheduledTask")) { TaskExists = false; return Ok(); }
            if (script.Contains("Get-MpComputerStatus")) return Ok(ShieldTools.Marker + "{\"defender\":true,\"real\":" + (Stopped ? "false" : "true") + ",\"restore\":" + (TaskExists ? "true" : "false") + ",\"tamper\":" + (RefuseStop ? "true" : "null") + ",\"behav\":true,\"products\":[],\"exclusions\":[" + string.Join(",", Excluded.Select(e => "\"" + e.Replace("\\", "\\\\") + "\"")) + "]}");
            throw new InvalidOperationException("unexpected script");
        }
        public readonly Dictionary<string, string> Known = [];
        private static Task<CommandResult> Ok(params string[] lines) => Task.FromResult(new CommandResult(0, lines));
    }

    [Fact] public async Task A_block_is_read_back_and_is_the_same_rule_when_asked_twice()
    {
        var win = new FakeWindows(); string exe = Path.Combine(AppContext.BaseDirectory, "testhost.exe"); win.Known[ShieldPaths.RuleId(exe)] = exe.Replace("\\", "\\\\");
        var tools = new ShieldTools(win);
        Assert.True((await tools.BlockAsync(exe, false, default)).Ok); Assert.True((await tools.BlockAsync(exe, false, default)).Ok); Assert.Single(win.Rules);
        Assert.Single((await tools.FirewallAsync(default))!.Blocked);
        Assert.True((await tools.UnblockAsync(exe, default)).Ok); Assert.Empty(win.Rules);
    }

    [Fact] public void Every_blocking_rule_is_listed_and_the_ones_of_other_makers_are_told_apart()
    {
        string json = """{"rules":[{"n":"MazestaBlock-AB-Inbound","d":"Inbound","e":"True","a":"Block","p":"C:\\G\\x.exe"},{"n":"{4F2A}","d":"Outbound","e":"True","a":"Block","p":"C:\\Vendor\\agent.exe"},{"n":"{9}","d":"Outbound","e":"True","a":"Block","p":"System"}],"profiles":[]}""";
        var s = ShieldTools.ParseFirewall(json)!; Assert.Equal(2, s.Blocked.Count);
        Assert.True(s.Blocked.Single(b => b.Name == "x.exe").Ours); var other = s.Blocked.Single(b => b.Name == "agent.exe"); Assert.False(other.Ours); Assert.True(other.Outbound && !other.Inbound);
    }

    [Fact] public async Task The_firewall_switch_is_read_back_and_the_antivirus_stop_is_either_real_or_refused()
    {
        var win = new FakeWindows(); var tools = new ShieldTools(win);
        Assert.True((await tools.FirewallSwitchAsync(false, default)).Ok); Assert.False(win.FirewallOn); Assert.True((await tools.FirewallSwitchAsync(true, default)).Ok);
        Assert.True((await tools.AvStopAsync(false, default)).Ok); Assert.True(win.Stopped && win.TaskExists);   // until restart: a task puts it back
        var state = (await tools.AvAsync(default))!; Assert.True(state.Stopped && state.RestoreAtStart);
        Assert.True((await tools.AvStartAsync(default)).Ok); Assert.False(win.Stopped); Assert.False(win.TaskExists);
        Assert.True((await tools.AvStopAsync(true, default)).Ok); Assert.True(win.Stopped && !win.TaskExists);   // for good: no task
        Assert.True((await tools.AvStartAsync(default)).Ok);
        win.RefuseStop = true; Assert.Equal(ShieldProblem.NotAccepted, (await tools.AvStopAsync(false, default)).Problem); Assert.False(win.TaskExists);   // Tamper Protection: said so, and nothing left to restore
    }

    [Fact] public void The_restore_task_runs_as_the_system_at_start_and_the_permanent_stop_leaves_none()
    {
        string temp = ShieldTools.StopScript(false), perm = ShieldTools.StopScript(true);
        Assert.Contains("-AtStartup", temp); Assert.Contains("SYSTEM", temp); Assert.Contains("-EncodedCommand", temp); Assert.DoesNotContain("Set-ItemProperty", temp);
        Assert.DoesNotContain("Register-ScheduledTask", perm.Replace("Unregister-ScheduledTask", "")); Assert.Contains("Set-ItemProperty", perm);
        string enc = temp[(temp.IndexOf("$enc='") + 6)..]; enc = enc[..enc.IndexOf((char)39)];
        string restore = Encoding.Unicode.GetString(Convert.FromBase64String(enc));
        Assert.Contains("-DisableRealtimeMonitoring $false", restore); Assert.Contains("Remove-ItemProperty", restore); Assert.Contains("Unregister-ScheduledTask", restore);
    }

    [Fact] public async Task An_exclusion_windows_did_not_take_is_reported_not_celebrated()
    {
        var win = new FakeWindows(); var tools = new ShieldTools(win); string dir = AppContext.BaseDirectory.TrimEnd((char)92);
        Assert.True((await tools.ExcludeAsync(dir, false, default)).Ok); Assert.Single(win.Excluded);
        Assert.True((await tools.UnexcludeAsync(dir, default)).Ok); Assert.Empty(win.Excluded);
        win.RefuseExclusions = true; Assert.Equal(ShieldProblem.NotAccepted, (await tools.ExcludeAsync(dir, false, default)).Problem);
        Assert.Equal(ShieldProblem.TooBroad, (await tools.ExcludeAsync(Path.GetPathRoot(dir), false, default)).Problem);
    }
}
