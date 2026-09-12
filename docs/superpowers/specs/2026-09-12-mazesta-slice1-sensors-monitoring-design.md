# Mazesta Test — Slice 1 Design: Solution Skeleton + Sensors & Monitoring

**Date:** 2026-09-12
**Status:** Approved by owner (design discussion), pending written-spec review
**Source spec:** «سند جامع ساخت سیستم تست مازستا» v2.0 (10 Sept 2026). This document covers only the first sub-project of Phase 1. Section references (§) point to that spec.

---

## 1. Context and decisions already made

- The product is **Mazesta Test** (سیستم تست مازستا), a Windows x64 diagnostics, stress-test and reporting tool for a PC service shop. Phase 1 alone spans ~12 subsystems, so Phase 1 is delivered as ordered **slices**, each with its own spec and plan. Slice order follows §19: sensors/monitoring → test engine → specialised tests → alert rules → reporting → tray/Monitor edition → recovery/polish.
- **Build from scratch.** The v0.5 prototype exists only as a compiled zip; its source is not available. Its own verification notes list unmet goals (207 MiB idle vs 80 MB target, sampled VRAM test, matrix-only "Linpack", unfinished tray). The spec allows a fresh start (§0).
- **Runtime: .NET 10 LTS**, not .NET 8. .NET 8 leaves support in November 2026. SDK 10.0.400 and the 10.0.11 Desktop runtime are installed on the dev machine; `dotnet.exe` is invoked from WSL.
- **Approach A** (single process, layered libraries, LibreHardwareMonitor behind our own provider interface) was chosen over a separate sensor-host process (B) and a Windows service (C). The provider boundary passes plain immutable snapshots, so B can be introduced later if driver stability demands it.
- **Dev/test machine:** Intel i9-14900K (8P+16E, 32 threads), NVIDIA RTX 4090 + Intel UHD 770 iGPU, MSI Z790 GAMING PLUS WIFI, Samsung 990 PRO 2 TB NVMe, 32 GB RAM, Windows 11 Pro 26200. No AMD hardware is available locally; AMD paths are covered by mapping unit tests only and recorded as *untested on hardware* in the hardware matrix.

## 2. Goals of this slice

Deliver a runnable **Mazesta Test** desktop app that:

1. Enumerates hardware and reads sensors **independently of HWiNFO/OCCT/AIDA64** (§3.1) using LibreHardwareMonitorLib 0.9.6 (PawnIO driver, no WinRing0).
2. Shows a **live monitoring screen** matching §3.3: collapsible groups per hardware node, columns *Name / Current / Min / Max / Avg / Unit / State*, search, interval selector (1/2/5/30 s), reset statistics, double-click → independent chart windows with visible data gaps.
3. Shows a **dashboard** with CPU / GPU / GPU Hot Spot / RAM cards, inventory summary, provider status, and the static Mazesta intro + AM9 product card (§9.5, offline, no price, no network call).
4. Has the full **app shell**: sidebar with all eleven §9.2 entries (unbuilt pages show an honest "این بخش در مرحله بعدی ساخته می‌شود" placeholder), dark theme, per-monitor DPI, English-primary / Persian-secondary UI with RTL switching, and the "?" Persian help mechanism (§9.3).
5. Persists configuration atomically with schema versioning (§2.1 Persistence).
6. Establishes the solution structure, coding conventions, test projects and build scripts every later slice builds on.

### Non-goals (explicitly out of scope for this slice)

Test engine and queue, every stress/benchmark test, alert rules and notifications, warning/critical colouring (§6 — only Missing/Stale/Invalid grey states appear here), reports of any format, Mazesta Monitor tray edition, tray icon in the full edition, overlay, installer/portable packaging scripts beyond a plain publish, live product/price fetch, theme switching (dark only), and all Phase 2 items (OC/UV, Windows tweaks, WordPress).

## 3. Architecture

### 3.1 Projects and dependencies

