using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Core.Providers; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Mazesta.Hardware.Tests;

/// <summary>
/// One LibreHardwareMonitor Computer for the whole class. LHM's storage backend does not survive
/// repeated open/close cycles in one process: with a provider per test (five opens and closes) the
/// last run enumerated zero storage nodes, so the storage tests were silently testing nothing.
/// An IClassFixture opens it once, polls on demand, and closes it once at the end.
/// </summary>
public sealed class DevBoxFixture : IDisposable
{
    private readonly LhmComputerAdapter _computer = new();
    public LibreHardwareMonitorProvider Provider { get; }
    public PollResult Last { get; private set; } = PollResult.Empty;
    public int Polls { get; private set; }

    public DevBoxFixture()
    {
        Provider = new LibreHardwareMonitorProvider(_computer, () => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled, IsElevated,
            hw => (hw as LibreHardwareMonitor.Hardware.Storage.StorageDevice)?.Storage?.SerialNumber, new SystemClock(),
            NullLogger<LibreHardwareMonitorProvider>.Instance);
        Provider.Start();
        Poll();
    }

    /// <summary>The raw LHM tree behind the provider, for assertions the Mazesta model does not expose.</summary>
    public IEnumerable<ISensor> AllRawSensors()
    {
        IEnumerable<ISensor> Walk(IEnumerable<IHardware> hw) => hw.SelectMany(h => h.Sensors.Concat(Walk(h.SubHardware)));
        return Walk(_computer.Hardware);
    }

    public PollResult Poll()
    {
        Polls++;
        return Last = Provider.Poll(new PollRequest(DateTimeOffset.UtcNow, Provider.Hardware.Select(h => h.Id).ToHashSet()));
    }

    public static bool IsElevated() { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
    public void Dispose() => Provider.Dispose();
}

[Trait("Category", "Hardware")]
public class DevBoxHardwareTests(DevBoxFixture fx) : IClassFixture<DevBoxFixture>
{
    [Fact] public void Provider_is_ready_or_explains_why()
    {
        Assert.NotEqual(ProviderState.Failed, fx.Provider.Status.State);
        if (fx.Provider.Status.State == ProviderState.Degraded)
            Assert.Contains(fx.Provider.Status.ReasonKey, new[] { LibreHardwareMonitorProvider.ReasonPawnIoMissing, LibreHardwareMonitorProvider.ReasonNotElevated });
    }

    [Fact] public void Cpu_package_temperature_present_when_elevated_with_pawnio()
    {
        if (!DevBoxFixture.IsElevated() || !LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) return;   // documented prerequisite; the status test covers the reason
        var def = fx.Provider.Hardware.SelectMany(h => h.Sensors).First(s => s.Role == SensorRole.CpuPackageTemp);
        var reading = fx.Last.Readings.Single(x => x.Id == def.Id);
        Assert.Equal(DataQuality.Ok, reading.Quality); Assert.InRange(reading.Value!.Value, 15, 110);
    }

    [Fact] public void Nvidia_gpu_exposes_hot_spot_and_igpu_is_separate_node()
    {
        var gpus = fx.Provider.Hardware.Where(h => h.Kind == HardwareKind.Gpu).ToList();
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Nvidia && g.Sensors.Any(s => s.Role == SensorRole.GpuHotSpotTemp));
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Intel);
    }

    [Fact] public void Storage_nodes_exist()
        => Assert.Contains(fx.Provider.Hardware, h => h.Kind == HardwareKind.Storage);

    [Fact] public async Task Storage_node_joins_wmi_by_serial_or_model()
    {
        // On Windows 11 26200 both Win32_DiskDrive and MSFT_PhysicalDisk report an NVMe device's
        // NGUID (0025_3841_4140_5504.) while LHM reports the vendor serial (S7DNNJ0X102517H), so a
        // serial-only join cannot succeed for NVMe on this OS. The join therefore accepts either a
        // serial match or an exact (normalised) model-name match against FriendlyName.
        var inv = await new WmiInventoryProvider(new WmiQuery(), NullLogger<WmiInventoryProvider>.Instance).ReadAsync(CancellationToken.None);
        var nodes = fx.Provider.Hardware.Where(h => h.Kind == HardwareKind.Storage).ToList();
        Assert.NotEmpty(nodes);
        Assert.NotEmpty(inv.Storage);
        static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
        Assert.Contains(nodes, node => inv.Storage.Any(d =>
            (d.SerialNumber is { } sn && sn.Trim().Length > 0 && node.Id == HardwareId.ForStorage(sn.Trim()))
            || Norm(d.FriendlyName) == Norm(node.Name)));
    }

    [Fact] public void No_temperature_reports_zero_as_ok()
    {
        var temps = fx.Provider.Hardware.SelectMany(h => h.Sensors).Where(s => s.Kind == SensorKind.Temperature).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(fx.Last.Readings, x => temps.Contains(x.Id) && x.Quality == DataQuality.Ok && x.Value == 0);
    }

    [Fact] public void No_power_reports_zero_as_ok()
    {
        var power = fx.Provider.Hardware.SelectMany(h => h.Sensors).Where(s => s.Kind is SensorKind.Power or SensorKind.Current or SensorKind.Energy).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(fx.Last.Readings, x => power.Contains(x.Id) && x.Quality == DataQuality.Ok && x.Value == 0);
    }

    [Fact] public void Lhm_keeps_no_per_sensor_value_history()
    {
        // Start() zeroes ISensor.ValuesTimeWindow, so LHM's own day-long history never accumulates -
        // this app's HistoryStore owns history. Poll a few times, then check the raw sensors.
        fx.Poll(); fx.Poll(); fx.Poll();
        var sampled = fx.AllRawSensors().Where(s => s.Value is not null).Take(25).ToList();
        Assert.NotEmpty(sampled);
        Assert.All(sampled, s => Assert.Equal(TimeSpan.Zero, s.ValuesTimeWindow));
        Assert.All(sampled, s => Assert.Empty(s.Values));
    }
}
