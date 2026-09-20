# Mazesta Test — guide for AI coding agents (Codex, Claude Code)

Persian-first (RTL) WPF / .NET 10 diagnostics suite for a PC service shop: live sensor monitoring, hardware tests, reports, and a low-footprint tray monitor.
Owner: Saeed Darabi (`saeed-darabi`). `CLAUDE.md` imports this file, so there is one source of truth.

## Build and test
```
dotnet build Mazesta.sln -c Release
dotnet test  Mazesta.sln -c Release --no-build --filter "Category!=Hardware"
```
- `TreatWarningsAsErrors` is on: a warning is a failed build. Packages are versioned centrally in `Directory.Packages.props`.
- Tests marked `Category=Hardware` need the real machine, admin rights and drivers. Do not run or change them unless the task says so.
- Do **not** change the Desktop target framework to a `net10.0-windows10.0.xxxxx` version (it pulls a 23 MB WinRT projection). Keep `net10.0-windows`.

## Layers (dependencies point down only)
| Project | Role |
|---|---|
| `Mazesta.Core` | Pure types: sensors, units, grouping, inventory models, health rules. No UI, no I/O. |
| `Mazesta.Hardware` | LibreHardwareMonitor provider + WMI inventory. |
| `Mazesta.Monitoring` | Polling engine, history store, statistics. |
| `Mazesta.Persistence` | `AppConfig`, `JsonStore`, schema migrations, `AppPaths`. |
| `Mazesta.Diagnostics` (+ `.Gpu`) | Test engine and executors (CPU, RAM, storage, network, GPU/DX12). |
| `Mazesta.Reporting` | Report model, JSON/HTML writers, on-disk store. UI-free. |
| `Mazesta.Desktop` | WPF app (MVVM, CommunityToolkit.Mvvm, DI). Also prints PDF through WebView2. |
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
- The app is Persian by default; English is being added. Every user-visible string is a key in **both** `Localization/Strings.resx` (English) and `Strings.fa.resx` (Persian). Use `{loc:Loc Key}` in XAML and `Loc.Get/Format` in code.
- The bundled IRANSansXFaNum font renders ASCII digits as Persian digits. Anything numeric that must stay Latin (sensor values, ports, versions, chart axes) uses `App.Font.Latin` / the `Text.Value` and `TextBox.Value` styles.
- Layout must work in RTL and LTR. Never hard-code left/right where `FlowDirection` should decide.

## Working on the UI on the owner's machine
- The app auto-elevates (UAC). UI Automation from a non-elevated shell sees nothing.
- WPF hardware rendering is broken system-wide on the owner's PC (white windows). The setting `renderMode: "software"` (Settings page or `appconfig.json`) fixes it; keep it working.
- Idle cost matters: animations only on opacity/transform, nothing animating while the window is hidden, no timers that wake without need.

## Git workflow
- Never push to `main`. One branch per task: `codex/<short-task>` for Codex, `slice-N/<name>` for Claude. Small commits, Conventional Commits (`feat(reporting): …`, `fix(hardware): …`, `test: …`, `docs: …`).
- Git identity is repo-local (`saeed-darabi`); do not change it.
- Do not touch files another open branch is changing; check `docs/CODEX-TASKS.md` for who owns what.
- Before a PR: build, run the non-hardware tests, and say plainly in the PR what you did **not** verify.
- Runnable build: `dotnet publish src/Mazesta.Desktop -c Release -o artifacts/Mazesta-Test` (delete the folder first). `artifacts/` is not committed.

## Where things are documented
`docs/ARCHITECTURE.md`, `docs/VERIFICATION-*.md` (what was actually verified, on real hardware), `docs/GUIDE-FA.md`, `docs/CODEX-TASKS.md` (the task board).
