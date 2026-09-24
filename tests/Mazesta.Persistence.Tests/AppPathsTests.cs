using Xunit;
using Mazesta.Persistence;
namespace Mazesta.Persistence.Tests;
public class AppPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-paths-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact] public void All_data_lives_in_the_Data_folder_next_to_the_exe()
    {
        var p = AppPaths.Create(@"E:\Tools\Mazesta");
        Assert.Equal(@"E:\Tools\Mazesta\Data", p.DataRoot); Assert.Equal(@"E:\Tools\Mazesta\Data\config\appconfig.json", p.ConfigFile);
        Assert.All(new[] { p.LogsDir, p.ReportsDir, p.SessionsDir, p.HistoryDir, p.CacheDir }, d => Assert.StartsWith(p.DataRoot + @"\", d));
    }
    [Fact] public void An_earlier_installed_versions_data_is_copied_in_once_and_left_in_place()
    {
        string legacy = Path.Combine(_dir, "legacy"); Directory.CreateDirectory(Path.Combine(legacy, "reports", "r1")); File.WriteAllText(Path.Combine(legacy, "reports", "r1", "report.json"), "{}");
        var p = AppPaths.Create(Path.Combine(_dir, "app"));
        Assert.True(p.AdoptLegacyData(legacy)); Assert.True(File.Exists(Path.Combine(p.ReportsDir, "r1", "report.json"))); Assert.True(File.Exists(Path.Combine(legacy, "reports", "r1", "report.json")));
        Assert.False(p.AdoptLegacyData(legacy));   // Data exists now: never copied twice, never over the portable data
        Assert.False(AppPaths.Create(Path.Combine(_dir, "other")).AdoptLegacyData(Path.Combine(_dir, "missing")));
    }
}
