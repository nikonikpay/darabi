# Providers and fallbacks

Mazesta Test reads hardware sensors through `LibreHardwareMonitorProvider`
(`Mazesta.Hardware.Lhm`, wrapping LibreHardwareMonitorLib 0.9.6) and reads
static inventory through `WmiInventoryProvider` (`Mazesta.Hardware.Wmi`,
`System.Management`). Neither provider ever throws past its public surface;
every failure becomes a `ProviderStatus` or a null inventory section, never a
fabricated value (spec §3.1, §9).

## Fallback summary (spec §5.4)

| Situation | Behaviour |
|---|---|
| LHM `Open()` throws | Provider `Failed`; monitoring shows a banner with the reason; dashboard still shows WMI inventory. |
| PawnIO not installed / not loaded | Provider `Degraded`; CPU temps/clocks from MSR missing; banner explains and links to the installer; no estimates. |
| A hardware node throws on update | Only that node's sensors go Stale; others unaffected; retried next due tick. |
| GPU has no Hot Spot / VRAM temp sensor | Role absent; dashboard Hot Spot card reads «دریافت نشد». |
| WMI query fails | That inventory section is null and displayed as not available. |
| Poll takes longer than the interval | Tick is skipped, an overrun event is logged (rate-limited to once per minute), next poll starts immediately. |

## Provider status decision table

`LibreHardwareMonitorProvider.Start()` (`src/Mazesta.Hardware/Lhm/LibreHardwareMonitorProvider.cs`)
computes status in this order, after `Computer.Open()` succeeds and hardware
is enumerated:

| Condition | Status | Reason key |
|---|---|---|
| `Computer.Open()` throws, or hardware enumeration throws | `Failed` | `Provider.OpenFailed` |
| Process is not elevated | `Degraded` | `Provider.NotElevated` |
| Elevated, but PawnIO driver not installed/loaded | `Degraded` | `Provider.PawnIoMissing` |
| Elevated, PawnIO present, but zero sensors enumerated | `Degraded` | `Provider.NoHardware` |
| Elevated, PawnIO present, sensors enumerated | `Ready` | — |

Reason keys are localisation keys resolved through `Loc.Get`, e.g.
`Strings.fa.resx` maps `Provider.PawnIoMissing` to:
> «درایور PawnIO نصب نیست. دمای و فرکانس CPU در دسترس نیست. PawnIO را از صفحه رسمی آن نصب کنید و برنامه را دوباره اجرا کنید.»

and `Provider.NotElevated` to:
> «برنامه با دسترسی Administrator اجرا نشده است. سنسورهای CPU و مادربرد در دسترس نیستند.»

## PawnIO detection

`LibreHardwareMonitorProvider.CreateDefault` wires the PawnIO check to
`LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled` — the library's own public
API (0.9.6 does expose this; no `ServiceController` fallback was needed, one
risk noted in the slice design (§14) that did not materialise). Elevation is
checked independently with `WindowsIdentity`/`WindowsPrincipal` against
`WindowsBuiltInRole.Administrator`, because a process can be elevated with
PawnIO still absent, or unelevated with PawnIO present but unusable (MSR
access requires both).

Observed on the dev box while writing this document: `sc.exe query PawnIO`
returns *"The specified service does not exist as an installed service"* —
PawnIO is **not currently installed** on this machine. This makes the
"PawnIO absent" fallback the box's actual current state rather than a
simulated one; see `docs/VERIFICATION-slice1.md` for how this was verified
against a live elevated run.

## Manifests and elevation

| Configuration | Manifest file | `requestedExecutionLevel` | Effect |
|---|---|---|---|
| Debug | `src/Mazesta.Desktop/app.debug.manifest` | `asInvoker` | Runs at the caller's integrity; lets a developer iterate without a UAC prompt on every F5. The provider still reports its honest `NotElevated`/`PawnIoMissing`/`Ready` status — nothing is faked because the build is Debug. |
| Release | `src/Mazesta.Desktop/app.manifest` | `requireAdministrator` | Windows elevates on launch (UAC, or silently if local policy auto-approves for the account — observed on this dev box, see below). This is the shape used for `artifacts/Mazesta-Test/MazestaTest.exe`. |

