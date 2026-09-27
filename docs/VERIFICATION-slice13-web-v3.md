# Verification: slice 13, web edition v3

Branch `slice-13/web-v3`. Checked on 2026-09-28 on the owner's machine (Ryzen 9 3950X, ASUS PRIME B550M-A, RTX 3090, Windows 11 26200), this
time from an **elevated** shell, so the Release build ran with real sensors.

## Verified
- `dotnet build Mazesta.sln -c Release`: 0 warnings, 0 errors. `dotnet test --filter "Category!=Hardware"`: all pass (595), including the new
  tests for the added sensor roles, the diagnostics notes, VRAM and link speed parsing, pinned drive items and overlay order, the page file
  plan checks and the hosts file (check, save with backup, read-only flag, no BOM).
- Sensor values read live with LibreHardwareMonitor to find why sensors never read a good value: the DIMM thermal limits are 0 (not
  programmed), the Samsung T7's composite temperature is 0 (USB bridge), and "Network Utilization" is NaN on adapters without a link.
- The published Release build (elevated, `MAZESTA_DEVTOOLS_PORT` for inspection):
  - the diagnostic findings went from **29 to 0**; four notes remain (T7 temperature, three adapters without a link);
  - the inventory shows the RTX 3090 with **24 GB** (was 4 GB) and a disconnected adapter's speed as not available (was 9223372036855 Mbps);
  - Windows tools read hibernation (on), Fast Startup (off), the page file (C:, 8000-150000 MB, not managed by Windows), the fixed drives,
    and the hosts file; the page renders as panels;
  - the overlay switched to two columns with a drive's own block (MSI M390 read/write) and the order set on the page, captured on screen
    as a real WPF window; the owner's overlay settings were restored afterwards (render preset, one column, hidden);
  - settings show the findings and the notes apart;
  - the overlay's chart caption reads "بیشینه 57 °C" in order (it was reordered to "C° 57 بیشینه"; the value is now its own left-to-right run);
  - closing the window left no MazestaWeb or WebView2 process.
- Browser preview (demo host): the Windows tools, gaming, tuning, reports and settings panels, the overlay order panel (arrow buttons and
  drag and drop both reorder blocks and items; the preview follows), the two-column preview.

## Not verified
- The changes themselves were **not** applied to this machine: hibernation was not switched, the page file was not changed, the hosts file
  was not saved (they change the system; the owner does that from the page). Their logic is covered by unit tests only, and the WMI write of
  the page file has not run anywhere.
- The ASUS board's six temperature channels ("Temperature #1-#6") keep LibreHardwareMonitor's generic names: naming them needs the same
  evidence the fan names had (a side-by-side reading with HWiNFO), so they were not guessed.
