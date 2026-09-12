// tests/Mazesta.Core.Tests/UnitsTests.cs
using Mazesta.Core.Hardware;
using Xunit;
namespace Mazesta.Core.Tests;
public class UnitsTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, Unit.Celsius)] [InlineData(SensorKind.Clock, Unit.MegaHertz)]
    [InlineData(SensorKind.Load, Unit.Percent)] [InlineData(SensorKind.SmallData, Unit.Megabyte)]
    [InlineData(SensorKind.Throughput, Unit.BytesPerSecond)] [InlineData(SensorKind.Timing, Unit.Nanoseconds)]
    public void Kind_maps_to_unit(SensorKind kind, Unit unit) => Assert.Equal(unit, Units.ForKind(kind));
    [Fact] public void Format_uses_unit_precision() { Assert.Equal("45.5", Units.Format(45.49, Unit.Celsius)); Assert.Equal("5200", Units.Format(5200.4, Unit.MegaHertz)); Assert.Equal("1.234", Units.Format(1.2341, Unit.Volt)); }
    [Fact] public void Throughput_scales_to_readable_prefix() { Assert.Equal("12.5 MB/s", Units.FormatWithSymbol(12_500_000, Unit.BytesPerSecond)); Assert.Equal("800 B/s", Units.FormatWithSymbol(800, Unit.BytesPerSecond)); }
}
