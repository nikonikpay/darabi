using System.Text; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence;
public sealed class RollingFileLoggerProvider(string directory, string prefix = "mazesta-test", int keepFiles = 7, Func<DateTime>? now = null) : ILoggerProvider
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now); private readonly object _lock = new(); private StreamWriter? _writer; private string? _currentFile;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    internal void Write(LogLevel level, string category, string message, Exception? ex)
    {
        var t = _now(); string file = Path.Combine(directory, $"{prefix}-{t:yyyyMMdd}.log");
        lock (_lock)
        {
            if (file != _currentFile) { _writer?.Dispose(); Directory.CreateDirectory(directory); _writer = new StreamWriter(file, append: true, Encoding.UTF8); _currentFile = file; Prune(); }
            else if (_writer is null) { _writer = new StreamWriter(file, append: true, Encoding.UTF8); }
            _writer!.Write($"{t:HH:mm:ss.fff} {Abbrev(level)} {category} {message}"); if (ex is not null) _writer.Write($" | {ex}"); _writer.WriteLine();
        }
    }
    private void Prune()
    {
        foreach (var old in Directory.GetFiles(directory, $"{prefix}-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keepFiles))
            try { File.Delete(old); } catch (IOException) { }
    }
    private static string Abbrev(LogLevel l) => l switch { LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF", LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "???" };
    public void Flush() { lock (_lock) { _writer?.Flush(); _writer?.Dispose(); _writer = null; } }
    public void Dispose() { lock (_lock) { _writer?.Dispose(); _writer = null; } }
    private sealed class FileLogger(RollingFileLoggerProvider p, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => p.Write(logLevel, category, formatter(state, exception), exception);
    }
}
public static class LoggingSetup
{
    public static ILoggerFactory CreateFactory(string logsDir, LogLevel minimum = LogLevel.Information)
        => LoggerFactory.Create(b => { b.SetMinimumLevel(minimum); b.AddProvider(new RollingFileLoggerProvider(logsDir)); });
}
