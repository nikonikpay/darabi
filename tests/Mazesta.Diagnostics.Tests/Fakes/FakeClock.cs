using Mazesta.Core.Time;
namespace Mazesta.Diagnostics.Tests.Fakes;
public sealed class FakeClock(DateTimeOffset start) : IClock { public DateTimeOffset UtcNow { get; set; } = start; public void Advance(TimeSpan t) => UtcNow += t; }
