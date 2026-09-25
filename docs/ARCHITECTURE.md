# Architecture

Mazesta Test is a single elevated WPF process built from layered class libraries, plus a separate low-footprint tray process.
Each layer is its own project so the project graph, not convention, enforces the dependencies. Logic lives in the lowest layer
that can hold it, so it is unit-testable without WPF.

## Projects and dependencies

| Project | Purpose | Target framework | References |
|---|---|---|---|
| `Mazesta.Core` | Domain model: ids, units (`Units`), sensor roles and grouping, validation, `PersianDigits`, `IClock`, inventory models, health rules (`Health.HealthAlerts`), and the provider contracts (`Providers`: `ISensorProvider`, `IInventoryProvider`). No Windows, WPF or LibreHardwareMonitor. | net10.0 | — |
| `Mazesta.Hardware` | `LibreHardwareMonitorProvider` (sensors, PawnIO driver), `WmiInventoryProvider` (inventory), the role-mapping table and `SensorNameCatalog`. | net10.0-windows | Core |
| `Mazesta.Monitoring` | `PollingEngine`, `HistoryStore`, `SensorStatistics`, `StaleDetector`, `EventLog`, `MonitoringFocus`. | net10.0 | Core |
| `Mazesta.Persistence` | `AppPaths` (portable only: everything in `Data\` next to the exe), `JsonStore<T>` (atomic, schema-migrated), `AppConfig`, `TrayIntervals` (read-only view for the tray), `RollingFileLogger`. | net10.0 | Core |
| `Mazesta.Diagnostics` | `TestEngine` (sequential queue, repeat, cancellation, crash checkpoint, WHEA post-check), executors for CPU, memory, storage and network, `SensorEvidence`; benchmarks (`Benchmarks`: CPU single/all-thread, memory, CrystalDiskMark-style storage, internet speed) run by `BenchmarkRunner`. | net10.0-windows | Core, Monitoring, Persistence |
| `Mazesta.Diagnostics.Gpu` | GPU tests on ComputeSharp (steady/variable/pulse stress, VRAM, render, power) and the GPU benchmarks on raw Direct3D 12 through Vortice (rasterisation, DXR 1.1 inline ray tracing, DirectML AI at FP32/FP16/INT8). HLSL in `Shaders/`, precompiled by `tools/compile-gpu-shaders.ps1`. | net10.0-windows | Core, Monitoring, Diagnostics |
| `Mazesta.Reporting` | `SessionReport` (test sessions and benchmark-only reports, `ReportKind`), JSON/HTML/plain-text writers in Persian or English (`ReportText`), `ReportStore`, `SensorSummarizer`, `ReportComparison` and the before/after page. UI-free. | net10.0 | Core, Monitoring |
| `Mazesta.Desktop` | The WPF app "Mazesta Test": MVVM (CommunityToolkit.Mvvm), DI, Views/ViewModels, localisation (fa/en, RTL), PDF through WebView2, tray control. | net10.0-windows | all of the above, and Tray (to ship its exe) |
| `Mazesta.Tray` | "Mazesta Monitor": windowless tray process that opens the sensor provider only during a check (spec §8.2). | net10.0-windows | Core, Hardware, Persistence |

Nothing references `Desktop`. `Diagnostics` does not reference `Hardware`: executors read what `PollingEngine` already published
(`SensorEvidence`), never a hardware provider directly. `Reporting` knows nothing of WPF; the Desktop `ReportService` feeds it.
`Tray` never references `Monitoring`, `Diagnostics` or `Desktop`, so it stays small and never loads the stress engine or the 3D code.

## Pages (Desktop)

Dashboard, Monitoring, Tests, System Information, Benchmarks, CPU, GPU, Network, Storage, Gaming, Windows Tools, Reports, Settings.
Gaming and Windows Tools (spec 11) only run Windows' own tools on a button (powercfg, sfc, DISM) or open Windows' own settings;
overclocking/undervolting and an FPS overlay are not offered. The sidebar carries the logo and the service number (spec 7.1), printed
on every report; finished tests and benchmarks raise a notice, and Ctrl+1 ... Ctrl+0 open the first ten pages (spec 9.4). The four component pages (`ComponentViewModel`) assemble existing
pieces for one kind of hardware: its System Information sections, the Monitoring tree limited to it, and its benchmark rows.

Long-running work belongs to session singletons, not pages: `TestEngine` for tests and `BenchmarkRunner` for benchmarks, so a
run keeps going and its result stays when the technician leaves the page. Page view models are built per visit by `Func<T>`
factories and disposed by the shell when it leaves them.

## Data (portable)

Everything the app writes lives in `Data\` next to `MazestaTest.exe`: `config\appconfig.json`, `logs\`, `sessions\` (test
checkpoint), `history\`, `reports\<date>-<id>\` (`report.json`, `report.html`, `report.txt`, `report.pdf` when exported, and
`comparison-*.html`), and `cache\` (the PDF printer's WebView2 profile). The first start of a copy without `Data\` copies an
earlier installed version's `%LocalAppData%\Mazesta\Test` in once, without changing it.

## Test projects

Each `src` project has a matching project under `tests/` referencing the same dependencies plus xunit, and each production project
grants `InternalsVisibleTo` to its own tests. Tests that need the real machine, drivers or administrator rights carry
`[Trait("Category","Hardware")]` and are excluded from the default run with `--filter "Category!=Hardware"`.
