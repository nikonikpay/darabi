using Xunit;
using System.Text.Json.Nodes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Persistence.Tests;
public class AppPathsTests
{
    [Fact] public void Portable_marker_puts_data_next_to_exe()
    { var p = AppPaths.Create(@"C:\Apps\Mazesta", @"C:\Users\u\AppData\Local", portableMarkerExists: true); Assert.True(p.IsPortable); Assert.Equal(@"C:\Apps\Mazesta\Data", p.DataRoot); Assert.Equal(@"C:\Apps\Mazesta\Data\config\appconfig.json", p.ConfigFile); }
    [Fact] public void Without_marker_uses_local_app_data()
    { var p = AppPaths.Create(@"C:\Apps\Mazesta", @"C:\Users\u\AppData\Local", false); Assert.False(p.IsPortable); Assert.Equal(@"C:\Users\u\AppData\Local\Mazesta\Test", p.DataRoot); Assert.EndsWith(@"\logs", p.LogsDir); }
}
