# Architecture

Mazesta Test is a single elevated WPF process built from layered class
libraries. Each layer is a separate project so that dependencies can be
enforced by the project graph, not just by convention.

## Projects and dependencies

| Project | Purpose | Target framework | May reference |
|---|---|---|---|
| `Mazesta.Core` | Domain model: ids, units, roles, validation, `PersianDigits`, `IClock`, and the provider contracts (`Mazesta.Core.Providers`: `ISensorProvider`, `PollRequest`, `PollResult`, `IInventoryProvider`). No Windows/WPF/LibreHardwareMonitor references. | net10.0 | — |
| `Mazesta.Hardware` | `LibreHardwareMonitorProvider`, `WmiInventoryProvider`, role-mapping table — the implementations of the Core provider contracts. | net10.0-windows | Core |
| `Mazesta.Monitoring` | `PollingEngine`, `HistoryStore`, `SensorStatistics`, `StaleDetector`, `EventLog`, `MonitoringFocus`. | net10.0 | Core |
| `Mazesta.Persistence` | `AppPaths` (portable vs. LocalAppData), `JsonStore<T>` (atomic), `SchemaMigrator`, `AppConfig`, `RollingFileLogger`. | net10.0 | Core |
| `Mazesta.Diagnostics` | Test engine: `TestEngine` (sequential queue, repeat modes, cancellation, crash checkpoint), `ITestExecutor`/`TestExecutionRequest`, one real executor so far (`Cpu.CpuMatrixStressExecutor`). | net10.0 | Core, Monitoring, Persistence |
| `Mazesta.Desktop` | WPF app "Mazesta Test". Views, ViewModels, Controls (`TimeSeriesChart`, `HelpTip`), Localization, Theme. | net10.0-windows | Monitoring, Hardware, Persistence, Diagnostics, Core |

## Allowed references

`Desktop → Monitoring, Hardware, Persistence, Diagnostics, Core`;
`Diagnostics → Core, Monitoring, Persistence`; `Monitoring → Core`;
`Hardware → Core`; `Persistence → Core`. Nothing references `Desktop`.

`Diagnostics` does not reference `Hardware`: its one executor so far
(`CpuMatrixStressExecutor`) only needs `PollingEngine`/`HistoryStore` to read
already-published sensor readings, never a hardware provider directly. A
future GPU/storage executor that needs to enumerate adapters or issue
provider-specific calls may need to add that reference then - not assumed
here. This also differs from the slice 1 design doc's original sketch
(`Diagnostics → Core, Hardware, Monitoring`, no `Persistence`): `Persistence`
was added because the crash-checkpoint requirement (spec §2.5/§8) belongs to
the engine itself, not the Desktop layer, and `Hardware` was dropped because
nothing in this slice's executor needs it.

The slice 1 layers (`Core`/`Hardware`/`Monitoring`/`Persistence`/`Desktop`)
match that slice's own design document exactly. The provider contracts
`PollingEngine` polls against (`ISensorProvider`, `PollRequest`, `PollResult`,
`IInventoryProvider`) live in `Mazesta.Core.Providers`, so `Mazesta.Monitoring`
needs no hardware reference and stays on the cross-platform `net10.0` TFM.
Between tasks 12 and the slice 1 final review those contracts sat in
`Mazesta.Hardware`, which forced `Monitoring` onto `net10.0-windows`; the
final fix wave moved them to `Core` and restored the designed graph.

## Deferred to slice 2

- **§5.3 inventory ↔ sensor-node join.** WMI inventory (`HardwareInventory`)
  and the LHM sensor tree (`HardwareNode`) are both read and both shown, but
  they are not joined into one object: the dashboard renders inventory
  fields, the monitoring page renders sensor nodes, and no storage node
  carries its WMI model/serial/firmware inline. Slice 1 ships the RAM card's
  total and module list from the inventory only. The join needs a stable key
  for NVMe devices, which this OS does not provide through WMI (see
  `docs/HARDWARE-MATRIX.md`, NGUID vs vendor serial), so it is deferred
  rather than guessed.

## Future projects (not yet created)

Two more projects are part of the overall design but are created by their
own later slices, not this one:

- `Mazesta.Reporting → Core`
- `Mazesta.Monitor` (tray edition) `→ Core, Hardware, Monitoring, Persistence`,
  and never `Desktop` or `Diagnostics`

## Test projects

Each `src` project has a matching project under `tests/` (`Mazesta.Core.Tests`,
`Mazesta.Hardware.Tests`, `Mazesta.Monitoring.Tests`,
`Mazesta.Persistence.Tests`, `Mazesta.Diagnostics.Tests`, `Mazesta.Desktop.Tests`),
referencing the same dependencies as its production project plus xunit. Each production project
grants
`InternalsVisibleTo` to its own test project only. `Mazesta.Hardware.Tests`
additionally carries tests marked `[Trait("Category","Hardware")]` that touch
real hardware and are excluded from the default `dotnet test` run via
`--filter "Category!=Hardware"`.
