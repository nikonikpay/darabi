# Slice 3 — tray monitor and component tests: verification

## Tray monitor (`src/Mazesta.Tray`)
- Windowless NotifyIcon process, single instance per session (mutex). Requires administrator (LibreHardwareMonitor).
- Rules live in `Mazesta.Core.Health.HealthAlerts` (unit-tested): CPU/GPU > 95 °C for 3 samples, re-arm < 90 °C,
  CPU clock < 2000 MHz at ≥ 98 % load, repeat at most every 15 min. A missing sensor never raises or resets a rule.
- Schedule: first check 20 s after start, then every 10 min; every 30 s only while a rule is building up (`IsWatching`).
- The sensor provider is opened per check and disposed; memory is compacted and trimmed afterwards.
- **Measured (2026-09-20):** after the first check the process sits at ~16.6 MB working set (125 MB without the trim);
  one check costs ~2.3 s CPU in total, none between checks.
- Not yet done: start-with-Windows option (scheduled task), disk-health (SMART) watch every 24 h, settings for the intervals.

## Also in this slice
- Themed ProgressBar and CheckBox, fade-in on view load (opacity only).
- 81 Core tests (health rules and sampler added); all non-hardware suites pass.