Both manifests set `dpiAwareness=PerMonitorV2` and `longPathAware=true`
identically; only the execution level differs.

**Dev-box elevation note:** on this machine, a non-elevated caller invoking
`Start-Process` against the Release exe (whose manifest requires
administrator) resulted in an actually-elevated process with no visible UAC
dialog and no prompt-wait — confirmed because a later `Stop-Process` from the
same non-elevated shell failed with *Access is denied* (a Medium-integrity
caller cannot terminate a High-integrity process it did not spawn with an
elevated token; termination succeeded once done through another elevated
`Start-Process -Verb RunAs`). This means the local UAC policy silently
auto-elevates for the signed-in account, and a genuinely non-elevated sample
of the **Release** binary's behaviour cannot be produced by launch flags
alone on this box — only the Debug build (`asInvoker`) demonstrates the
`NotElevated` status without elevation. This is an environment fact about
the dev box's UAC configuration, not a defect in the app.

## SensorRoleMap — LHM sensor name → role table

Copied verbatim from `src/Mazesta.Hardware/Lhm/SensorRoleMap.cs`. Unlisted
`(HardwareType, SensorType, name)` combinations resolve to `SensorRole.None`
(the sensor is still shown in Monitoring under its raw name; it just isn't
addressable by role for dashboard cards or future alert rules).

### CPU (`HardwareType.Cpu`)

| Sensor type | Exact LHM name(s) | Role |
|---|---|---|
| Temperature | `CPU Package` | `CpuPackageTemp` |
| Temperature | `Core (Tctl/Tdie)`, `Core (Tdie)`, `Core (Tctl)` | `CpuTctlTdie` |
| Temperature | `CCD{n} (Tdie)` | `CpuCcdTemp` |
| Temperature | `CPU Core #n`, `P-Core #n`, `E-Core #n` | `CpuCoreTemp` |
| Clock | `Bus Speed` | `CpuBusClock` |
| Clock | `Cores (Average Effective)` | `CpuEffectiveClockAverage` |
| Clock | `Cores (Average)` | `CpuCoreClockAverage` |
| Clock | `Core #n (Effective)` (AMD) | `CpuEffectiveClock` |
| Clock | `CPU Core #n` / `P-Core #n` / `E-Core #n` (Intel), `Core #n` (AMD) | `CpuCoreClock` |
| Voltage | `CPU Core`, `Core (SVI2 TFN)` | `CpuVcore` |
| Power | `CPU Package`, `Package` | `CpuPackagePower` |
| Power | `CPU Cores` | `CpuCorePower` |
| Load | `CPU Total` | `CpuTotalLoad` |
| Load | `CPU Core #n[ Thread #m]`, `P-Core #n[ Thread #m]`, `E-Core #n[ Thread #m]` | `CpuThreadLoad` |

### GPU (`HardwareType.GpuNvidia`/`GpuAmd`/`GpuIntel`)

| Sensor type | Exact LHM name(s) | Role |
|---|---|---|
| Temperature | `GPU Core` | `GpuCoreTemp` |
| Temperature | `GPU Hot Spot` | `GpuHotSpotTemp` |
| Temperature | `GPU Memory Junction`, or `GPU Memory` on non-NVIDIA | `GpuVramTemp` |
| Clock | `GPU Core` | `GpuCoreClock` |
| Clock | `GPU Memory` | `GpuMemoryClock` |
| Load | `GPU Core` | `GpuLoad3D` |
| Load | `D3D 3D` | `GpuLoadD3D3D` |
| Load | `D3D Compute*` (regex, unanchored — see known gaps) | `GpuLoadCompute` |
| Load | `GPU Video Engine`, `GPU Media` | `GpuLoadVideo` |
| Load | `GPU Memory Controller` | `GpuLoadMemoryController` |
| Power | `GPU Package`, `GPU Power` | `GpuPower` |
| Voltage | `GPU Core Voltage`, `GPU Core` | `GpuVoltage` |
| Fan (any) | — | `GpuFanRpm` |
| Control | `GPU Fan` | `GpuFanPercent` |
| SmallData | `GPU Memory Total` | `GpuVramTotal` |
| SmallData | `GPU Memory Used` | `GpuVramUsed` |
| SmallData | `GPU Memory Free` | `GpuVramFree` |

