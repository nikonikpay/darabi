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

## Reports (`src/Mazesta.Reporting`, Reports page)
- When a test queue finishes, `ReportService` writes `reports/<date>-<id>/report.json` and `report.html` (self-contained: no scripts,
  no external requests, IRANSans embedded). The PDF is printed from that HTML with the system WebView2 (offline) on demand from the Reports page.
- Content: verdict (Passed only if every requested test ran and passed; cancelled/unsupported/never-run → Incomplete), counts, per-test outcome,
  duration, errors, options and measured evidence, key sensors (min/avg/max + chart with each test's span) from the monitor's recorded history
  for the run window, and the machine inventory. A sensor with no samples is left out; nothing is invented.
- **Verified live (2026-09-20):** all 11 tests, 3 s each, produced a 59 KB JSON, a 320 KB HTML (rendered correctly, RTL Persian) and a 265 KB PDF; the Reports
  page lists it with a Jalali date and verdict.
- Not yet: before/after comparison, benchmark section (no benchmarks exist yet), English wording, PDF page-level visual check (no PDF rasteriser on this box).
- Also fixed: a duplicate LHM sensor identifier crashed the Monitoring page (`ced5a78`).
