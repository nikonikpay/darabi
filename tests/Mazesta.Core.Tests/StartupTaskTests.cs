using Mazesta.Core.Tray;
using Xunit;
namespace Mazesta.Core.Tests;

public class StartupTaskTests
{
    [Fact] public void Register_runs_at_logon_with_highest_privileges_on_battery_too_and_without_a_time_limit()
    {
        Assert.Equal("/Create /TN \"MazestaTray\" /XML \"C:\\Temp\\t.xml\" /F", StartupTask.RegisterArguments(@"C:\Temp\t.xml"));
        var xml = System.Xml.Linq.XDocument.Parse(StartupTask.TaskXml(@"C:\Program Files\M&S\MazestaTray.exe", @"PC\saeed")); var ns = xml.Root!.Name.Namespace;
        string V(string name) => xml.Descendants(ns + name).Single().Value;
        Assert.Equal(@"C:\Program Files\M&S\MazestaTray.exe", V("Command")); Assert.Equal("HighestAvailable", V("RunLevel"));
        Assert.Equal("false", V("DisallowStartIfOnBatteries")); Assert.Equal("false", V("StopIfGoingOnBatteries")); Assert.Equal("PT0S", V("ExecutionTimeLimit"));
        Assert.Equal(@"PC\saeed", xml.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
    }
    [Fact] public void Query_and_delete_target_the_task_by_name()
    {
        Assert.Equal("/Query /TN \"MazestaTray\" /FO LIST", StartupTask.QueryArguments());
        Assert.Equal("/Delete /TN \"MazestaTray\" /F", StartupTask.DeleteArguments());
    }
    [Fact] public void IsRegistered_true_only_when_the_task_name_is_in_the_query_output()
    {
        Assert.True(StartupTask.IsRegistered("TaskName:  \\MazestaTray\r\nNext Run Time: N/A\r\n"));
        Assert.False(StartupTask.IsRegistered("ERROR: The system cannot find the file specified.\r\n"));
    }
}
