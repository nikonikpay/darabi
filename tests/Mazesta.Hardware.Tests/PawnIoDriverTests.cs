using Mazesta.Hardware.Lhm;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class PawnIoDriverTests
{
    [Fact] public void A_driver_path_to_a_missing_file_is_not_an_installed_driver() => Assert.False(PawnIoDriver.DriverFileExists(@"\SystemRoot\System32\DriverStore\FileRepository\pawnio.inf_none\PawnIO.sys"));
    [Fact] public void A_path_to_a_file_that_is_there_counts() { string f = Path.GetTempFileName(); try { Assert.True(PawnIoDriver.DriverFileExists(@"\??\" + f)); Assert.True(PawnIoDriver.DriverFileExists("\"" + f + "\"")); } finally { File.Delete(f); } }
    [Fact] public void System32_files_resolve_under_the_windows_folder() => Assert.True(PawnIoDriver.DriverFileExists(@"\SystemRoot\System32\kernel32.dll"));
    [Theory] [InlineData(null, true)] [InlineData("2.1.0", true)] [InlineData("2.2.0", false)] [InlineData("2.2.0.0", false)] [InlineData("2.3.0", false)]
    public void The_setup_is_run_over_an_unrecorded_or_older_driver_only(string? listed, bool worth) => Assert.Equal(worth, PawnIoDriver.WorthSetup(listed is null ? null : Version.Parse(listed)));
    [Theory] [InlineData(false, "2.2.0", "1", true)] [InlineData(true, "2.2.0", "1", false)] [InlineData(true, null, "1", true)] [InlineData(true, "2.1.0", "1", true)] [InlineData(true, "2.2.0", "0", true)] [InlineData(true, "2.2.0", null, true)]
    public void The_driver_is_put_in_again_by_every_new_version_of_the_app(bool installed, string? listed, string? doneBy, bool due) => Assert.Equal(due, PawnIoDriver.IsDue(installed, listed is null ? null : Version.Parse(listed), doneBy, "1"));
    [Theory] [InlineData("0", "install", 0, false)] [InlineData("183,0,0", "install,uninstall,install", 0, true)] [InlineData("183,5", "install,uninstall", 5, false)] [InlineData("183,3010,0", "install,uninstall,install", 0, true)]
    public void A_driver_of_the_same_version_is_taken_out_and_put_in_again(string exits, string ran, int code, bool removed)
    {
        var left = new Queue<int>(exits.Split(',').Select(int.Parse)); var runs = new List<string>();
        var result = PawnIoDriver.Reinstall(a => { runs.Add(a.Split(' ')[0].TrimStart('-')); return left.Dequeue(); });
        Assert.Equal(ran, string.Join(",", runs)); Assert.Equal((code, removed), (result.Code!.Value, result.Removed));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("system32\\drivers\\x.sys")]
    public void An_unreadable_or_unrecognised_path_is_not_taken_for_a_broken_driver(string? path) => Assert.True(PawnIoDriver.DriverFileExists(path));
}
