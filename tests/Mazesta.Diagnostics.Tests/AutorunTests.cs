using Mazesta.Diagnostics.Windows;
using Xunit;
namespace Mazesta.Diagnostics.Tests;

public class AutorunTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\Acme\svc.exe"" -service", @"C:\Program Files\Acme\svc.exe")]
    [InlineData(@"C:\Acme\svc.exe /k run", @"C:\Acme\svc.exe")]
    [InlineData(null, "")]
    public void The_program_file_is_cut_out_of_an_image_path(string? image, string file) => Assert.Equal(file, Autorun.ImageFile(image));

    [Fact]
    public void Startup_switch_matches_task_managers_encoding()
    {
        Assert.True(Autorun.IsEnabled(null)); Assert.True(Autorun.IsEnabled(Autorun.ApprovedValue(true, DateTime.UtcNow)));
        var off = Autorun.ApprovedValue(false, new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
        Assert.False(Autorun.IsEnabled(off)); Assert.Equal(12, off.Length); Assert.Equal(3, off[0]);
        Assert.Equal(new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
    }
}
