using Xunit; using Mazesta.Core.Windows;
namespace Mazesta.Core.Tests;

public class PageFilePlanTests
{
    [Theory]
    [InlineData(PageFileMode.SystemManaged, null, 0, 0, null)]
    [InlineData(PageFileMode.None, null, 0, 0, null)]
    [InlineData(PageFileMode.Custom, null, 1024, 2048, "Tools_Vm_Error_Drive")]
    [InlineData(PageFileMode.Custom, "D:", 0, 0, null)]                       // Windows picks the size on D:
    [InlineData(PageFileMode.Custom, "C:", 8, 2048, "Tools_Vm_Error_Initial")]
    [InlineData(PageFileMode.Custom, "C:", 4096, 2048, "Tools_Vm_Error_Order")]
    [InlineData(PageFileMode.Custom, "C:", 4096, 49153, "Tools_Vm_Error_Max")]   // 3 × 16 GB
    [InlineData(PageFileMode.Custom, "C:", 60000, 49152, "Tools_Vm_Error_Order")]
    [InlineData(PageFileMode.Custom, "C:", 4096, 8192, null)]
    public void Plans_are_checked_as_windows_checks_them(PageFileMode mode, string? drive, long initial, long maximum, string? problem)
        => Assert.Equal(problem, new PageFilePlan(mode, drive, initial, maximum).Problem(16384, 100_000));
    [Fact] public void The_initial_size_has_to_fit_on_the_drive() => Assert.Equal("Tools_Vm_Error_Space", new PageFilePlan(PageFileMode.Custom, "C:", 4096, 8192).Problem(16384, 2000));
    [Fact] public void A_small_machine_may_still_have_a_4_gb_maximum() => Assert.Null(new PageFilePlan(PageFileMode.Custom, "C:", 1024, 4096).Problem(1024, null));
}
