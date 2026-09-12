using Mazesta.Core.Time; using Microsoft.Extensions.Logging;
namespace Mazesta.Monitoring;
public enum EventLevel { Info, Warning, Error }
public readonly record struct EventEntry(DateTimeOffset At, EventLevel Level, string Key, string Detail);
public interface IEventLog { void Log(EventLevel level, string key, string detail); IReadOnlyList<EventEntry> Snapshot(); event Action<EventEntry>? Logged; }
public sealed class BoundedEventLog(IClock clock, ILogger logger, int capacity = 1000) : IEventLog
{
    private readonly Queue<EventEntry> _q = new(capacity); private readonly object _lock = new();
    public event Action<EventEntry>? Logged;
    public void Log(EventLevel level, string key, string detail)
    {
        var e = new EventEntry(clock.UtcNow, level, key, detail);
        lock (_lock) { if (_q.Count == capacity) _q.Dequeue(); _q.Enqueue(e); }
        logger.Log(level switch { EventLevel.Error => LogLevel.Error, EventLevel.Warning => LogLevel.Warning, _ => LogLevel.Information }, "{Key}: {Detail}", key, detail);
        Logged?.Invoke(e);
    }
    public IReadOnlyList<EventEntry> Snapshot() { lock (_lock) return _q.ToList(); }
}
