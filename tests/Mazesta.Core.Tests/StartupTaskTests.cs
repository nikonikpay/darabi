using Mazesta.Core.Tray;
using Xunit;
namespace Mazesta.Core.Tests;

public class StartupTaskTests
{
    [Fact] public void Register_quotes_a_path_with_spaces_and_runs_at_logon_with_highest_privileges()
    {
        var args = StartupTask.RegisterArguments(@"C:\Program Files\Mazesta\MazestaTray.exe");
        Assert.Equal("/Create /TN \"MazestaTray\" /TR \"\\\"C:\\Program Files\\Mazesta\\MazestaTray.exe\\\"\" /SC ONLOGON /RL HIGHEST /F", args);
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
