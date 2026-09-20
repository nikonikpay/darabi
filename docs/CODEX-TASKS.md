# Task board for Codex

Read `AGENTS.md` first. One task = one branch `codex/<task-id>` = one PR into `slice-3/fa-ui-monitoring` (not `main`). Mark the status here in the PR.
Every task must end with: build passes, non-hardware tests pass, and a note in the PR of what was not verified.

Status: `open` · `in-progress (branch)` · `review` · `done`

## Safe to hand over (pure logic, tests, text, docs)

### T1 — Resource parity test · `open`
Add a test in `tests/Mazesta.Desktop.Tests` that loads `Strings.resx` and `Strings.fa.resx` and fails when a key exists in one and not the other, or when a Persian value is empty. Fix any drift it finds (add the missing entries; do not delete keys that are used).
Done when: the test exists, passes, and a deliberately removed key makes it fail (show that in the PR).

### T2 — English wording for the report · `open`
`src/Mazesta.Reporting/ReportText.cs` is Persian only. Make the wording selectable (`fa`/`en`) — an interface or a small record of strings passed to `ReportHtml.Write` — keep Persian as the default, set `lang`/`dir` on `<html>` from it, and let `ReportService` (Desktop) pass the app's current language.
Done when: HTML for `en` is LTR with English text; tests in `tests/Mazesta.Reporting.Tests` cover both languages; existing tests still pass.

### T3 — Report comparison, before vs after (logic only) · `open`
In `Mazesta.Reporting` add `ReportComparison.Compare(SessionReport before, SessionReport after)`: matches tests by `Id` and sensors by `Id`, returns per-test outcome change and per-sensor min/avg/max deltas. Compare only when both reports describe the same machine (same CPU name + motherboard + storage serials); otherwise return a result that says "not comparable" with the reason. No UI in this task.
Done when: unit tests cover same machine, different machine, a test present on one side only, and a sensor missing on one side (it must be reported as missing, never as 0).

### T4 — Documentation catch-up · `open`
Update `README.md`, `docs/ARCHITECTURE.md` and `docs/THIRD-PARTY-NOTICES.md` for what exists now: `Mazesta.Diagnostics.Gpu` (ComputeSharp), `Mazesta.Reporting`, `Mazesta.Tray`, WebView2 (PDF), the render-mode setting, the health rules in `Mazesta.Core.Health`. Only describe what the code does; read the code, do not guess.
Done when: every project in `Mazesta.sln` appears in the architecture table and every third-party package in `Directory.Packages.props` has a notice entry.

### T5 — Test gaps in existing code · `open`
Find public logic without tests (start with `Mazesta.Persistence` migrations, `TestQueueRowViewModel` validation, `SensorGrouping` edge cases, `TestOptions`) and add focused tests. Do not change production code except to fix a bug the tests expose — report such a bug instead of silently changing behaviour.
Done when: each new test names the behaviour it protects; list what was covered and what was left.

### T6 — Tray: start with Windows and interval settings · `open`
Design only the pure part first: a `StartupTask` helper that builds/parses the `schtasks` command line for "run `MazestaTray.exe` at logon, highest privileges", plus an `AppConfig` section for the tray's idle/watch intervals (with a schema migration and tests). Wire the tray to read them.
Done when: command-line building and config migration are unit-tested; registering the task is behind an explicit user action (no silent install).

### T7 — System Information page (port from the ChatGPT build) · `open`
A read-only page showing the machine inventory (`InventoryCache` already provides it): CPU, GPU + driver, RAM modules, board/BIOS, storage with health, network adapters (exclude virtual bindings with `NetworkAdapterFilter`), OS. Follow the existing page pattern (`ViewModels/*`, `Views/*`, factory in `Bootstrapper`, nav item). Use existing styles; Persian + English keys.
Done when: page opens from the nav, shows only real values (missing = "unavailable"), and a view-model test covers the mapping. Say in the PR that the visual check needs the owner's machine (software render mode).

## Keep with Claude (needs real hardware, drivers, live UI verification)
- Anything in `Mazesta.Hardware` / sensors / naming vs HWiNFO.
- GPU, memory, storage executors and their hardware tests; WHEA.
- Animated dashboard, gauges, idle-resource measurements, tray runtime behaviour.
- Benchmarks (not built yet) and how they enter the report.
