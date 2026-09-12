# Hardware matrix

Per-machine record of what Mazesta Test observes through
`LibreHardwareMonitorProvider` + `WmiInventoryProvider`. Only one machine has
been tested on real hardware so far; AMD paths are covered by unit tests on
fake hardware trees only (`SensorRoleMapTests`, `LhmHardwareMapperTests`) and
are **untested on hardware**.

## Dev box: Intel i9-14900K

| Component | Detail |
|---|---|
| CPU | Intel Core i9-14900K (8 P-cores + 16 E-cores, 32 threads) |
| GPU | NVIDIA GeForce RTX 4090 (discrete) + Intel UHD 770 (iGPU) |
| Motherboard | MSI Z790 GAMING PLUS WIFI |
| Storage | Samsung SSD 990 PRO 2 TB (NVMe) |
| RAM | 32 GB |
| OS | Windows 11 Pro, build 26200 |
| PawnIO | **Not installed** (`sc.exe query PawnIO` → service does not exist) |

### Hardware-category test results (elevated, `dotnet test tests/Mazesta.Hardware.Tests -c Release --filter Category=Hardware`)

Run 2026-09-12, elevated (see `docs/VERIFICATION-slice1.md` for how
elevation was obtained on this box), commit `34e0544`.

| Test | Result | Notes |
|---|---|---|
| `Provider_is_ready_or_explains_why` | Passed | Status is `Degraded`/`Provider.PawnIoMissing` given PawnIO's absence (deterministic from `LibreHardwareMonitorProvider.Start()` given elevated=true, PawnIO=false — see providers doc). |
| `Cpu_package_temperature_present_when_elevated_with_pawnio` | Passed (trivially) | Guard clause returns early because `PawnIo.IsInstalled == false`; the assertion body never runs. Cannot be exercised meaningfully on this box until PawnIO is installed. |
| `Nvidia_gpu_exposes_hot_spot_and_igpu_is_separate_node` | Passed | RTX 4090 exposes a `GpuHotSpotTemp`-rolled sensor; UHD 770 enumerates as its own `HardwareVendor.Intel` GPU node, confirming §3.1/§13-2 for this box. |
| `Nvme_node_id_matches_wmi_serial` | **Failed** | LHM storage id is `storage/S7DNNJ0X102517H`; the only WMI `MSFT_PhysicalDisk` serial available is `0025_3841_4140_5504.`. No `HardwareId.ForStorage(wmiSerial)` matches the LHM node. See "Concerns" below and `PROVIDERS-AND-FALLBACKS.md`. |
| `No_temperature_reports_zero_as_ok` | Passed | No temperature sensor reported `0 °C` as `Ok` in this poll. |

