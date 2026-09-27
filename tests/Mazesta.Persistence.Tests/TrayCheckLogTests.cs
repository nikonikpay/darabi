using Xunit;
namespace Mazesta.Persistence.Tests;

public class TrayCheckLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-traylog-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "tray", "checks.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private static TrayCheck Check(int minute, string? error = null, params string[] problems)
        => new(new DateTimeOffset(2026, 9, 28, 10, minute % 60, 0, TimeSpan.Zero), "temps", 50, null, [], problems, error);

    [Fact] public void A_missing_or_damaged_file_reads_as_no_checks()
    {
        Assert.Empty(TrayCheckLog.Read(File_));
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!); File.WriteAllText(File_, "[{ broken");
        Assert.Empty(TrayCheckLog.Read(File_));
    }
    [Fact] public void Checks_round_trip_newest_last_and_unread_values_stay_null()
    {
        TrayCheckLog.Append(File_, Check(1)); TrayCheckLog.Append(File_, Check(2, problems: "hot"));
        var all = TrayCheckLog.Read(File_);
        Assert.Equal(2, all.Count); Assert.Equal(2, all[^1].Time.Minute); Assert.Null(all[0].GpuTempC);
        Assert.False(all[0].IsProblem); Assert.True(all[1].IsProblem); Assert.True(Check(3, error: "no provider").IsProblem);
    }
    [Fact] public void Only_the_newest_are_kept()
    {
        for (int i = 0; i < TrayCheckLog.Keep + 5; i++) TrayCheckLog.Append(File_, Check(i));
        var all = TrayCheckLog.Read(File_);
        Assert.Equal(TrayCheckLog.Keep, all.Count); Assert.Equal((TrayCheckLog.Keep + 4) % 60, all[^1].Time.Minute);
    }
}