```
darabi/                                  (repo root)
├── Mazesta.sln
├── Directory.Build.props                # net10.0 / net10.0-windows, nullable, implicit usings, TreatWarningsAsErrors, version
├── Directory.Packages.props             # central package version management
├── global.json                          # pins SDK 10.0.4xx
├── .gitignore, .editorconfig, README.md
├── src/
│   ├── Mazesta.Core/                    # domain model, ids, units, roles, validation, PersianDigits, IClock. NO Windows/WPF/LHM references.
│   ├── Mazesta.Hardware/                # ISensorProvider, IInventoryProvider, LibreHardwareMonitorProvider, WmiInventoryProvider, role-mapping table. net10.0-windows.
│   ├── Mazesta.Monitoring/              # PollingEngine, HistoryStore, SensorStatistics, StaleDetector, EventLog, MonitoringFocus.
│   ├── Mazesta.Persistence/             # AppPaths (portable vs LocalAppData), JsonStore<T> (atomic), SchemaMigrator, AppConfig, RollingFileLogger.
│   └── Mazesta.Desktop/                 # WPF app "Mazesta Test". Views, ViewModels, Controls (TimeSeriesChart, HelpTip), Localization, Theme.
├── tests/
│   ├── Mazesta.Core.Tests/
│   ├── Mazesta.Hardware.Tests/          # mapping tests on fake trees + [Trait("Category","Hardware")] tests that touch real hardware
│   ├── Mazesta.Monitoring.Tests/
│   └── Mazesta.Persistence.Tests/
├── docs/
│   ├── ARCHITECTURE.md                  # all layers incl. future Diagnostics / Reporting / Monitor and their allowed dependencies
│   ├── PROVIDERS-AND-FALLBACKS.md       # what each provider supplies, what happens when it is absent
│   ├── HARDWARE-MATRIX.md               # per machine: what was observed, what was missing, comparison with HWiNFO
│   ├── VERIFICATION-slice1.md           # measured RAM/CPU/startup, test results, honest gaps
│   ├── GUIDE-FA.md                      # راهنمای فارسی (grows per slice)
│   └── superpowers/specs/, superpowers/plans/
├── build.ps1                            # build / test / publish → artifacts/
└── artifacts/                           (git-ignored)
```

Allowed references (arrows = "may reference"): `Desktop → Monitoring, Hardware, Persistence, Core`; `Monitoring → Core`; `Hardware → Core`; `Persistence → Core`. Nothing references `Desktop`. Future `Diagnostics → Core, Hardware, Monitoring`; `Reporting → Core`; `Monitor (tray) → Core, Hardware, Monitoring, Persistence` and never `Desktop` or `Diagnostics` (§8.2). These future projects are described in `docs/ARCHITECTURE.md` now and created when their slice starts.

### 3.2 Runtime shape

One elevated WPF process (`app.manifest`: `requestedExecutionLevel="requireAdministrator"`, `dpiAwareness="PerMonitorV2"`, `longPathAware`). Threads:

- **UI thread** — WPF dispatcher only. Never blocks on providers.
- **Polling thread** — one dedicated `Thread("Mazesta.Polling", IsBackground=true)` owned by `PollingEngine`. Opens the LHM `Computer`, polls, publishes snapshots. All provider calls happen here.
- **Inventory task** — one `Task` at startup for WMI inventory (WMI can take seconds; the window must appear first).

A named mutex `Global\Mazesta.Test.SingleInstance` enforces one full-edition instance; a second launch brings the first window to the foreground and exits.

## 4. Core model (Mazesta.Core)

All types are immutable records unless stated. No provider type leaks past `Mazesta.Hardware`.