**4 of 5 passed.** The failure is a real data-source discrepancy on this
specific NVMe drive/Windows build, not a fix made to `src/` (none was made,
per this task's scope) — see Concerns.

### Mandatory sensors from spec §3.2, observed vs. missing vs. unsupported

| Sensor | Status on this box | Detail |
|---|---|---|
| CPU package temperature | Present (role mapped), **value unavailable** | `CpuPackageTemp` role exists on the CPU node; no `Ok` reading possible without PawnIO — shows «دریافت نشد». |
| CPU per-core temperature | Present (role), value unavailable | Same PawnIO dependency (`CpuCoreTemp`). |
| CPU core/effective clocks | Core clock role present; **Intel effective clock role has no LHM source** | LHM 0.9.6 exposes `Cores (Average)`→`CpuCoreClockAverage` and per-core clocks, but the "effective clock" (post-boost, accounting for throttling) role mapping (`CpuEffectiveClock`/`CpuEffectiveClockAverage`) is only ever populated from an AMD-specific sensor name (`Core #n (Effective)`) or the average-effective aggregate; Intel per-core **effective** clocks (as distinct from requested/bus-multiplied clocks) are **not exposed by LibreHardwareMonitor 0.9.6** for this CPU family. Not exposed by the provider — not a mapping gap. |
| Thermal-throttling flags | Not exposed | LibreHardwareMonitor 0.9.6 has no sensor for Intel RAPL/thermal throttle status bits (PROCHOT, PL1/PL2 throttling flags) on this CPU. **Not exposed by LibreHardwareMonitor 0.9.6.** |
| C-states | Not exposed | No C-state residency sensors in LHM 0.9.6's Intel CPU backend. **Not exposed by LibreHardwareMonitor 0.9.6.** |
| Vcore | Present (role), value unavailable without PawnIO | `CpuVcore` maps from `CPU Core`/`Core (SVI2 TFN)`. |
| Package power | Present (role), value unavailable without PawnIO | `CpuPackagePower`. |
| Per-thread load | Present, **available now** | `CpuThreadLoad` does not require PawnIO/MSR — CPU load is read from the OS scheduler, not MSRs, so per-thread load rows populate even in the current Degraded state. |
| RTX 4090 core temp / clocks / loads / power / fan | Present, available | NVIDIA sensors go through NVAPI/NVML, not PawnIO — unaffected by the PawnIO gap. |
| RTX 4090 hot spot temp | Present, available | Confirmed by the passing hardware test above. |
| RTX 4090 VRAM temperature | **Observed: mapped as `GpuVramTemp` via "GPU Memory Junction"** (NVIDIA-specific name) if the sensor is exposed by the installed driver; not independently re-verified with a screenshot in this pass — see Known gaps for the general "GPU without hot spot/VRAM sensor" fallback. |
| UHD 770 as its own group | Present, available | Confirmed by the passing hardware test (`HardwareVendor.Intel` GPU node distinct from the NVIDIA node). |
| RAM used/free/total + modules | Present, available | `RamUsed`/`RamFree`/`RamLoad` do not need PawnIO; module inventory comes from WMI (`Win32_PhysicalMemory`), independent of LHM. |
| Motherboard temps/fans/voltages | Present (roles), **value availability not independently re-verified with PawnIO absent** | MSI Z790's SuperIO chip (Nuvoton/ITE) is typically readable without PawnIO on many boards (direct port I/O), but was not screenshotted in this pass; treat as "present, verify at owner's next elevated session with PawnIO installed" for a fully confident number. |
| 990 PRO temperature + health sensors (slow cadence) | Present, available | `StorageTemp`/`StorageRemainingLife`/`StoragePowerOnHours` map from NVMe SMART data via LHM's storage backend, independent of PawnIO. Slow-cadence behaviour (900 s default) is exercised by `Mazesta.Monitoring` unit tests, not re-verified live in this pass. |
| Network adapter up/down throughput | Present (roles) | `NetUpload`/`NetDownload`/`NetUtilization`; not independently re-verified live in this pass. |

### HWiNFO cross-check (±2 °C, spec §13-3)

**PENDING OWNER.** Requires HWiNFO installed and a person at the physical
screen (screenshots cannot be produced from this automated session). Exact
steps to record the comparison:

1. Close HWiNFO, OCCT, AIDA64. Launch Mazesta Test (elevated), let it settle
   on the Dashboard, then Monitoring. Screenshot Dashboard
   (`artifacts/shots/dashboard.png`, already exists) and Monitoring
   (`artifacts/shots/monitoring.png`, already exists).
2. Close Mazesta Test. Open HWiNFO (Sensors-only mode is fine). Within one
   minute, screenshot HWiNFO's CPU package temperature, per-core
   temperatures, and GPU hot-spot temperature panels.
3. Compare each pair of readings; confirm they agree within ±2 °C at idle.
   Explain any difference: HWiNFO and LibreHardwareMonitor both read the
   same physical MSR/NVAPI values on Intel/NVIDIA hardware, so agreement is
   expected; a >2 °C gap most often means the two tools sampled at
   different moments while the CPU was not truly idle (turbo boost residue
   from closing the previous tool), not a reading error.
4. Record the two screenshots' file paths and the numeric pairs
   (package/core/hot-spot for Mazesta vs. HWiNFO) in this section.

**Package vs. core vs. hot-spot, explained** (for whoever fills in the
numbers above): *Package* temperature is a single Intel Digital Thermal
Sensor aggregate for the whole CPU die, typically tracking the hottest core
plus some controller overhead — it is usually the highest of the three.
*Core* temperatures are per-core DTS readings and can vary a few degrees
between cores under uneven load, and are typically at or just below package
under sustained load, but can spike above the package's reported value
instantaneously since package temperature has a slight reporting lag on
some silicon. *Hot spot* is GPU-specific (NVIDIA reports it via NVAPI on
Ada Lovelace/RTX 40-series): the single highest die-temperature location
across the GPU, always ≥ the average "GPU Core" temperature by design — a
gap of several degrees between GPU core and GPU hot spot is normal and
expected, not a discrepancy to reconcile with HWiNFO.

### Concerns (evidence, not source changes — per this task's scope)

1. **NVMe storage id does not join to `MSFT_PhysicalDisk.SerialNumber` on
   this box.** LHM's storage serial (`S7DNNJ0X102517H`, the label printed on
   the drive) and Windows' `MSFT_PhysicalDisk.SerialNumber`
   (`0025_3841_4140_5504.`, an NVMe EUI-64/NGUID-style id) are different
   strings for the same physical Samsung 990 PRO. Verified independently:
   `Get-CimInstance -Namespace root\Microsoft\Windows\Storage -ClassName
   MSFT_PhysicalDisk | Select FriendlyName,SerialNumber` returns the same
   `0025_3841_4140_5504.` value non-elevated. This means
   `Nvme_node_id_matches_wmi_serial` fails not because of a coding defect —
   `WmiInventoryParser.Disks` passes `SerialNumber` through unmodified — but
   because the two data sources genuinely disagree on what "the serial" is
   for this drive/Windows build. Any later feature that joins storage
   inventory to storage sensor nodes by serial (spec §5.3) should be aware
   this join can silently fail on some NVMe drives. Not fixed here per this
   task's "do not modify `src/`" scope; flagged for slice/task owner
   decision (e.g., fall back to matching by `FriendlyName` + index, or strip
   the WMI value's `EUI.`/dot-separated hex form and compare against the
   NVMe identify page directly).
