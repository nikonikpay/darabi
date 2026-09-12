# Architecture

Mazesta Test is a single elevated WPF process built from layered class
libraries. Each layer is a separate project so that dependencies can be
enforced by the project graph, not just by convention.

## Projects and dependencies

| Project | Purpose | Target framework | May reference |
|---|---|---|---|
| `Mazesta.Core` | Domain model: ids, units, roles, validation, `PersianDigits`, `IClock`. No Windows/WPF/LibreHardwareMonitor references. | net10.0 | — |
| `Mazesta.Hardware` | `ISensorProvider`, `IInventoryProvider`, `LibreHardwareMonitorProvider`, `WmiInventoryProvider`, role-mapping table. | net10.0-windows | Core |
| `Mazesta.Monitoring` | `PollingEngine`, `HistoryStore`, `SensorStatistics`, `StaleDetector`, `EventLog`, `MonitoringFocus`. | net10.0 | Core |
| `Mazesta.Persistence` | `AppPaths` (portable vs. LocalAppData), `JsonStore<T>` (atomic), `SchemaMigrator`, `AppConfig`, `RollingFileLogger`. | net10.0 | Core |
| `Mazesta.Desktop` | WPF app "Mazesta Test". Views, ViewModels, Controls (`TimeSeriesChart`, `HelpTip`), Localization, Theme. | net10.0-windows | Monitoring, Hardware, Persistence, Core |

## Allowed references

`Desktop → Monitoring, Hardware, Persistence, Core`; `Monitoring → Core`;
`Hardware → Core`; `Persistence → Core`. Nothing references `Desktop`.

## Future projects (not yet created)

Three more projects are part of the overall design but are created by their
own later slices, not this one:

- `Mazesta.Diagnostics → Core, Hardware, Monitoring`
- `Mazesta.Reporting → Core`
- `Mazesta.Monitor` (tray edition) `→ Core, Hardware, Monitoring, Persistence`,
  and never `Desktop` or `Diagnostics`

## Test projects

Each `src` project has a matching project under `tests/` (`Mazesta.Core.Tests`,
`Mazesta.Hardware.Tests`, `Mazesta.Monitoring.Tests`,
`Mazesta.Persistence.Tests`), referencing the same dependencies as its
production project plus xunit. Each production project grants
`InternalsVisibleTo` to its own test project only. `Mazesta.Hardware.Tests`
additionally carries tests marked `[Trait("Category","Hardware")]` that touch
real hardware and are excluded from the default `dotnet test` run via
`--filter "Category!=Hardware"`.
