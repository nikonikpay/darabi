using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Core.Providers; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace Mazesta.Hardware.Tests;
[Trait("Category", "Hardware")]
public class DevBoxHardwareTests : IDisposable
{
    private readonly LibreHardwareMonitorProvider _p = LibreHardwareMonitorProvider.CreateDefault(new SystemClock(), NullLoggerFactory.Instance);
    private PollResult PollAll() { _p.Start(); return _p.Poll(new PollRequest(DateTimeOffset.UtcNow, _p.Hardware.Select(h => h.Id).ToHashSet())); }
    public void Dispose() => _p.Dispose();
    private static bool Elevated() { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
    [Fact] public void Provider_is_ready_or_explains_why()
    { PollAll(); Assert.NotEqual(ProviderState.Failed, _p.Status.State); if (_p.Status.State == ProviderState.Degraded) Assert.Contains(_p.Status.ReasonKey, new[] { LibreHardwareMonitorProvider.ReasonPawnIoMissing, LibreHardwareMonitorProvider.ReasonNotElevated }); }
    [Fact] public void Cpu_package_temperature_present_when_elevated_with_pawnio()
    {
        var r = PollAll(); if (!Elevated() || !LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) return;   // documented prerequisite; the status test covers the reason
        var def = _p.Hardware.SelectMany(h => h.Sensors).First(s => s.Role == SensorRole.CpuPackageTemp);
        var reading = r.Readings.Single(x => x.Id == def.Id); Assert.Equal(DataQuality.Ok, reading.Quality); Assert.InRange(reading.Value!.Value, 15, 110);
    }
    [Fact] public void Nvidia_gpu_exposes_hot_spot_and_igpu_is_separate_node()
    {
        PollAll(); var gpus = _p.Hardware.Where(h => h.Kind == HardwareKind.Gpu).ToList();
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Nvidia && g.Sensors.Any(s => s.Role == SensorRole.GpuHotSpotTemp));
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Intel);
    }
    [Fact] public async Task Nvme_node_id_matches_wmi_serial()
    {
        PollAll(); var inv = await new WmiInventoryProvider(new WmiQuery(), NullLogger<WmiInventoryProvider>.Instance).ReadAsync(CancellationToken.None);
        var serials = inv.Storage.Select(d => d.SerialNumber?.Trim()).Where(s => s is not null).ToList();
        Assert.Contains(_p.Hardware.Where(h => h.Kind == HardwareKind.Storage), h => serials.Any(s => h.Id == HardwareId.ForStorage(s!)));
    }
    [Fact] public void No_temperature_reports_zero_as_ok()
    { var r = PollAll(); var temps = _p.Hardware.SelectMany(h => h.Sensors).Where(s => s.Kind == SensorKind.Temperature).Select(s => s.Id).ToHashSet(); Assert.DoesNotContain(r.Readings, x => temps.Contains(x.Id) && x.Quality == DataQuality.Ok && x.Value == 0); }
}
