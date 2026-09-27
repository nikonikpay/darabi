# Verification: slice 12, web edition v2

Branch `slice-12/web-v2`. What was checked, and how, on 2026-09-28 (the owner's machine: Ryzen 9 3950X, RTX 3090, Windows 11 26200).

## Verified
- `dotnet build Mazesta.sln -c Release`: 0 warnings, 0 errors. `dotnet test --filter "Category!=Hardware"`: all pass (551), including the new
  tests for benchmark records, the overlay catalog and frame statistics, the overlay view model, the drive-attention rule, the tray check log
  and the hardware diagnostics report.
- In the browser preview (demo host, `?still`): dashboard part panels with folding details, shop and contact panels; part-coloured CPU,
  GPU and storage pages; monitoring groups, double-click charts stacked and coloured, the pop-out page (`chart.html`); tests and
  benchmarks in part groups with the record comparison; the overlay page (presets, custom state, items, preview); settings with the
  diagnostics column.
- The Debug build (runs as invoker, so CPU and board sensors are unavailable) started, fetched a real product from dfmrendering.com into
  `Data\cache\shop`, wrote `Data\logs\hardware-report.txt` and its warnings after 15 polls, logged no page or bridge error, and **exited
  completely when its window was closed** (no process left).

## Not verified
- The Release build (elevated) was not run from this non-elevated shell: the tray (bundling, summary window, drive-health checks,
  notifications), the WPF overlay window's new layout, the pop-out chart window as a real window, and the main app's Windows notifications
  were not seen on screen.
- FPS / 1% low: the ETW session needs administrator rights; it was not run against a real game. Vulkan and OpenGL games are not measured by
  design.
- Benchmark records were exercised by unit tests only, not by a real benchmark run.
- The pages were not checked at the 1024 px minimum window width in the real app.
