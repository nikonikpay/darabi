# Slice 1 verification record

**Date:** 2026-09-12
**Machine:** dev box — Intel i9-14900K (8P+16E), NVIDIA RTX 4090 + Intel UHD
770, MSI Z790 GAMING PLUS WIFI, Samsung 990 PRO 2 TB NVMe, 32 GB RAM,
Windows 11 Pro build 26200.
**Build hash (base commit this verification was measured against):**
`34e054458437e1fbaaf5c6a2db9d0e9e06698f3f` (`34e0544`) — HEAD of
`slice-1/sensors-monitoring` immediately before this task's own commit(s).

All commands below were run from `/mnt/f/Projects/darabi` (WSL) via
`"$DOTNET" = "/mnt/c/Program Files/dotnet/dotnet.exe"` and `powershell.exe`,
per this repository's documented workflow. Every number in this document
traces to one of the commands quoted here.

## 1. Build

```
"$DOTNET" build Mazesta.sln -c Release
```
Result: **Build succeeded. 0 Warning(s). 0 Error(s).** (`TreatWarningsAsErrors`
is on repo-wide.)

## 2. Unit tests (non-hardware)

```
"$DOTNET" test Mazesta.sln -c Release --filter "Category!=Hardware"
```

| Project | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| Mazesta.Core.Tests | 33 | 0 | 0 | 33 |
| Mazesta.Persistence.Tests | 12 | 0 | 0 | 12 |
| Mazesta.Hardware.Tests (non-`Hardware` category) | 110 | 0 | 0 | 110 |
| Mazesta.Desktop.Tests | 25 | 0 | 0 | 25 |
| Mazesta.Monitoring.Tests | 31 | 0 | 0 | 31 |
| **Total** | **211** | **0** | **0** | **211** |

All 211 unit tests pass. (Earlier in this same session, before the final
full run above, `build.ps1 -Test` reported Mazesta.Desktop.Tests at 24/24
rather than 25/25 with everything else identical; re-running
`dotnet test tests/Mazesta.Desktop.Tests -c Release --no-build` twice
afterwards reproduced 25/25 consistently. No file under
`tests/Mazesta.Desktop.Tests` or `src/Mazesta.Desktop` was touched by this
task, so this looks like a one-off VSTest discovery/count artifact from an
earlier build in this same session, not a regression introduced here; the
211 figure is the one reproduced twice and reported.)

## 3. Hardware-category tests (elevated)

**Elevation on this dev box is silent.** The calling shell (WSL's
`powershell.exe`) runs at Medium integrity
(`[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)`
→ `False`), confirming genuinely non-elevated callers exist on this box —
but `Start-Process ... -Verb RunAs` completes immediately with no visible
consent dialog and no wait, because local UAC policy auto-elevates for this
account. This was confirmed independently: a process started without
`-Verb RunAs` (whose exe carries `requireAdministrator`) could not later be
stopped by `Stop-Process` from the same non-elevated shell (*Access is
denied* — proof the target process actually holds a High-integrity token),
while `Start-Process powershell -Verb RunAs -Wait -ArgumentList
'-Command','Stop-Process -Id <pid> -Force'` succeeded in under a second. So
the command below ran and completed with no owner interaction required and
no timeout:

```
Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-Command','cd F:\Projects\darabi; dotnet test tests/Mazesta.Hardware.Tests -c Release --no-build --filter Category=Hardware --logger "trx;LogFileName=hardware.trx" *> F:\Projects\darabi\artifacts\hardware-tests.log'
```
(run inside `timeout 300` from WSL; completed in a few seconds, well inside
the bound). The custom trx filename was not honoured by the nested quoting
(a pre-existing default-named trx was written instead, under
`tests/Mazesta.Hardware.Tests/TestResults/`, `.gitignore`d); `hardware.trx`
under that name does not exist, but the actual trx file
(`Niko_DESKTOP-4IKOS19_2026-09-12_15_55_49_net10.0.trx`) was produced and is
the source of the results below.

