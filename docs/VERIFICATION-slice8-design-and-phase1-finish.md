# Slice 8 — design, service number, SMART, notices, Windows Tools and Gaming: verification

## What changed
- **Design:** coloured action buttons (Run/Start green, Cancel/Stop red, main action blue, Delete red outline) with an overlay hover;
  benchmark results as tiles; thin dark scroll bars; inventory values beside their labels; the Mazesta logo in the sidebar.
- **Service number** (spec 7.1): under the sidebar, saved with the settings, on every report (HTML, text, comparison) with the logo;
  two different service numbers are not compared (spec 4.4).
- **Final SMART re-check** (spec 4.2 item 11): Windows' health verdict and reliability counters per drive; Warning/Unhealthy or
  uncorrected errors fail it. Read only.
- **Notices and shortcuts** (spec 9.4): a notice per saved report or unfinished benchmark; Ctrl+1 ... Ctrl+0 open pages.
- **Windows Tools and Gaming** (spec 11): sfc, DISM check/repair with live output; power plans via powercfg; Game Mode and HAGS state;
  Windows' own dialogs for the rest. SFC and the DISM check are also tests, so Windows health reaches the report.
- **T5:** TestOptions, service-number settings, encoding guard for Persian strings.

## Verified (2026-09-25, owner's machine, not elevated)
- Build 0 warnings; 464 non-hardware tests pass.
- Pages rendered offscreen in Persian/RTL and looked at: Benchmarks, Tests, Storage, the whole window (sidebar, service number,
  notices), Windows Tools, Gaming. A sample report with the logo and service number was rendered with headless Edge.
- Real hardware, read only: three drives report Healthy (counters absent without admin, as designed); powercfg lists 5 plans with one
  active; the page file and HAGS read.

## Not verified
- sfc and DISM runs, and switching a power plan, from the elevated app (not run here: they need admin, and switching changes this
  machine's settings).
- The drive reliability counters with admin rights; notices and shortcuts in the running app; non-English Windows output of sfc/DISM
  (such output is reported as Unsupported with the tool's own lines).
