# Mazesta Test — guide for AI coding agents (Codex, Claude Code)

Persian-first (RTL) .NET 10 diagnostics suite for a PC service shop: live sensor monitoring, hardware tests, reports, and a low-footprint tray monitor. The interface is a local web page in one WebView2 (`Mazesta.Web`); the earlier WPF edition was retired on 2026-09-28 (git tag `wpf-edition-final`) and is not to be revived or extended.
Owner: Saeed Darabi (`saeed-darabi`). `CLAUDE.md` imports this file, so there is one source of truth.

## Build and test
```
dotnet build Mazesta.sln -c Release
dotnet test  Mazesta.sln -c Release --no-build --filter "Category!=Hardware"
```
- `TreatWarningsAsErrors` is on: a warning is a failed build. Packages are versioned centrally in `Directory.Packages.props`.
- Tests marked `Category=Hardware` need the real machine, admin rights and drivers. Do not run or change them unless the task says so.
- Do **not** change the Desktop or Web target framework to a `net10.0-windows10.0.xxxxx` version (it pulls a 23 MB WinRT projection). Keep `net10.0-windows`.

## Layers (dependencies point down only)
| Project | Role |
|---|---|
| `Mazesta.Core` | Pure types: sensors, units, grouping, inventory models, health rules. No UI, no I/O. |
| `Mazesta.Hardware` | LibreHardwareMonitor provider + WMI inventory. |
| `Mazesta.Monitoring` | Polling engine, history store, statistics. |
| `Mazesta.Persistence` | `AppConfig`, `JsonStore`, schema migrations, `AppPaths`. |
| `Mazesta.Diagnostics` (+ `.Gpu`) | Test engine and executors (CPU, RAM, storage, network, GPU/DX12). |
| `Mazesta.Reporting` | Report model, JSON/HTML writers, on-disk store. UI-free. |
| `Mazesta.Desktop` | The app layer, a library (assembly `MazestaTest`): composition/DI, the page view models the web bridge drives, report and PDF services, tray control, the string tables, and the one native window, the on-screen overlay. No pages of its own. |
| `Mazesta.Web` | The app (`MazestaWeb.exe`): the services of Desktop, interface drawn by one WebView2 from `wwwroot` (plain HTML/CSS/JS, no build step) through a fixed JSON bridge (`WebBridge`). Design rules in `DESIGN.md`. |
| `Mazesta.Tray` | Windowless tray monitor; opens the sensor provider only during a check. |

Put logic in the lowest layer that can hold it, so it is unit-testable without WPF. View models stay thin.

## Honesty rules (the product's core value — never break them)
- **No fake or guessed data.** A sensor or value that is unavailable is `null`/omitted, never `0` or a placeholder.
- A test that did not run is never a pass: `Cancelled`, `Unsupported`, `NotRun` are distinct and make a report *Incomplete*.
- A pass must be backed by measured evidence (see `SensorEvidence`), not by the test's own say-so.
- Sensor display names come only from verified catalogs (`SensorNameCatalog`); do not invent board-specific names.

## Code style
- File-scoped namespaces, 4 spaces, LF (`.editorconfig`). Match the surrounding code: compact style, several short statements per line where the neighbours do, terse `[Fact]` one-liners, `/// <summary>` only where it explains *why*.
- Comments explain intent and non-obvious constraints, not what the code does.
- No new dependency without a reason stated in the PR; add it to `Directory.Packages.props` and `docs/THIRD-PARTY-NOTICES.md`.

## Localization and RTL
- The app is Persian by default; English is being added. Every user-visible string is a key in **both** `Localization/Strings.resx` (English) and `Strings.fa.resx` (Persian). The page reads them through `t(key)` (the bridge sends the whole table); code uses `Loc.Get/Format`.
- Anything numeric that must stay Latin (sensor values, ports, versions, chart axes) is a `.num` / `.lat` run on the page (Archivo, left to right, isolated); see `DESIGN.md`.
- Layout must work in RTL and LTR. Never hard-code left/right where logical properties (`inset-inline-start`, `margin-inline-end`) or `FlowDirection` should decide.

## Working on the UI on the owner's machine
- The app auto-elevates (UAC). UI Automation from a non-elevated shell sees nothing.
- WPF hardware rendering is broken system-wide on the owner's PC (white windows). The window frame, its loading panel and the overlay are still WPF, so the setting `renderMode: "software"` (Settings page or `appconfig.json`) still matters; keep it working.
- Idle cost matters: animations only on opacity/transform, nothing animating while the window is hidden, no timers that wake without need.

## Git workflow
- Never push to `main`. One branch per task: `codex/<short-task>` for Codex, `slice-N/<name>` for Claude. Small commits, Conventional Commits (`feat(reporting): …`, `fix(hardware): …`, `test: …`, `docs: …`).
- Git identity is repo-local (`saeed-darabi`); do not change it.
- Do not touch files another open branch is changing; check `docs/CODEX-TASKS.md` for who owns what.
- Before a PR: build, run the non-hardware tests, and say plainly in the PR what you did **not** verify.
- Runnable build: `pwsh tools/publish.ps1` (publishes the app to `artifacts/Mazesta-Web`; the old `artifacts/Mazesta-Test` of the WPF edition is never touched, its `Data` stays). It refuses while the app runs, backs `Data` up to `../Mazesta-Data-Backups/<time>` (outside the repo, newest 20 kept), and deletes everything but `Data` before publishing. Never delete the folder by hand: the portable app keeps the owner's settings, reports and history in `Data`, which git does not hold. `artifacts/` is not committed.
- Push every commit to GitHub on its branch right away (the owner's backup); never to `main`.

## Where things are documented
`docs/ARCHITECTURE.md`, `docs/VERIFICATION-*.md` (what was actually verified, on real hardware), `docs/GUIDE-FA.md`, `docs/CODEX-TASKS.md` (the task board).