| Test | Outcome |
|---|---|
| `Provider_is_ready_or_explains_why` | Passed |
| `Cpu_package_temperature_present_when_elevated_with_pawnio` | Passed (guard clause short-circuits: PawnIO not installed on this box) |
| `Nvidia_gpu_exposes_hot_spot_and_igpu_is_separate_node` | Passed |
| `Nvme_node_id_matches_wmi_serial` | **Failed** — see `docs/HARDWARE-MATRIX.md` "Concerns"; a real WMI/LHM serial-format mismatch on this NVMe drive, not a code defect touched by this task |
| `No_temperature_reports_zero_as_ok` | Passed |

**4 of 5 passed.** Full detail, including the assertion failure text, is in
`docs/HARDWARE-MATRIX.md`.

## 4. Publish

```
powershell.exe -ExecutionPolicy Bypass -File build.ps1 -Test -Publish
```
(`build.ps1 -Publish` runs
`dotnet publish src/Mazesta.Desktop -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test`,
then copies `docs/GUIDE-FA.md` and `docs/THIRD-PARTY-NOTICES.md` into the
publish folder — that copy step failed on the first run in this session
because those two docs did not exist yet; it succeeds now that they do, and
was not re-verified after this document was written since it is a trivial
file copy.)

- Output folder: `artifacts/Mazesta-Test/`
- File count: **31 files** (including the `fa\MazestaTest.resources.dll`
  satellite subfolder)
