using Xunit; using Mazesta.Persistence;
namespace Mazesta.Persistence.Tests;

public class TrayIntervalsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tray-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "appconfig.json");
    public TrayIntervalsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact] public void Missing_file_gives_the_defaults() => Assert.Equal(new TrayIntervals(20, 10, 30, 30), TrayIntervals.Read(File_));
    [Fact] public void Values_are_read_from_the_file()
    { File.WriteAllText(File_, """{"trayFirstCheckSeconds":45,"trayIdleIntervalMinutes":5,"trayWatchIntervalSeconds":15,"trayHealthIntervalMinutes":60}"""); Assert.Equal(new TrayIntervals(45, 5, 15, 60), TrayIntervals.Read(File_)); }
    [Fact] public void Zero_negative_or_non_numeric_values_fall_back_to_the_default_not_a_crashing_timer()
    { File.WriteAllText(File_, """{"trayFirstCheckSeconds":0,"trayIdleIntervalMinutes":-3,"trayWatchIntervalSeconds":"soon"}"""); Assert.Equal(new TrayIntervals(20, 10, 30, 30), TrayIntervals.Read(File_)); }
    [Fact] public void A_damaged_file_gives_the_defaults_and_is_left_alone()
    { File.WriteAllText(File_, "{ not json"); Assert.Equal(new TrayIntervals(20, 10, 30, 30), TrayIntervals.Read(File_)); Assert.Equal("{ not json", File.ReadAllText(File_)); Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*")); }
    [Fact] public void An_old_schema_file_is_not_migrated_or_rewritten_by_reading()
    { const string v2 = """{"schemaVersion":2,"language":"fa"}"""; File.WriteAllText(File_, v2); TrayIntervals.Read(File_); Assert.Equal(v2, File.ReadAllText(File_)); }
}
