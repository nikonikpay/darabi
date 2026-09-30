using System.Globalization; using System.Runtime.InteropServices; using Mazesta.Core.Inventory; using Mazesta.Core.Providers; using Mazesta.Hardware.Nvidia; using Mazesta.Hardware.Wmi;
using Microsoft.Extensions.Logging; using Microsoft.Win32;
namespace Mazesta.Hardware.Details;

public interface IHardwareDetailsProvider { Task<HardwareDetails> ReadAsync(HardwareInventory inventory, CancellationToken ct); }

/// <summary>
/// Reads the specification pages' details: each part on its own, so one that fails (no NVIDIA driver, no SMBus, a WMI class this Windows lacks)
/// leaves the others and adds its reason to Errors. Every value comes from Windows, the part's driver or the part itself; nothing is looked up by model.
/// </summary>
public sealed class HardwareDetailsReader(IWmiQuery query, IDriveHealthProvider drives, ILogger<HardwareDetailsReader> logger) : IHardwareDetailsProvider
{
    private const string Cimv2 = @"root\cimv2";

    public Task<HardwareDetails> ReadAsync(HardwareInventory inv, CancellationToken ct) => Task.Run(() =>
    {
        var errors = new List<string>();
        T Part<T>(string name, Func<T> read, T fallback)
        {
            ct.ThrowIfCancellationRequested();
            try { return read(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Details {Part} failed", name); errors.Add($"{name}: {ex.Message}"); return fallback; }
        }
        return new HardwareDetails(
            Part<CpuDetails?>("cpu", Cpu, null), Part("gpu", () => Gpus(inv), (IReadOnlyList<GpuDetails>)[]), Part("spd", SpdReader.Read, (IReadOnlyList<SpdModule>)[]),
            Part<PlatformSecurity?>("security", Security, null), Part("drives", () => Drives(inv), (IReadOnlyList<DriveDetails>)[]), Part("network", Nics, (IReadOnlyList<NicDetails>)[]),
            errors);
    }, ct);

    private CpuDetails Cpu()
    {
        var r = query.Query(Cimv2, "SELECT MaxClockSpeed,ExtClock,VirtualizationFirmwareEnabled,SecondLevelAddressTranslationExtensions FROM Win32_Processor").FirstOrDefault();
        return CpuDetailsReader.Read(Int(r, "MaxClockSpeed"), Int(r, "ExtClock"), Bool(r, "VirtualizationFirmwareEnabled"), Bool(r, "SecondLevelAddressTranslationExtensions"));
    }

    /// <summary>Each inventory GPU with its link and board maker; an NVIDIA card also with what NVML says, paired by PCI bus (or, with one card each, directly).</summary>
    private static IReadOnlyList<GpuDetails> Gpus(HardwareInventory inv)
    {
        var cards = NvmlDetails.Read();
        var nvidia = inv.Gpus.Where(g => g.PnpDeviceId?.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) == true).ToList();
        return [.. inv.Gpus.Select(g =>
        {
            var sub = PciVendors.Subsystem(g.PnpDeviceId);
            int? bus = PciDevice.BusNumber(g.PnpDeviceId);
            var card = cards.FirstOrDefault(c => bus is not null && c.PciBus == bus) ?? (cards.Count == 1 && nvidia.Count == 1 && nvidia[0] == g ? cards[0] : null);
            var link = PciDevice.Link(g.PnpDeviceId) ?? (card is null ? null : new PciLinkInfo(card.CurrentGen, card.CurrentWidth, card.MaxGen, card.MaxWidth));
            return new GpuDetails(g.PnpDeviceId, sub is { } s ? PciVendors.Name(s.Vendor) : null, sub is { } x ? $"{x.Vendor:X4}:{x.Device:X4}" : null, link, card?.Vbios, card?.BusWidthBits,
                card?.Cores, card?.Architecture, card?.ComputeCapability, card?.Bar1Bytes, card?.MaxCoreClockMhz, card?.MaxMemoryClockMhz, card?.PowerDefaultW, card?.PowerMaxW, card?.PciBusId);
        })];
    }

    private PlatformSecurity Security()
    {
        bool? uefi = GetFirmwareType(out uint type) ? type == 2 : null;   // FirmwareTypeUefi
        bool? secure = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", null) is int v ? v == 1 : null;
        string? tpmVersion = null; bool? tpmEnabled = null;
        try
        {
            var tpm = query.Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT SpecVersion,IsEnabled_InitialValue FROM Win32_Tpm").FirstOrDefault();
            tpmVersion = Str(tpm, "SpecVersion")?.Split(',')[0].Trim(); tpmEnabled = tpm is null ? false : Bool(tpm, "IsEnabled_InitialValue");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogInformation(ex, "TPM not readable"); }
        bool? vbs = null, hvci = null;
        try
        {
            var dg = query.Query(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus,SecurityServicesRunning FROM Win32_DeviceGuard").FirstOrDefault();
            if (dg is not null)
            {
                vbs = Int(dg, "VirtualizationBasedSecurityStatus") is { } st ? st == 2 : null;
                hvci = dg.TryGetValue("SecurityServicesRunning", out var running) && running is Array a ? a.Cast<object>().Any(x => Convert.ToInt32(x, CultureInfo.InvariantCulture) == 2) : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogInformation(ex, "Device Guard not readable"); }
        return new(uefi, secure, tpmVersion, tpmEnabled, vbs, hvci);
    }

    /// <summary>Each drive's SMART counters, and an NVMe drive's PCIe link: the link of the PCI controller its disk device hangs from.</summary>
    private IReadOnlyList<DriveDetails> Drives(HardwareInventory inv)
    {
        var health = drives.Read();
        var disks = query.Query(Cimv2, "SELECT Index,Model,SerialNumber,PNPDeviceID FROM Win32_DiskDrive");
        return [.. inv.Storage.Select(d =>
        {
            var h = health.FirstOrDefault(x => x.Serial is not null && x.Serial == d.SerialNumber) ?? health.FirstOrDefault(x => x.Name == d.FriendlyName);
            PciLinkInfo? link = null; NvmeHealthLog? log = null;
            if (d.BusType == "NVMe")
            {
                var disk = disks.FirstOrDefault(r => Str(r, "SerialNumber") is { } s && d.SerialNumber is { } n && Norm(s) == Norm(n)) ?? disks.FirstOrDefault(r => Str(r, "Model") == d.FriendlyName);
                link = PciDevice.Link(PciDevice.Ancestor(Str(disk, "PNPDeviceID"), id => id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)));
                if (disk?.GetValueOrDefault("Index") is { } index) log = NvmeHealthReader.ReadLog(Convert.ToInt32(index, System.Globalization.CultureInfo.InvariantCulture));
            }
            return new DriveDetails(d.SerialNumber, d.FriendlyName, link, h?.PowerOnHours, h?.TemperatureC, h?.TemperatureMaxC, h?.ReadErrorsUncorrected, h?.WriteErrorsUncorrected, h?.WearPercent, log);
        })];
    }

    private IReadOnlyList<NicDetails> Nics()
    {
        var drivers = query.Query(Cimv2, "SELECT DeviceID,DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass='NET'");
        return [.. query.Query(Cimv2, "SELECT Name,PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE").Select(a =>
        {
            string? id = Str(a, "PNPDeviceID");
            string? driver = Str(drivers.FirstOrDefault(d => string.Equals(Str(d, "DeviceID"), id, StringComparison.OrdinalIgnoreCase)), "DriverVersion");
            return new NicDetails(Str(a, "Name"), id?.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) == true ? PciDevice.Link(id) : null, driver);
        })];
    }

    private static string Norm(string s) => s.Replace(" ", "").Replace("_", "").Replace(".", "").ToUpperInvariant();
    private static string? Str(IReadOnlyDictionary<string, object?>? r, string k) => r is not null && r.TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } s ? s : null : null;
    private static int? Int(IReadOnlyDictionary<string, object?>? r, string k) => r is not null && r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;
    private static bool? Bool(IReadOnlyDictionary<string, object?>? r, string k) => r is not null && r.TryGetValue(k, out var v) && v is bool b ? b : null;

    [DllImport("kernel32.dll")] private static extern bool GetFirmwareType(out uint type);
}