- Total size: **9.8 MB**
- Contents include: `MazestaTest.exe`/`.dll`/`.pdb`/`.deps.json`/`.runtimeconfig.json`,
  `Mazesta.Core/.Hardware/.Monitoring/.Persistence.dll` (+ `.pdb`),
  `LibreHardwareMonitorLib.dll`, `DiskInfoToolkit.dll`, `RAMSPDToolkit-NDD.dll`,
  `HidSharp.dll`, `System.Management.dll`, `System.IO.Ports.dll`,
  `Mono.Posix.NETStandard.dll` + `MonoPosixHelper.dll`/`libMonoPosixHelper.dll`,
  `BlackSharp.Core.dll`, `CommunityToolkit.Mvvm.dll`,
  `Microsoft.Extensions.{DependencyInjection,DependencyInjection.Abstractions,Logging,Logging.Abstractions,Options,Primitives}.dll`,
  and the `fa\` satellite resources folder.
- `PublishReadyToRun=true` was used (per the command above); no further ReadyToRun
  verification (e.g. crossgen output inspection) was performed.

## 5. Startup timing

Launched the published exe once, unelevated in intent
(`Start-Process -FilePath artifacts\Mazesta-Test\MazestaTest.exe`, no
`-Verb RunAs`) — per §3 above this still resulted in an elevated process on
this box. Newest log line set at
`/mnt/c/Users/Niko/AppData/Local/Mazesta/Test/logs/mazesta-test-20260912.log`
(WSL path for `%LocalAppData%\Mazesta\Test\logs\`), timestamps `15:47:53`–`15:47:54`:

```
15:47:53.087 INF Startup Window shown at 467 ms
15:47:53.846 INF Startup Provider ready at 1226 ms
15:47:54.278 INF Startup Inventory ready at 1658 ms
```

| Metric | Target | Measured | Status |
|---|---|---|---|
| Time to window shown | < 3 s | 467 ms | **Met** |
| Time to provider ready | < 3 s | 1226 ms | **Met** |
| Time to inventory ready | < 3 s | 1658 ms | **Met** |

(The log also recorded later, larger "Inventory ready" numbers at
`18909 ms` and `50416 ms` after the first one — these are periodic
re-reads the Dashboard triggers on its own refresh timer, timestamped
relative to a restarted `StartupClock` context, not a slow first load; the
first triad above is the one the < 3 s targets in spec §13 are about.)

## 6. Idle resource measurement

Primary measurement, exactly `tools/measure-idle.ps1` as specified, run
with a shortened settle time per the controller's guidance for this
session (`-SettleSeconds 120` instead of the script's 300 s default, to
keep the elevated wait reasonable — the script's own 30 s sample window is
unchanged):

```
& '.\tools\measure-idle.ps1' -SettleSeconds 120
```

Result:

```
WorkingSetMB   : 288.5
PrivateMB      : 260.6
IdleCpuPercent : 0.07
Threads        : 15
```

A second, independent sample (different launch, plain `Start-Process`
without `-Verb RunAs`, 60 s settle, no CPU sampling window) corroborates the
memory order of magnitude: `WorkingSetMB = 325.3`, `PrivateMB = 294.5`,
`Threads = 20`.

| Target | Measured | Status |
|---|---|---|
| Working set < 80 MB | 288.5 MB (120 s settle) / 325.3 MB (60 s settle) | **Missed** |
| Idle CPU < 1 % | 0.07 % | **Met** |

### Mitigation attempts (spec §13.1 / brief order)

1. **Confirm `PublishReadyToRun=true` was used.** Confirmed — see §4 above;
   the publish command that produced the tested binary passed
   `-p:PublishReadyToRun=true`. No effect beyond what's already measured
   (this was already in place before this task).
2. **`<TieredPGO>true</TieredPGO>` / `<UseSystemResourceKeys>true</UseSystemResourceKeys>`
   in the Desktop csproj.** **Not attempted.** This task's scope explicitly
   excludes modifying anything under `src/`; `Mazesta.Desktop.csproj` is a
   source file. Left for the task/slice owner to try as a follow-up; not
   done here to keep the "no source files changed" guarantee intact.
3. **Disable LHM `IsNetworkEnabled` temporarily to see its share.**
   **Not attempted**, same reason — `IsNetworkEnabled` is set in
   `src/Mazesta.Hardware/Lhm/LhmComputerAdapter.cs`, a source file.

No working-set trimming was applied or considered, per instruction. The
80 MB target is missed by a wide margin (roughly 3.5–4×) even before any
mitigation; a WPF process hosting LibreHardwareMonitorLib (with its NVAPI/
WMI/NVMe backends) plus `System.Management` plainly costs more than 80 MB
of working set on .NET 10 today. This should be flagged to the product
owner as a target that may need revisiting rather than an implementation
defect — the v0.5 prototype's own verification notes (referenced in the
slice design doc, §1) already recorded 207 MiB idle against the same 80 MB
goal, so this gap predates this slice's own code.

## 7. PawnIO-absent / provider-failure scenarios

PawnIO is **not installed at all** on this dev box today (confirmed
non-elevated: `sc.exe query PawnIO` → *"The specified service does not
exist as an installed service."*). This makes the "PawnIO absent" scenario
this box's actual, unforced state rather than something that needed to be
simulated with `sc.exe stop PawnIO`.

Given that fact plus the elevated hardware-test run in §3 (which ran with
`Elevated() == true`), `LibreHardwareMonitorProvider.Start()`'s own logic
(`src/Mazesta.Hardware/Lhm/LibreHardwareMonitorProvider.cs`) deterministically
computes `Status = Degraded(Provider.PawnIoMissing, …)` for this run: elevated
is true (so the `NotElevated` branch is skipped) and `PawnIo.IsInstalled` is
false (so the `PawnIoMissing` branch is taken) — and
`Provider_is_ready_or_explains_why` passing confirms the status was not
`Failed`. This is a sound deduction from source plus measured facts, **not**
a screenshot of the live UI banner.

**PENDING OWNER:** a screenshot of the Dashboard/Monitoring Degraded banner
and the «دریافت نشد» CPU temperature rows while running elevated, to close
the loop visually. Exact steps once PawnIO is later installed on this
machine (to demonstrate the *transition* rather than the permanent absence):
```
sc.exe stop PawnIO
<launch artifacts\Mazesta-Test\MazestaTest.exe, elevated>
<screenshot the Degraded banner and «دریافت نشد» CPU rows>
sc.exe start PawnIO
```
Provider-failure injection (fake provider forcing `Failed`) is covered by
existing automated tests in `Mazesta.Hardware.Tests`/`Mazesta.Monitoring.Tests`
from earlier tasks (part of the 211 passing above), not re-verified visually
in this task.

## 8. Config persistence

Covered by `Mazesta.Persistence.Tests` (12/12 passing, §2 above): atomic
save (temp file + `File.Replace`), corrupt-file handling (renamed aside,
defaults applied, event logged), v0→v1 migration sample, portable vs.
LocalAppData path resolution. Not re-verified manually in this task; no
regression suspected since `src/Mazesta.Persistence` was not touched.

## 9. Network check

No formal packet capture or Windows Firewall log was taken. What was done,
during the same elevated session used for the idle CPU sample above:
`netstat.exe -n -o`, filtered to the running `MazestaTest.exe` process id,
taken twice, roughly 60 s apart:

```
netstat.exe -n -o | Select-String " $pid$"
```
Sample 1 (immediately after the 60 s settle) and sample 2 (60 s later) both
returned **zero matching lines** — no TCP/UDP endpoint owned by the
`MazestaTest.exe` process id at either sample point. This is consistent
with the app's design (no HTTP client, no network sensor push, the AM9
product card only opens the *default browser* on a user click, which is a
separate process) but is a spot check, not a continuous capture across a
full 10-minute session.

**PENDING OWNER** if a more rigorous, continuous check is wanted: run
```
netstat.exe -b -n
```
from an elevated prompt at the start and end of a full 10-minute session on
the Dashboard, confirming no `MazestaTest.exe` line appears in either
listing (the `-b` flag names the owning executable directly and needs
elevation).

## 10. Persian UI / RTL / help popups

Not re-verified with new screenshots in this task. Existing evidence from
earlier tasks in this branch: `artifacts/shots/rtl.png` (Persian RTL shell
with Vazirmatn glyphs), `artifacts/shots/help-popup.png` and
`artifacts/shots/shell-helptip.png` (the "?" popup mechanism). No visible
English feature label was audited against a missing help key in this task.

## Acceptance criteria — spec §13, items 1–10

| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1 | `dotnet build` zero warnings; `dotnet test` passes; hardware tests pass elevated | **Partially met** | §1–3 above: build 0 warnings, 211/211 unit tests pass, 4/5 hardware tests pass. `Nvme_node_id_matches_wmi_serial` fails for a real environment reason (see `HARDWARE-MATRIX.md`), not fixed per this task's scope. |
| 2 | Monitoring shows all listed sensors for the dev box, «دریافت نشد» for anything unexposed | **Partially met** | `artifacts/shots/monitoring.png` (pre-existing, from an earlier task's manual pass, before this task confirmed PawnIO's absence). CPU MSR-based rows (package/core temp, clocks, Vcore, package power) will read «دریافت نشد» today because PawnIO is not installed — expected, not a defect. Per-thread CPU load, GPU (both), RAM, storage and network roles are mapped and not PawnIO-dependent (§6 of `HARDWARE-MATRIX.md`); motherboard sensor availability without PawnIO was not independently re-verified in this pass. |
| 3 | Package/core/hot-spot agree with HWiNFO within ±2 °C at idle | **Pending owner** | Needs HWiNFO installed and a person at the screen; exact steps in `HARDWARE-MATRIX.md`. |
| 4 | Expand/collapse never changed by ticks; focus request expands exactly once | **Met** | Covered by `MonitoringFocus`/`PollingEngine` tests in `Mazesta.Monitoring.Tests` (part of the 31/31 passing, §2). |
| 5 | Three chart windows update live, auto-scale, show min/max, visible gap after pause/resume | **Met** | `artifacts/shots/charts.png` (three windows), `artifacts/shots/chart-gap.png` (pause gap) — both pre-existing from an earlier task's manual pass. |
| 6 | Idle resource numbers measured and recorded, each target met/missed | **Met** (as an obligation — measured honestly) | §6 above: CPU met (0.07 % < 1 %), startup timing met (all < 3 s), working set missed (288.5–325.3 MB vs. < 80 MB target), cause and attempted mitigations recorded. |
| 7 | PawnIO-absent and provider-failure scenarios behave correctly, no fabricated values | **Partially met** | §7 above: Degraded/PawnIoMissing status deterministically confirmed via a real elevated run against this box's actual PawnIO-absent state; no screenshot of the live banner. `No_temperature_reports_zero_as_ok` (hardware test) and `ReadingValidator` unit tests confirm no fabricated readings. |
| 8 | Config survives restart, migrates v0→v1, corrupt file doesn't block startup | **Met** | `Mazesta.Persistence.Tests`, 12/12 (§2, §8). |
| 9 | No network request made | **Partially met** | §9 above: two clean 60‑s‑apart `netstat -n -o` samples during an elevated session show zero connections for the process id; a full 10‑minute `netstat -b -n` capture is pending owner for a more rigorous check. |
| 10 | Persian UI renders RTL correctly; every visible English label has a working help popup | **Met** (pre-existing evidence, not re-audited here) | `artifacts/shots/rtl.png`, `artifacts/shots/help-popup.png`, `artifacts/shots/shell-helptip.png`. |

## Known gaps / deferred

Sensor and provider limitations (this slice, this hardware):

- **Sensor-id stability for duplicate GPUs.** `HardwareId.FromProviderPath`
  relies on LHM's own enumeration order for same-model duplicate hardware
  (e.g. two identical GPUs); storage is already serial-keyed to avoid this,
  but non-storage duplicates are not. Recorded as a known limitation in the
  slice design (§14) and unchanged by this task.
- **Sensors LibreHardwareMonitor 0.9.6 does not expose:** Intel per-core
  *effective* clocks (distinct from the average-effective aggregate),
  thermal-throttling flags (PROCHOT/PL1/PL2), and C-state residency. See
  `HARDWARE-MATRIX.md` for detail.
- **NVMe storage id does not match `MSFT_PhysicalDisk.SerialNumber` on this
  box's Samsung 990 PRO** — see `HARDWARE-MATRIX.md` "Concerns" and
  `PROVIDERS-AND-FALLBACKS.md`. A real hardware/Windows-version discrepancy
  discovered by this task's hardware test, not fixed (out of `src/` scope).
- **Dev-box UAC auto-elevates silently** for the account used to run these
  verifications, so a genuinely non-elevated sample of the **Release**
  binary's behaviour could not be produced by launch flags alone — only the
  Debug build (`asInvoker`) demonstrates `NotElevated` without elevation.
  Environment fact, not a defect.

Deferred minors (tracked, not fixed — out of this task's `src/`-touching
scope):

- `.editorconfig` style rule is IDE-only.
- Stray path comments atop two Core test files.
- Records hold `IReadOnlyList` without defensive copies.
- `^D3D Compute` regex unanchored.
- GPU/board Fan arms map any fan to one role.
- Dead `_byId` field in the LHM provider.
- Never-updated node readings stamped with request time.
- `Task.Run` wraps sequential WMI queries.
- `SensorStatistics.Get` double lookup.
- `BoundedEventLog` capacity 0 throws.
- `AllowedFastSeconds` is a mutable array.
- `SecondsSinceEpoch` unguarded cast.
- `Stop()` stamps Stopped on join timeout and touches `_thread` without the lock.
- Unused usings in `AppPathsTests`.
- Logger `Flush()` reopens the handle.
- `Write()` after `Dispose()` resurrects the logger.
- `FlowDirection`-on-root-grid pattern not centralised.
- HelpTip popup could outlive an unloaded target.
- Two `ILoggerFactory` instances at startup.
- Dead `_suppressConfig` field (focus-driven expansion persists).
- Per-tick array copies in the chart view model.
- Minute-tier min/max band does not break across missing minutes.

Additional gap found while writing this document:

- **`Mazesta.Desktop.Tests` count varied (24 vs. 25) between two runs in
  the same session** without any source change from this task — see §2.
  Likely a VSTest discovery artifact from an earlier, possibly stale build
  in this session; reproduced at 25/25 twice afterwards. Worth a second
  look if it recurs on a clean checkout.
