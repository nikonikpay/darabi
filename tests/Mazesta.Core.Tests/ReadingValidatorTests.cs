using Mazesta.Core.Hardware;
using Xunit;
namespace Mazesta.Core.Tests;
public class ReadingValidatorTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, -5.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, 151.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, 45.0, DataQuality.Ok)]
    [InlineData(SensorKind.Clock, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Clock, 4800.0, DataQuality.Ok)]
    [InlineData(SensorKind.Load, 100.0, DataQuality.Ok)]
    [InlineData(SensorKind.Load, 100.5, DataQuality.Invalid)]
    [InlineData(SensorKind.Voltage, 12.1, DataQuality.Ok)]
    [InlineData(SensorKind.Voltage, 21.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Fan, 0.0, DataQuality.Ok)]
    [InlineData(SensorKind.Power, -1.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Power, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Power, 65.5, DataQuality.Ok)]
    [InlineData(SensorKind.Current, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Current, 1.5, DataQuality.Ok)]
    [InlineData(SensorKind.Energy, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Energy, 1234.0, DataQuality.Ok)]
    [InlineData(SensorKind.Throughput, 0.0, DataQuality.Ok)]
    [InlineData(SensorKind.Data, 0.0, DataQuality.Ok)]
    [InlineData(SensorKind.SmallData, 0.0, DataQuality.Ok)]
    [InlineData(SensorKind.Timing, -3.0, DataQuality.Ok)]
    public void Ranges(SensorKind kind, double value, DataQuality expected) => Assert.Equal(expected, ReadingValidator.Validate(kind, value));
    [Fact] public void Null_is_missing() => Assert.Equal(DataQuality.Missing, ReadingValidator.Validate(SensorKind.Temperature, null));
    [Fact] public void NaN_and_infinity_are_invalid() { Assert.Equal(DataQuality.Invalid, ReadingValidator.Validate(SensorKind.Fan, double.NaN)); Assert.Equal(DataQuality.Invalid, ReadingValidator.Validate(SensorKind.Fan, double.PositiveInfinity)); }
    [Fact] public void ProviderStatus_factories()
    {
        Assert.Equal(ProviderState.Ready, ProviderStatus.Ready(12).State);
        var d = ProviderStatus.Degraded("Provider.PawnIoMissing", "not installed", 3);
        Assert.Equal((ProviderState.Degraded, 3, "Provider.PawnIoMissing"), (d.State, d.SensorCount, d.ReasonKey));
    }
}
