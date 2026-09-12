using Xunit;
using Mazesta.Persistence; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence.Tests;
public class RollingFileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-log-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    [Fact] public void Writes_line_with_level_category_and_message()
    {
        using var p = new RollingFileLoggerProvider(_dir, now: () => new DateTime(2026, 9, 12, 10, 0, 0));
        p.CreateLogger("Mazesta.Test").LogWarning("hello {Name}", "world"); p.Flush();
        var text = File.ReadAllText(Path.Combine(_dir, "mazesta-test-20260912.log"));
        Assert.Contains("WRN", text); Assert.Contains("Mazesta.Test", text); Assert.Contains("hello world", text);
    }
    [Fact] public void Keeps_only_newest_files()
    {
        Directory.CreateDirectory(_dir);
        for (int i = 1; i <= 9; i++) File.WriteAllText(Path.Combine(_dir, $"mazesta-test-202609{i:00}.log"), "x");
        using var p = new RollingFileLoggerProvider(_dir, keepFiles: 7, now: () => new DateTime(2026, 9, 12));
        p.CreateLogger("c").LogInformation("new"); p.Flush();
        Assert.Equal(7, Directory.GetFiles(_dir, "mazesta-test-*.log").Length); Assert.False(File.Exists(Path.Combine(_dir, "mazesta-test-20260901.log")));
    }
}
