# Slice 10 — yellow redesign, V/F curve, benchmark queue, customer summary, overlay

Branch `slice-10/yellow-redesign` (from `slice-9/gpu-tuning`). What was checked, and how.

## Measured on the owner's machine (RTX 3090, Ryzen 9 3950X), not elevated

GPU load of the automatic tuner (`ComputeGpuLoad`), 20 s runs, `nvidia-smi` sampled once a second, same card, before and after:

| Load | Before (fixed 16-dispatch batches) | After (`BatchSizer`, ~250 ms submissions) |
|---|---|---|
| Compute | 70–72 % GPU, ~308 W, 8803 Gop/s | 88–100 % (mostly 95–100), ~348 W, 12174 Gop/s |
| Memory | 50–53 % GPU, ~222 W, 209 GB/s | 98–100 %, ~280 W, 392 GB/s |

The 70 % the owner saw during undervolt/overclock was the host readback and CPU check between small batches, not the card.
Because the score changed, profiles now record `LoadVersion`; auto-overclock no longer reuses a stock measurement from the old load.

## Rendered and inspected (WPF RenderTargetBitmap / headless Edge), with fake data

- Shell: logo, yellow selected-page pill, RTL sidebar, toasts.
- Tuning page: live tiles, V/F curve (measured stock dashed, tuned yellow, cap line and tag, live point), sliders and switches,
  estimate chip, automatic-tuning cards, RTL step log with Latin numbers in their own runs.
- Benchmarks page: queue bar, tick boxes, running-row highlight, metric tiles.
- Customer summary: one A5 page (headless Edge print), boxed sections, Persian labels with intact "78 °C" runs.
- Overlay panel: GPU/CPU/RAM/NET blocks, sparklines, RTL rows.

## Unit tests

All non-hardware tests pass (`dotnet test Mazesta.sln -c Release --no-build --filter "Category!=Hardware"`). New: batch sizing,
V/F curve maths (no extrapolation), curve scanner (settled readings only, stops at the card's top, card left at stock, refusals),
latest-reading helper, benchmark queue (order, cancel skips the rest, one bad duration starts nothing), customer summary
(drive matching by serial, "not reported" never 0, A5/RTL), summary temperatures (stale reading is not "now"), overlay
(missing sensors left out, virtual switch excluded from traffic, hidden overlay ignores snapshots), mixed-script splitting.

## NOT verified yet (needs the elevated app on the owner's machine)

- The curve scan on the real card (NVML clock caps need administrator rights): whether LibreHardwareMonitor's GPU core voltage
  updates fast enough for ~2 readings per 5 s step, and how many points a 3090 reaches before the power limit.
- Applying a curve-editor setting and seeing the live dot land on the estimated voltage.
- The overlay over a real borderless game; Ctrl+Shift+O while a game has focus; overlay cost in software render mode.
- The customer summary PDF printed by WebView2 at A5 (headless Edge was used for the check) and on the shop's printer.
- The full app in `renderMode: software` after the theme change.
