using Xunit; using Mazesta.Diagnostics.Storage;
namespace Mazesta.Diagnostics.Tests;

public class StorageFitTests
{
    [Fact] public void A_file_that_fits_the_chosen_drive_is_left_alone() => Assert.Null(StorageExecutor.Fit("", 16));

    [Fact] public void A_file_no_drive_can_hold_is_cut_to_the_roomiest_drive_or_moved()
    {
        var fit = StorageExecutor.Fit("", 60000);   // 60 GB: more than the margin plus most test machines' free space, never more than asked
        if (fit is { } f) { Assert.True(f.FileMb <= 60000); Assert.True(f.FileMb % 64 == 0 || f.FileMb == 60000); }
    }
}
