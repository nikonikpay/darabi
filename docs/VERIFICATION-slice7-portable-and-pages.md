# Slice 7 — portable app, component pages, report languages and comparison: verification

## What changed
- **Portable only.** All data in `Data\` next to `MazestaTest.exe` (config, logs, sessions, history, reports, the PDF printer's
  WebView2 profile in `cache\`). The first start of a copy without `Data\` copies `%LocalAppData%\Mazesta\Test` in once and leaves
  it untouched. `portable.marker` and the installed mode are gone.
- **CPU, GPU, Storage, Network pages** (spec §9.2): inventory of the part, its live sensors (Monitoring tree limited to it) and its
  benchmarks. Gaming and Windows Tools stay placeholders (phase 2).
- **Internet speed benchmark** (spec §5.4): ICMP latency/jitter/loss to 1.1.1.1, then 6 parallel HTTPS streams down and up to
  speed.cloudflare.com, data used reported; Unsupported without a connection.
- **Reports**: Persian (RTL) or English (LTR) following the app language (T2); plain text `report.txt` (spec §7.4), written for older
  reports on first open; before/after page from two ticked reports of the same machine (spec §4.4) with tests, sensors and
  benchmark numbers; different machines are refused with the reason.
- **T1** English/Persian string parity + XAML key test; **T4** README, ARCHITECTURE, notices (ComputeSharp, WebView2, EventLog,
  Vortice), Persian guide.

## Verified (2026-09-25, owner's machine)
- Build: 0 warnings, 0 errors. Non-hardware tests: 436 passed.
- The parity test fails when a key is removed (Bench_Run taken out of Strings.fa.resx → "missing in Persian: Bench_Run").
- ComponentView loads and lays out on an STA thread with inventory, sensors and benchmark rows.
- speed.cloudflare.com `__down` and `__up` answer HTTP 200 from this network (1 KB / 10 B requests).
- The publish folder is 20 MB and contains no DirectML.dll; the AI benchmark ran on Windows' System32 DirectML (RTX 3090:
  FP32 15.5 TFLOPS, FP16 110 TFLOPS, INT8 12.6 TOPS).

## Not verified
- The new pages, the Compare button and the Text button on screen (the app elevates; checked by view-model and STA tests only).
- A full internet speed run (not run here, to avoid using the owner's data allowance) and its numbers against another speed test.
- The first-start copy of the existing `%LocalAppData%\Mazesta\Test` data on this machine (3 reports, config) — covered by a unit test.
- A comparison page opened in a browser or printed to PDF.