| Type | Definition |
|---|---|
| `HardwareKind` | `Cpu, Gpu, Memory, Motherboard, Storage, Network, Psu, Cooler, Other` |
| `HardwareVendor` | `Intel, Amd, Nvidia, Unknown` — assigned **only** from the provider's hardware type (e.g. LHM `HardwareType.GpuNvidia`), never from the product name (§3.1). |
| `HardwareId` | `readonly record struct HardwareId(string Value)`. Format `{kind}/{token}`. Storage uses the drive serial: `storage/S6Z2NJ0T123456` (spec §6.1 "stable disk id"). Others use the provider's path with `/` → `-`, e.g. `cpu/intelcpu-0`, `gpu/nvidiagpu-0`, `gpu/intelgpu-0`, `memory/generic-0`, `motherboard/lpc-nct6687d-0`. If a storage serial is unavailable the provider path is used and `HardwareNode.IdIsStable=false`. |
| `HardwareNode` | `Id, Kind, Vendor, Name, ParentId?, IdIsStable, IReadOnlyList<SensorDefinition> Sensors, InventoryKey?` |
| `SensorKind` | `Temperature, Clock, Load, Voltage, Current, Power, Energy, Fan, Control, Level, Factor, Data, SmallData, Throughput, Timespan, Frequency, Noise, Humidity, Flow, Unknown` |
| `Unit` | `Celsius, MegaHertz, Percent, Volt, Ampere, Watt, Rpm, Gigabyte, Megabyte, BytesPerSecond, Seconds, Hertz, Decibel, None` with display symbol and formatting precision. |
| `SensorRole` | Semantic tag for sensors named as mandatory in §3.2. Values: `None, CpuPackageTemp, CpuCoreTemp, CpuTctlTdie, CpuCcdTemp, CpuCoreClock, CpuEffectiveClock, CpuBusClock, CpuVcore, CpuPackagePower, CpuCorePower, CpuTotalLoad, CpuThreadLoad, CpuFan, GpuCoreTemp, GpuHotSpotTemp, GpuVramTemp, GpuCoreClock, GpuMemoryClock, GpuLoad3D, GpuLoadCompute, GpuLoadVideo, GpuLoadMemoryController, GpuPower, GpuVoltage, GpuFanRpm, GpuFanPercent, GpuVramTotal, GpuVramUsed, GpuVramFree, RamUsed, RamFree, RamTotal, RamLoad, DimmTemp, BoardTemp, ChipsetTemp, BoardFan, BoardVoltage, StorageTemp, StorageUsedSpace, StorageReadRate, StorageWriteRate, StorageRemainingLife, NetUpload, NetDownload, NetUtilization`. Roles let later slices (alert rules, reports) address "the CPU package temperature" without string matching. |
| `SensorDefinition` | `SensorId Id, HardwareId Hardware, string Name, SensorKind Kind, Unit Unit, SensorRole Role, int Ordinal` |
| `SensorId` | `readonly record struct SensorId(string Value)`; `{hardwareId}#{providerSensorPath}` e.g. `gpu/nvidiagpu-0#temperature/2`. |
| `DataQuality` | `Ok, Missing, Stale, Invalid` |
| `SensorReading` | `SensorId Id, double? Value, DateTimeOffset Timestamp, DataQuality Quality, string Source` |
| `SensorSnapshot` | `long Sequence, DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus` |
| `NodeStatus` | `Ok, UpdateFailed(string reason, DateTimeOffset since)` |
| `ProviderStatus` | `NotStarted, Starting, Ready(int sensorCount), Degraded(string reasonKey, string detail), Failed(string reasonKey, string detail)`; `reasonKey` is a localisation key (e.g. `Provider.PawnIoMissing`) so the UI can show Persian text. |
| `HardwareInventory` | `CpuInfo, IReadOnlyList<GpuInfo>, IReadOnlyList<MemoryModuleInfo>, MotherboardInfo, BiosInfo, IReadOnlyList<StorageDeviceInfo>, IReadOnlyList<NetworkAdapterInfo>, OsInfo`. Every field is nullable and rendered as «دریافت نشد» / "Not available" when null. |

**Validation (`ReadingValidator`)** runs on every reading before publication and downgrades quality to `Invalid`:

| Kind | Invalid when |
|---|---|
| Temperature | `value <= 0` or `value > 150` (§3.1: a zero temperature is never a healthy reading) |
| Clock | `value <= 0` (§6.2 rule 2: zero clock readings are discarded) |
| Load, Level, Control | `value < 0` or `value > 100` |
| Voltage | `value < 0` or `value > 20` |
| Power, Current, Fan, Data, SmallData, Throughput, Energy | `value < 0` (0 RPM is a valid "fan stopped") |
| any | `NaN` or `±Infinity` |

