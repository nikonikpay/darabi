namespace Mazesta.Diagnostics;

public enum RepeatMode { Once, Count, Unlimited }

/// <summary>One user-configured queue entry. RepeatCount is read only when Repeat == Count; a zero or
/// negative DurationSeconds is invalid input (spec §8 "مقدار صفر یا منفی نامعتبر است") and is rejected
/// by the executor as Unsupported rather than silently clamped.</summary>
public sealed record QueuedTest(TestDefinition Definition, int DurationSeconds, RepeatMode Repeat, int RepeatCount)
{
    public static QueuedTest Once(TestDefinition definition, int durationSeconds) => new(definition, durationSeconds, RepeatMode.Once, 1);
}