Note: any `SensorType.Fan` sensor on a GPU node maps to `GpuFanRpm` — a card
with two physical fan sensors has both mapped to the same role (see known
gaps in `VERIFICATION-slice1.md`). VRAM-as-a-`Memory`-hardware-type sub-node
(`/vram` identifier) is excluded from the generic Memory mapping so it is not
double counted as system RAM.

### Memory (`HardwareType.Memory`)

| Sensor type | Exact LHM name | Role |
|---|---|---|
| Load | `Memory` | `RamLoad` |
| Data | `Memory Used` | `RamUsed` |
| Data | `Memory Available` | `RamFree` |
| Temperature | `DIMM #n` | `DimmTemp` |

### Motherboard / SuperIO / EmbeddedController

| Sensor type | Exact LHM name | Role |
|---|---|---|
| Temperature | `Chipset`, `PCH` | `ChipsetTemp` |
| Temperature | anything else | `BoardTemp` |
| Fan | starts with `CPU Fan` | `CpuFan` |
| Fan | anything else | `BoardFan` |
| Voltage | one of `Vcore`, `Vcore SoC`, `+12V`, `+5V`, `+3.3V`, `+3V Standby`, `AVCC`, `3VCC`, `VBat`, `DIMM`, `CPU Termination`, `CPU System Agent`, `VTT`, `VRM`, `CPU Core` | `BoardVoltage` |

As with GPU fans, any board/CPU fan sensor collapses onto `CpuFan` or
`BoardFan` — a board exposing multiple fan headers under the same category
cannot be told apart by role alone (documented known gap).

### Storage (`HardwareType.Storage`)

| Sensor type | Exact LHM name | Role |
|---|---|---|
| Temperature | `Temperature`, `Composite Temperature` | `StorageTemp` |
| Level | `Life` | `StorageRemainingLife` |
| Factor | `Power On Hours` | `StoragePowerOnHours` |
| Load | `Used Space` | `StorageUsedSpace` |
| Throughput | `Read Rate` | `StorageReadRate` |
| Throughput | `Write Rate` | `StorageWriteRate` |

### Network (`HardwareType.Network`)

| Sensor type | Exact LHM name | Role |
|---|---|---|
| Throughput | `Upload Speed` | `NetUpload` |
| Throughput | `Download Speed` | `NetDownload` |
| Load | `Network Utilization` | `NetUtilization` |

## Storage node identity vs. WMI inventory

`LhmHardwareMapper` builds the storage `HardwareId` from the serial LHM
reads off the device's SMART/identify data
(`HardwareId.ForStorage(serial)` → `storage/<serial, spaces joined with _>`).
`WmiInventoryProvider` reads `MSFT_PhysicalDisk.SerialNumber` for the same
join (spec §5.3). On this dev box these two serials **do not match** for the
Samsung 990 PRO NVMe: LHM reports `S7DNNJ0X102517H` (the manufacturer serial
printed on the drive), while `MSFT_PhysicalDisk.SerialNumber` reports
`0025_3841_4140_5504.` (an NVMe EUI-64/NGUID-style identifier that Windows'
storage stack exposes for many NVMe drives instead of the vendor serial).
This is a real, reproducible discrepancy on this hardware — not a parsing
bug in `WmiInventoryParser.Disks` (it passes the WMI field through
unmodified) — and is recorded as a concern in `VERIFICATION-slice1.md`
because it means the storage-to-inventory join by serial silently fails for
this drive today.
