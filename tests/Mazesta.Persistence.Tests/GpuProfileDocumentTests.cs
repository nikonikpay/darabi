using Mazesta.Core.Tuning; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions; using Xunit;
namespace Mazesta.Persistence.Tests;

public sealed class GpuProfileDocumentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private JsonStore<GpuProfileDocument> Store() => new(Path.Combine(_dir, "gpu-profiles.json"), new SchemaMigrator([]), GpuProfileDocument.CurrentSchemaVersion, NullLogger.Instance);

    [Fact] public void A_profile_keeps_its_settings_and_the_measurements_it_was_kept_on_and_a_missing_reading_stays_missing()
    {
        var baseline = new LoadMeasurement(1905, 340.5, 71, 74, 190.2, 0, false); var tuned = baseline with { AveragePowerW = 301, AverageTemperatureC = null };
        var doc = new GpuProfileDocument { Journal = new("GPU-A", new(90, 0, 1905, null, null), DateTimeOffset.UnixEpoch) };
        doc.Profiles.Add(new("uv", GpuProfileKind.Undervolt, "GPU-A", "RTX", new(135, 0, 1905, 320, null), DateTimeOffset.UnixEpoch, baseline, tuned));
        Assert.True(Store().Save(doc));
        var loaded = Store().Load().Value;
        Assert.Equal(doc.Profiles, loaded.Profiles); Assert.Equal(doc.Journal, loaded.Journal);
        Assert.Null(loaded.Profiles[0].Tuned!.AverageTemperatureC);
    }
}
