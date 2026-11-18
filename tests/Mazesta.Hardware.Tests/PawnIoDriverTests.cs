using Mazesta.Hardware.Lhm;
using Xunit;
namespace Mazesta.Hardware.Tests;
public class PawnIoDriverTests
{
    [Fact] public void A_driver_path_to_a_missing_file_is_not_an_installed_driver() => Assert.False(PawnIoDriver.DriverFileExists(@"\SystemRoot\System32\DriverStore\FileRepository\pawnio.inf_none\PawnIO.sys"));
    [Fact] public void A_path_to_a_file_that_is_there_counts() { string f = Path.GetTempFileName(); try { Assert.True(PawnIoDriver.DriverFileExists(@"\??\" + f)); Assert.True(PawnIoDriver.DriverFileExists("\"" + f + "\"")); } finally { File.Delete(f); } }
    [Fact] public void System32_files_resolve_under_the_windows_folder() => Assert.True(PawnIoDriver.DriverFileExists(@"\SystemRoot\System32\kernel32.dll"));
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("system32\\drivers\\x.sys")]
    public void An_unreadable_or_unrecognised_path_is_not_taken_for_a_broken_driver(string? path) => Assert.True(PawnIoDriver.DriverFileExists(path));
}