**Utilities:** `PersianDigits.Normalize(string)` maps Persian and Arabic-Indic digits to ASCII for numeric inputs (§4.1); `IClock` / `SystemClock` for testable time.

## 5. Hardware layer (Mazesta.Hardware)

### 5.1 Interfaces

```csharp
public interface ISensorProvider : IDisposable
{
    string Name { get; }
    ProviderStatus Status { get; }
    event Action<ProviderStatus>? StatusChanged;
    /// Called once on the polling thread. Never throws; failures become Status = Failed/Degraded.
    void Start();
    IReadOnlyList<HardwareNode> Hardware { get; }
    /// Called on the polling thread. Updates only nodes whose cadence is due. Never throws.
    IReadOnlyList<SensorReading> Poll(PollRequest request);
}
public readonly record struct PollRequest(DateTimeOffset Now, IReadOnlySet<HardwareId> NodesToUpdate);

public interface IInventoryProvider
{
    Task<HardwareInventory> ReadAsync(CancellationToken ct);   // never throws; partial results with nulls
}
```

### 5.2 LibreHardwareMonitorProvider

- Wraps `LibreHardwareMonitor.Hardware.Computer` with `IsCpuEnabled, IsGpuEnabled, IsMemoryEnabled, IsMotherboardEnabled, IsStorageEnabled, IsNetworkEnabled = true`; `IsControllerEnabled, IsPsuEnabled, IsBatteryEnabled = false` for this slice (PSU only with real telemetry, §3.2 — deferred until a supported PSU can be tested).
- `Start()` calls `Computer.Open()` inside try/catch. Exceptions → `Failed`. After open, it builds `HardwareNode`s from `IHardware` (including sub-hardware, flattened with `ParentId`), assigns `Kind/Vendor` from `HardwareType`, and assigns `SensorRole` through `SensorRoleMap` — a single table keyed by `(HardwareType, SensorType, exact LHM sensor name)`. Unlisted sensors get `SensorRole.None`. A GPU without a "GPU Hot Spot" sensor simply has no `GpuHotSpotTemp` role anywhere; nothing is substituted (§3.1).
- **Driver detection.** After `Open()`, the provider checks whether the PawnIO driver is loaded (via the library's PawnIO API if exposed, otherwise by checking the `PawnIO` service state through `ServiceController`). If absent, status = `Degraded("Provider.PawnIoMissing", …)`. CPU MSR-based sensors will then be missing and are shown as such. The app never modifies Memory Integrity or driver policy (§2.2).
- **Cadence.** `Poll` updates only nodes in `NodesToUpdate`. The engine puts Storage nodes on the slow cadence (`StoragePollInterval`, default 900 s, min 60 s) and every other node on the fast cadence. The very first poll updates everything.
- **Isolation.** Each `IHardware.Update()` runs in its own try/catch. On exception the node is recorded `UpdateFailed(reason)`; its readings for that tick are emitted with `Quality = Stale` carrying the last good timestamp; the node is retried on its next due tick. A node failing 3 consecutive times is logged once as an event (not every tick).
- `ISensor.Value == null` → `Quality = Missing`. Otherwise `ReadingValidator` decides `Ok`/`Invalid`.
- Storage node id uses the serial obtained from LHM's storage hardware (via its SMART/identify data) when present.

### 5.3 WmiInventoryProvider

Uses `System.Management` against `Win32_Processor`, `Win32_VideoController`, `Win32_PhysicalMemory`, `Win32_BaseBoard`, `Win32_BIOS`, `Win32_OperatingSystem`, `Win32_NetworkAdapter` + `Win32_NetworkAdapterConfiguration`, and `MSFT_PhysicalDisk` (`root\Microsoft\Windows\Storage`). Each query is isolated; a failing query leaves its section null and logs an event. Results feed the Dashboard cards and the *inventory* portion of monitoring groups (e.g. RAM modules listed under the Memory group; disk model/serial/firmware under each Storage group). Storage entries are joined to LHM storage nodes by serial number.

### 5.4 Fallback summary (goes into `docs/PROVIDERS-AND-FALLBACKS.md`)

| Situation | Behaviour |
|---|---|
| LHM `Open()` throws | Provider `Failed`; monitoring shows a banner with the reason; dashboard still shows WMI inventory. |
| PawnIO not installed / not loaded | Provider `Degraded`; CPU temps/clocks from MSR missing; banner explains and links to the installer; no estimates. |
| A hardware node throws on update | Only that node's sensors go Stale; others unaffected; retried next due tick. |
| GPU has no Hot Spot / VRAM temp sensor | Role absent; dashboard Hot Spot card reads «دریافت نشد». |
| WMI query fails | That inventory section is null and displayed as not available. |
| Poll takes longer than the interval | Tick is skipped, an overrun event is logged (rate-limited to once per minute), next poll starts immediately. |

## 6. Monitoring layer (Mazesta.Monitoring)

### 6.1 PollingEngine

- Constructor: `(ISensorProvider provider, IClock clock, MonitoringOptions options, IEventLog log)`. Options: `FastInterval` (1/2/5/30 s, default 2 s), `StorageInterval` (default 900 s).
- `Start()` spins up the polling thread: `provider.Start()` → loop `{ compute due nodes; readings = provider.Poll(); validate; stale-check; build snapshot; publish }` with `Stopwatch`-based scheduling. `Pause()` / `Resume()` stop polling without disposing the provider (used by the UI *Pause* button and naturally produces chart gaps). `SetFastInterval(TimeSpan)` applies from the next tick without restart.
- `SnapshotPublished` event is raised **on the polling thread**. Subscribers must not block. The Desktop app posts to the dispatcher with *latest-wins* coalescing: if a previous UI update is still pending, the pending snapshot is replaced.
- `StaleDetector`: a reading is `Stale` if its node has `UpdateFailed` status, or if the node's last successful update is older than `3 × its cadence`.

### 6.2 HistoryStore (bounded, §3.3)

Two tiers per sensor, both fixed-capacity ring buffers, values stored as `float`:

| Tier | Capacity | Content | Memory per sensor |
|---|---|---|---|
| Raw | 900 samples | `(int secondsSinceEngineStart, float value)`; a Missing/Stale/Invalid tick stores `NaN` so gaps are preserved | 7.2 KB |
| Minute | 2880 buckets (48 h) | `(int minuteIndex, float min, float max, float avg, ushort okCount)` | ≈ 52 KB |

At 2 s the raw tier covers 30 min; at 30 s it covers 7.5 h. 200 sensors ≈ 12 MB worst case; verified by a unit test that asserts the byte budget. Charts read raw when the requested window fits, otherwise the minute tier (avg line + min/max band).

### 6.3 SensorStatistics

Per sensor: `Min, Max, Average (running mean), Count, Since`. Only `Ok` readings contribute. `ResetAll()` and `Reset(HardwareId)`. Exposed to the UI in the same snapshot-driven update.

### 6.4 MonitoringFocus

`void RequestFocus(IReadOnlySet<HardwareKind> kinds, FocusReason reason)` → raises `FocusRequested` once. The monitoring view expands exactly those groups and collapses the rest **once**; subsequent ticks never touch expansion state (§3.3, acceptance §15-7). This slice ships the API, the view handler, and a hidden developer command (`Ctrl+Shift+F`) that exercises it; the real caller is the test engine in slice 2.

### 6.5 EventLog

Bounded in-memory ring (1000 entries) of `(timestamp, level, key, detail)` plus a sink to the rolling file logger. Used for provider status changes, node failures, overruns, inventory failures, config migrations.

## 7. Persistence (Mazesta.Persistence)

- `AppPaths`: if `portable.marker` exists beside the executable → data root is `<exe dir>\Data`; else `%LocalAppData%\Mazesta\Test`. Sub-folders `config`, `logs`, `sessions` (future), `history` (future).
- `JsonStore<T>`: `Load()` / `Save(T)`; save writes `*.tmp` then `File.Replace` (atomic); files carry `"schemaVersion": n`. `SchemaMigrator` applies ordered `IMigration` steps and logs each migration; an unreadable file is renamed `*.corrupt-<timestamp>` and defaults are used (event logged, never silent).
- `AppConfig` (schemaVersion 1): `Language ("en"|"fa")`, `FastIntervalSeconds`, `StorageIntervalSeconds`, `ShopName` (§1: only the shop name is configurable; product name fixed), `ExpandedGroups: string[]`, `MainWindowPlacement`, `ChartWindows: [{sensorId, placement, windowMinutes}]`.
- `RollingFileLogger`: `Microsoft.Extensions.Logging` provider writing `logs\mazesta-test-YYYYMMDD.log`, keeps the newest 7 files. No third-party logging framework.

## 8. Desktop app (Mazesta.Desktop)

- **Stack:** WPF on `net10.0-windows`, `CommunityToolkit.Mvvm` (source-generated observable properties and commands), `Microsoft.Extensions.DependencyInjection` for composition. No hosting framework, no Skia, no WebView2.
- **Localization:** `Strings.resx` (English, primary) and `Strings.fa.resx` (Persian). Culture set from `AppConfig.Language` at startup; switching language prompts a restart (simplest correct behaviour). `FlowDirection` of the shell binds to the active language (RTL for Persian). Component names (CPU model, sensor names) stay English in both languages (§0). **Vazirmatn** (SIL OFL) is embedded for Persian glyphs; UI font falls back to Segoe UI.
- **Help ("?") mechanism:** `HelpTip` control placed next to each English feature label; a round, keyboard-focusable button that opens a Persian RTL popup with text from `Help.fa.resx` keyed by feature id (§9.3). Sidebar labels are single-line and never wrap.
- **Shell:** left sidebar with the eleven §9.2 entries and icons; content region; bottom status bar with provider status (`Ready · 137 sensors` / `Degraded: PawnIO not installed` / `Failed: …`), poll interval, and a Pause/Resume monitoring button. Dark theme only in this slice; colours are tokens in a resource dictionary so a light theme can be added later. Red/orange are reserved for real alert states and are **not used** anywhere in this slice (§9.4).
- **Dashboard page:** cards for CPU (package temp, effective clock, load, power), GPU per adapter (core temp, load, clock, power, VRAM used/total), GPU Hot Spot (own card; «دریافت نشد» if absent), RAM (used/free/total, modules); inventory summary card (CPU, GPUs, board + BIOS, disks, OS); Mazesta intro card and a smaller AM9 product card with an embedded image, name, one-line description and a "مشاهده محصول" button opening the official URL in the default browser. No price is shown. Official contact page link is taken from the spec (§9.5). Nothing is fetched from the network and nothing about the customer's hardware is ever sent.
- **Monitoring page:** grouped, virtualised grid. One group per `HardwareNode` (each CPU, each GPU including iGPU, Memory, Motherboard and its sub-hardware, each Storage device, each Network adapter). Columns: Name, Current, Min, Max, Avg, Unit, State. State cell shows text + icon for Missing («دریافت نشد»), Stale («قدیمی»), Invalid («نامعتبر»); rows in those states are grey. Toolbar: search box (filters by group or sensor name), interval selector 1/2/5/30 s, Reset stats, Pause/Resume. Expand/collapse state is user-owned and persisted; ticks never change it. Double-click on a sensor row opens a chart window.
- **Chart window (`TimeSeriesChart` control):** custom `FrameworkElement` rendering with `DrawingContext`. Title `"<Hardware> — <Sensor> (<unit>)"`, time axis with readable ticks, Y axis auto-scaled with padding and "nice" steps, min/max reference lines and labels, current value readout. `NaN` samples break the line so **gaps are visible** (§3.3). Series colour is fixed per `HardwareKind` (CPU one constant colour, RAM another, GPU another) and the same colours are used by dashboard card legends. Window selector 1/5/10/30 min, 1/6/24 h. Any number of chart windows may be open; each subscribes to `HistoryStore` and re-renders at most once per tick. Open chart windows are restored at next launch.
- **Settings page:** language, fast interval, storage interval (numeric field accepting Persian digits), shop name, data folder path (read-only, with "open folder"), portable/installed mode indicator, app version, third-party notices link.
- **Placeholder pages** (Tests, Benchmarks, GPU, CPU, Network, Storage, Gaming, Windows Tools, Reports): title + Persian/English sentence stating the feature arrives in a later slice/phase. Nothing is simulated.
- **Threading discipline:** one dispatcher update per snapshot; row view models are updated in place (no collection churn); heavy inventory strings are computed off-thread.

## 9. Error handling

- Provider start failure → app fully usable; monitoring shows the failure banner; dashboard shows WMI inventory; nothing pretends to be a reading.
- Node update failure → only that node's rows go Stale; retry next due tick; logged once per 3 consecutive failures.
- PawnIO missing → Degraded banner with Persian guidance; no estimation, no policy changes.
- Poll overrun → skip + rate-limited event.
- Unhandled exception on the UI thread → logged with stack trace to the rolling log, a Persian/English dialog offers to continue or exit; the polling thread keeps running unless the process exits.
- Polling-thread exception outside provider code (engine bug) → caught at loop level, logged, engine status `Failed`, UI banner. Never crashes the process silently.
- Corrupt config → renamed aside, defaults applied, event logged and surfaced in the status bar once.
- Second instance → foregrounds the first, exits.

## 10. Testing

**Unit tests (xUnit, run by `dotnet test`, no hardware, no admin):**

- Core: `ReadingValidator` table above (0 °C → Invalid, 151 °C → Invalid, 0 MHz → Invalid, 0 RPM → Ok, NaN → Invalid); unit formatting; `PersianDigits.Normalize`.
- Hardware: `SensorRoleMap` on fake `IHardware`/`ISensor` trees — NVIDIA hot spot maps to `GpuHotSpotTemp`; a GPU without hot spot yields **no** `GpuHotSpotTemp` (no fallback to core); AMD Tctl/Tdie mapping; Intel package temp; vendor from `HardwareType` even when the name says "GPU"; storage id from serial; unmapped sensor → `None`; node exception → Stale readings for that node only; cadence respected (storage node not updated on a fast tick).
- Monitoring: `HistoryStore` raw/minute tiers, NaN gaps, byte budget; `SensorStatistics` ignores non-Ok; `StaleDetector` thresholds; `PollingEngine` with fake clock and fake provider: interval change, pause/resume, overrun handling, latest-wins coalescing contract; `MonitoringFocus` raises once.
- Persistence: atomic save (temp file + replace), corrupt file handling, migration v0→v1 sample, portable vs LocalAppData resolution.

**Hardware tests (`[Trait("Category","Hardware")]`, excluded by default, run elevated on the dev box):** LHM opens; CPU package temp present with PawnIO loaded; RTX 4090 exposes `GpuHotSpotTemp`; UHD 770 appears as a separate `gpu/intelgpu-*` node; NVMe node id equals `storage/<serial>` and matches WMI; no sensor reports `0 °C` as `Ok`.

**Manual acceptance (recorded with screenshots in `docs/HARDWARE-MATRIX.md` and `docs/VERIFICATION-slice1.md`):** side-by-side with HWiNFO (HWiNFO closed while Mazesta runs, then vice-versa) for package/core/hot-spot temps at idle, with an explanation of sensor differences (§15-2); three chart windows open simultaneously with a visible gap after Pause/Resume; expand/collapse survives ticks and restarts; search and reset work; interval change applies live; PawnIO-absent scenario (PawnIO service stopped) shows Degraded and no temperatures; provider failure injected via a fake provider keeps the UI responsive.

**Performance record (§13):** idle working set, idle CPU %, and time-to-window / time-to-inventory-ready measured on the dev box after 5 minutes idle on the Dashboard, and reported as measured. Targets: < 80 MB, < 1 %, < 3 s. Publish uses `PublishReadyToRun=true`, framework-dependent, win-x64. If a target is missed the number and the cause are written down; no working-set trimming tricks (§13.1).

## 11. Build and run

```powershell
# from Windows PowerShell or via dotnet.exe from WSL
dotnet build Mazesta.sln -c Release
dotnet test Mazesta.sln -c Release --filter "Category!=Hardware"  # unit tests
dotnet test Mazesta.sln -c Release --filter Category=Hardware   # elevated, dev box only
dotnet publish src/Mazesta.Desktop -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
```
`build.ps1 -Test -Publish` wraps the above. Requirements: .NET SDK 10.0.400+, Windows 10 21H2+/11 x64, .NET 10 Desktop Runtime on target machines, PawnIO driver for CPU MSR sensors.

## 12. Dependencies and licences

| Package | Version | Licence | Used by |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | Hardware |
| ↳ DiskInfoToolkit, RAMSPDToolkit-NDD, HidSharp, System.IO.Ports, Mono.Posix.NETStandard (transitive) | as pinned by LHM | reviewed before first commit; recorded in `THIRD-PARTY-NOTICES.md` | Hardware |
| System.Management | 10.0.x | MIT | Hardware |
| CommunityToolkit.Mvvm | 8.x | MIT | Desktop |
| Microsoft.Extensions.DependencyInjection, .Logging.Abstractions | 10.x | MIT | Desktop, Persistence |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | latest stable | Apache-2.0 / MIT | tests |
| Vazirmatn font | latest | SIL OFL 1.1 | Desktop (embedded) |
| PawnIO driver | 2.x | separate signed installer, not bundled in this slice | runtime prerequisite |

No HWiNFO SDK or shared-memory access exists in the product path (§3.1).

## 13. Acceptance criteria for slice 1

1. `dotnet build` produces zero warnings; `dotnet test` passes; hardware-category tests pass elevated on the dev box.
2. With HWiNFO, OCCT and AIDA64 not running, the Monitoring page shows for the dev box: CPU package and per-core temperatures, core and effective clocks, Vcore, package power, per-thread load; RTX 4090 core temp, hot spot, VRAM temp if exposed, core/memory clocks, loads, power, fan, VRAM used/total; UHD 770 as its own group; RAM used/free/total plus module inventory; motherboard temperatures, fans, voltages; 990 PRO temperature and health-related sensors on the slow cadence; network adapter up/down throughput. Anything the provider does not expose is shown as «دریافت نشد», never as a number.
3. Package, core and hot-spot temperatures agree with HWiNFO within ±2 °C at idle when compared in sequence; differences are explained in the hardware matrix.
4. Expand/collapse state is never changed by ticks; focus request expands exactly the requested groups once.
5. Three chart windows update live, auto-scale, show min/max, and display a visible gap after Pause/Resume.
6. Idle resource numbers are measured and recorded; each target is marked met or missed with the measured value.
7. PawnIO-absent and provider-failure scenarios behave as in §9 with no fabricated values.
8. Config survives restart, migrates from a v0 sample, and a corrupt file does not prevent startup.
9. No network request is made by the application in this slice (verified with a packet capture or Windows firewall log during a 10-minute session).
10. Persian UI renders RTL with correct glyph shaping; every visible English feature label on Dashboard, Monitoring and Settings has a working "?" help popup.

## 14. Risks and open points

- **LHM sensor names** are the mapping key; they are stable per hardware family but must be verified against the 0.9.6 source for each family in the role map. Mapping tests pin them.
- **Sensor id stability** relies on LHM's enumeration order for same-model duplicates (e.g. two identical GPUs). Recorded as a known limitation; storage is already serial-keyed.
- **PawnIO detection API** — if 0.9.6 exposes no public "is driver loaded" call, the service-state check is used. Confirmed during implementation and noted in `PROVIDERS-AND-FALLBACKS.md`.
- **Memory target** — WPF + LHM + WMI baseline may exceed 80 MB. Measured honestly; mitigation options (ReadyToRun, trimming unused LHM sub-systems, lazy WMI) are tried before reporting a miss.
- **AMD coverage** cannot be validated on local hardware in this slice.
