using Xunit; using Mazesta.Persistence;
namespace Mazesta.Persistence.Tests;

public class TrayIntervalsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tray-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "appconfig.json");
    public TrayIntervalsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact] public void Missing_file_gives_the_defaults() => Assert.Equal(new TrayIntervals(20, 60, 60, 1440), TrayIntervals.Read(File_));
    [Fact] public void Values_are_read_from_the_file()
    { File.WriteAllText(File_, """{"trayFirstCheckSeconds":45,"trayIdleIntervalMinutes":5,"trayWatchIntervalSeconds":15,"trayHealthIntervalMinutes":60}"""); Assert.Equal(new TrayIntervals(45, 5, 15, 60), TrayIntervals.Read(File_)); }
    [Fact] public void Zero_negative_or_non_numeric_values_fall_back_to_the_default_not_a_crashing_timer()
    { File.WriteAllText(File_, """{"trayFirstCheckSeconds":0,"trayIdleIntervalMinutes":-3,"trayWatchIntervalSeconds":"soon"}"""); Assert.Equal(new TrayIntervals(20, 60, 60, 1440), TrayIntervals.Read(File_)); }
    [Fact] public void A_damaged_file_gives_the_defaults_and_is_left_alone()
    { File.WriteAllText(File_, "{ not json"); Assert.Equal(new TrayIntervals(20, 60, 60, 1440), TrayIntervals.Read(File_)); Assert.Equal("{ not json", File.ReadAllText(File_)); Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*")); }
    [Fact] public void An_old_schema_file_is_not_migrated_or_rewritten_by_reading()
    { const string v2 = """{"schemaVersion":2,"language":"fa"}"""; File.WriteAllText(File_, v2); TrayIntervals.Read(File_); Assert.Equal(v2, File.ReadAllText(File_)); }
    [Fact] public void An_unmigrated_file_still_holding_the_old_defaults_follows_the_new_schedule_but_keeps_what_a_person_chose()
    { File.WriteAllText(File_, """{"schemaVersion":3,"trayIdleIntervalMinutes":10,"trayWatchIntervalSeconds":45,"trayHealthIntervalMinutes":30,"trayCpuAlertC":100}"""); var t = TrayIntervals.Read(File_); Assert.Equal((60, 45, 1440, 98), (t.IdleMinutes, t.WatchSeconds, t.HealthMinutes, t.CpuAlertC)); }
}
