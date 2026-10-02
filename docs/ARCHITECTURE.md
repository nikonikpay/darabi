# Architecture

Mazesta is a single elevated process (`MazestaWeb.exe`) whose whole interface is a local web page in one WebView2, built from
layered class libraries, plus a separate low-footprint tray process. Each layer is its own project so the project graph, not
convention, enforces the dependencies. Logic lives in the lowest layer that can hold it, so it is unit-testable without a window.

The earlier WPF edition (`MazestaTest.exe`, its own XAML pages) was retired on 2026-09-28; its last state is the git tag
`wpf-edition-final` (branch `backup/wpf-edition-20260928`). What the web edition used of it stays in `Mazesta.Desktop` as the app layer.

## Projects and dependencies

| Project | Purpose | Target framework | References |
|---|---|---|---|
| `Mazesta.Core` | Domain model: ids, units (`Units`), sensor roles and grouping, validation, `PersianDigits`, `IClock`, inventory models, health rules (`Health.HealthAlerts`), GPU tuning types and the automatic undervolt/overclock search (`Tuning`), and the provider contracts (`Providers`: `ISensorProvider`, `IInventoryProvider`). No Windows, WPF or LibreHardwareMonitor. | net10.0 | — |
| `Mazesta.Hardware` | `LibreHardwareMonitorProvider` (sensors, PawnIO driver), `WmiInventoryProvider` (inventory), the role-mapping table and `SensorNameCatalog`; GPU tuning through NVIDIA's NVML (`Nvidia.NvmlTuningProvider`). | net10.0-windows | Core |
| `Mazesta.Monitoring` | `PollingEngine`, `HistoryStore`, `SensorStatistics`, `StaleDetector`, `EventLog`, `MonitoringFocus`. | net10.0 | Core |
| `Mazesta.Persistence` | `AppPaths` (portable only: everything in `Data\` next to the exe), `JsonStore<T>` (atomic, schema-migrated), `AppConfig`, `GpuProfileDocument` (GPU profiles and the tuning crash journal), `TrayIntervals` (read-only view for the tray), `RollingFileLogger`. | net10.0 | Core |
| `Mazesta.Diagnostics` | `TestEngine` (sequential queue, repeat, cancellation, crash checkpoint, WHEA post-check), executors for CPU, memory, storage and network, `SensorEvidence`; benchmarks (`Benchmarks`: CPU single/all-thread, memory, CrystalDiskMark-style storage, internet speed) run by `BenchmarkRunner`; `Tuning.GpuAutoTuner` runs a tuning search on a card. | net10.0-windows | Core, Monitoring, Persistence |
| `Mazesta.Diagnostics.Gpu` | GPU tests on ComputeSharp (steady/variable/pulse stress, VRAM, render, power) and the GPU benchmarks on raw Direct3D 12 through Vortice (rasterisation, DXR 1.1 inline ray tracing, DirectML AI at FP32/FP16/INT8); the verified compute/memory load the GPU tuner judges settings by. HLSL in `Shaders/`, precompiled by `tools/compile-gpu-shaders.ps1`. The two visual tests and the two scene benchmarks draw the Persian garden (`Scene/GardenScene`, embedded `garden.mzscene`, the plants, logo and night light rig of `Mazesta-Art/courtyard-v4.blend` around the building of the owner's DFM_Courtyard_V6, joined by `Mazesta-Art/scripts/prepare_courtyard_v6.py` (which also makes the trees' leaf particles real objects), exported from `Mazesta-Art/courtyard-v6.blend` by `tools/scene/export_garden.py`): `GardenRaster` (shadow map, the still scene's part drawn once and the moving logo each frame; the pool's planar reflection, MSAA per load level) and `GardenRay` (DXR 1.1 inline, a BLAS per mesh, 4 rays a pixel, soft shadow rays to every lamp, one bounced-light ray, reflection and refraction, then an edge-aware a-trous denoiser over the frame's own light). Both draw a clouded procedural sky and a furnished room behind each stained window (interior mapping: the room is painted in the shader, nothing is behind the glass). | net10.0-windows | Core, Monitoring, Diagnostics |
| `Mazesta.Reporting` | `SessionReport` (test sessions and benchmark-only reports, `ReportKind`), JSON/HTML/plain-text writers in Persian or English (`ReportText`), `ReportStore`, `SensorSummarizer`, `ReportComparison` and the before/after page. UI-free. | net10.0 | Core, Monitoring |
| `Mazesta.Desktop` | The app layer (a library; the assembly keeps its old name `MazestaTest`): composition (`Bootstrapper`, DI), the page view models the web bridge drives (tests, benchmarks, reports, settings, tuning, Windows tools, gaming, system information), `ReportService` and PDF through WebView2, tray control, the string tables (fa/en), and the one native window, the on-screen overlay (`OverlayViewModel`; `OverlayRenderer` draws it with GDI+, `OverlayWindow` shows it as a click-through layered window), and `UiDispatcher` (the UI thread over the Windows Forms message loop). No WPF since 2026-10-01 (tag `pre-wpf-removal`). | net10.0-windows | all of the above |
| `Mazesta.Web` | "Mazesta": the app (Windows Forms: `Program`, `MainWindow` around WebView2). Composes the services of `Mazesta.Desktop` (its `Bootstrapper`) and shows the interface in one WebView2 from `wwwroot` (plain HTML/CSS/JS modules, no build step, offline). `WebBridge` is the only way the page reaches the machine: a fixed list of named methods over JSON, mirroring the Desktop view models, and live sensor snapshots while the window is visible. Only one edition runs at a time (shared single-instance mutex); a second start brings the open window forward, and closing the main window ends the process. Also: `ChartWindow` (a sensor's chart popped out, its own tiny bridge), `ShopFeed` (one product from the shop's WordPress REST API, as plain text, cached), `Notifier` (Windows notifications for health alerts, failed tests and a failed sensor reader), benchmark records (`BenchmarkRecords`, best per system), the overlay page, and the diagnostic export. Ships the tray next to its exe. | net10.0-windows | Desktop (as a library), Tray (to ship it) |
| `Mazesta.Tray` | "Mazesta Monitor": windowless tray process that opens the sensor provider only during a check (spec §8.2): temperatures every 10 minutes, drive health every 30, each check kept in `Data\tray\checks.json` and shown in its summary window (double-click the icon); a problem is a Windows notification. Its menu shows and hides the app's overlay (`OverlaySignals`: the app then outlives its closed window for the overlay alone) and switches each NVIDIA card's GPU profile, which it puts back at every sign-in (`GpuStartup`, Data/config/gpu-startup.json). | net10.0-windows | Core, Hardware, Persistence |

Only `Web` references `Desktop`. `Diagnostics` does not reference `Hardware`: executors read what `PollingEngine` already published
(`SensorEvidence`), never a hardware provider directly. `Reporting` knows nothing of the UI; the Desktop `ReportService` feeds it.
`Tray` never references `Monitoring`, `Diagnostics` or `Desktop`, so it stays small and never loads the stress engine or the 3D code.

## Pages

The pages are the web page's modules (`src/Mazesta.Web/wwwroot/js/pages`), grouped in the side bar as families: Dashboard,
Monitoring, Tests & benchmarks, Hardware (System, CPU, GPU, Storage, Network), Overlay, Overclock & undervolt, Windows & games
(Windows tools, Tweaks, Windows Update, Gaming), Reports, Settings; Ctrl+1 ... Ctrl+9 open the families. Windows tools and Gaming run
Windows' own tools on a button (powercfg, sfc, DISM) or open Windows' own settings; Tweaks and Windows Update (WinUtil's list, trimmed to
what can be undone: `Diagnostics.Windows.TweakCatalog`, `UpdateProfiles`, `DnsChoice`) change registry values and services on an explicit
click and read the state back. Overclock & undervolt (slice 9) changes NVIDIA GPUs through
NVML - manual settings and an automatic search whose results are kept only when measured better than stock - and explains CPU and
memory-profile tuning without changing them (`docs/TUNING-RESEARCH.md`). The side bar carries the service number (spec 7.1),
printed on every report. The look is set by `DESIGN.md`.

Long-running work belongs to session singletons, not pages: `TestEngine` for tests and `BenchmarkRunner` for benchmarks, so a
run keeps going and its result stays when the technician leaves the page. Page view models are built by `Func<T>` factories; the
web bridge builds the ones it drives when the window opens and disposes them when it closes.

## Data (portable)

Everything the app writes lives in `Data\` next to `MazestaWeb.exe`: `config\appconfig.json`, `logs\`, `sessions\` (test
checkpoint), `history\`, `reports\<date>-<id>\` (`report.json`, `report.html`, `report.txt`, `report.pdf` when exported, and
`comparison-*.html`), `cache\` (the PDF printer's and the web edition's WebView2 profiles, the shop product in `cache\shop`),
`benchmarks\records.json` (the best result of each benchmark per system), `tray\checks.json` (the tray's recent checks), `logs\hardware-report.txt`
(what the first polls could not read on this machine) and `diagnostics\` (the exported zip to bring back). The first start of a copy without `Data\` copies an
earlier installed version's `%LocalAppData%\Mazesta\Test` in once, without changing it.

## Test projects

Each `src` project has a matching project under `tests/` referencing the same dependencies plus xunit, and each production project
grants `InternalsVisibleTo` to its own tests. Tests that need the real machine, drivers or administrator rights carry
`[Trait("Category","Hardware")]` and are excluded from the default run with `--filter "Category!=Hardware"`.
