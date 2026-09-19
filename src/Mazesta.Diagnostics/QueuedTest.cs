namespace Mazesta.Diagnostics;

public enum RepeatMode { Once, Count, Unlimited }

/// <summary>One user-configured queue entry. RepeatCount is read only when Repeat == Count.</summary>
public sealed record QueuedTest(TestDefinition Definition, int DurationSeconds, RepeatMode Repeat, int RepeatCount, IReadOnlyDictionary<string, string>? Options = null);
