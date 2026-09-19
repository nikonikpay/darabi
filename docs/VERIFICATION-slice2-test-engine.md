# Slice 2 (test engine start) verification record

**Date:** 2026-09-18
**Scope:** `Mazesta.Diagnostics` (new project), `Cpu.CpuMatrixStressExecutor`, Test Center wiring in
`Mazesta.Desktop`. See the design doc for what this increment does and does not cover:
`docs/superpowers/specs/2026-09-18-mazesta-slice2-test-engine-design.md`.

This machine did not have the pinned .NET SDK (10.0.400, `global.json`) installed at the start of this
session; `Microsoft.DotNet.SDK.10` (10.0.401, `rollForward: latestFeature`) was installed via `winget`
with the owner's explicit approval before any of the numbers below were produced.

## 1. Build

```
dotnet build Mazesta.sln -c Release
```
Result: **Build succeeded. 0 Warning(s). 0 Error(s).** (`TreatWarningsAsErrors` is on repo-wide; this
increment tripped it twice during development - CS9124 on a primary-constructor parameter double-used in
`TestQueueRowViewModel`, and a duplicate `Border.Style` assignment in `TestCenterView.xaml` - both fixed
before this run.)

## 2. Unit tests (non-hardware)

```
dotnet test Mazesta.sln -c Release --filter "Category!=Hardware"
```

| Project | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| Mazesta.Core.Tests | 45 | 0 | 0 | 45 |
| Mazesta.Persistence.Tests | 15 | 0 | 0 | 15 |
| Mazesta.Hardware.Tests (non-`Hardware` category) | 112 | 0 | 0 | 112 |
| Mazesta.Desktop.Tests | 48 | 0 | 0 | 48 |
| **Mazesta.Diagnostics.Tests (new this increment)** | **13** | **0** | **0** | **13** |
| Mazesta.Monitoring.Tests | 35 | 0 | 0 | 35 |
| **Total** | **268** | **0** | **0** | **268** |

(13, not 10: one regression test after the live GUI run below found the `Detail`-on-`Passed`
bug - `A_passed_result_still_carries_its_executor_s_detail_text` - plus two after the /simplify review showed the same last-iteration-wins mechanism could hide a Failed loop: `TestRunResult.Combine` now folds repeat iterations by severity, covered by `A_failure_on_one_loop_is_not_hidden...` and `Cancelling_after_a_failed_loop_still_reports_Failed`.)

The 10 new tests: `TestEngineTests` (queue order, cancel-before-start leaves the not-yet-started
executor uncalled, missing-executor is Unsupported not an exception, Count repeat runs exactly N times
and sums errors, Unlimited repeat stops at cancellation instead of looping forever, incomplete-checkpoint
detection and dismissal) and `CpuMatrixStressExecutorTests` (zero/negative duration is Unsupported, a
real ~1 s two-thread run passes with measured iterations and reports progress, pre-cancelled run reports
Cancelled, and - the one worth calling out - `VerifySpotChecks_catches_a_deliberately_corrupted_cell`,
which corrupts a genuine 64×64 multiply's result and asserts the verification actually catches it. A
healthy run can never exercise that failure path on its own, so this test calls the (internal,
`InternalsVisibleTo`-exposed) `Multiply`/`VerifySpotChecks` methods directly against corrupted input
instead of only trusting a whole-`RunAsync` pass.

`Mazesta.Hardware.Tests` non-`Hardware` count rose from 110 (slice 1's last recorded number) to 112 on
this run; not investigated further here, as no file under `Mazesta.Hardware` or its tests was touched by
this increment - most likely two tests added on `main` between slice 1's verification and now.

## 3. Hardware-category tests

**Not run this session.** No elevated shell was available in this non-interactive environment (build/test
ran via the Bash tool, which is not elevated on this machine and cannot prompt for UAC). Nothing in this
increment touches `Mazesta.Hardware`, so no regression is suspected, but this is not the same as having
run `dotnet test tests/Mazesta.Hardware.Tests -c Release --filter Category=Hardware` elevated and
recorded the result, the way slice 1's verification did. **Pending owner** if that confirmation is
wanted.

## 4. Publish

```
dotnet publish src/Mazesta.Desktop -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
```
Succeeded, producing `MazestaTest.exe` and the `Mazesta.Diagnostics.dll` alongside the other layer
DLLs. Not re-inspected file-by-file (slice 1's verification did a full file/size accounting; this
increment only confirms the publish step itself still succeeds with the new project in the graph).

## 5. Live GUI run, on real hardware, after all the numbers above

An interactive Windows session turned out to be reachable this session after all (the owner's machine
auto-elevates UAC for this account, matching what slice 1's own verification recorded on its dev box).
The published exe was launched, driven with `System.Windows.Automation` from an **elevated** PowerShell
(UIPI blocks a non-elevated automator from an elevated target window - confirmed the hard way first:
`FindAll` returned 0 elements non-elevated, 152 elevated against the same window), and the run was
captured with real screenshots, not just log lines.

Machine: AMD Ryzen 9 3950X (16C/32T), NVIDIA RTX 3090, 64 GB RAM, Windows 11 Pro 10.0.26200 - a real,
different box from slice 1's Intel/RTX 4090 dev machine. Dashboard showed live, real numbers (413
sensors ready) before Tests was ever opened, so the sensor pipeline this increment builds on top of was
already confirmed working here, not assumed.

**Two real bugs were found this way and are fixed on top of the commit this document originally
described** (see the follow-up commit on this branch for the full diff/rationale):

1. **`TestEngine.RunQueuedAsync` dropped `Detail` on a `Passed` outcome.** The loop only copied
   `TestRunResult.Detail` across when an iteration's outcome was *not* `Passed`, so a passing run's own
   evidence (thread count, iteration count, measured CPU load) was silently discarded before it reached
   the ViewModel. Invisible to `TestEngineTests` because none of them asserted `Detail` on a `Passed`
   result - fixed alongside a new regression test that does.
2. **Three `TestCenterView.xaml` TextBlocks (Detail, ValidationError, error count) could never become
   visible.** Each one's style set the base `Visibility` to `Collapsed` and then had a `DataTrigger` that
   *also* collapsed it on the empty/zero case - there was no path left that ever set it back to `Visible`.
   Invisible to the unit suite because it is pure XAML trigger logic, not something a ViewModel test
   exercises. Inverted to the same visible-by-default/collapse-on-empty pattern already used correctly
   elsewhere in this same file (`BannerBorderStyle`).

**After both fixes, confirmed live**, with the duration set to 3 s via `ValuePattern` and Start invoked
via `InvokePattern` (both real UI Automation calls into the real running window, not a simulation): the
row's progress bar filled, CPU usage in the OS-level indicator rose to 100% during the run and returned
to idle after, the outcome read **Passed**, and the detail line under the row read
`matrix load 64x64; threads=32; iterations=122130; measured CPU load avg 64.9% (n=1)` - a real thread
count matching this box's 32 logical processors, a real iteration count, and a real load percentage read
back from `PollingEngine` history for the run's own time window, not a placeholder.

Screenshots are not committed to the repository (this session had no established `artifacts/shots/`
convention to follow for this increment and did not want to guess one); they exist only as this
session's own evidence. Re-capturing them for the repo, the way slice 1 committed
`artifacts/shots/*.png`, is a reasonable follow-up if the owner wants them on record here too.

## 6. What is still **not** verified

- **RTL/Persian rendering of the new strings.** The live run above was in English (`AppConfig.Language`
  default); the new Test Center strings' Persian rendering (`Strings.fa.resx`, `Help.fa.resx`) was not
  screenshotted, the way slice 1 specifically captured `fa-dashboard.png`/`fa-chart.png` for its own new
  strings.
- **Hardware-category tests were still not run elevated this session** (`dotnet test
  tests/Mazesta.Hardware.Tests -c Release --filter Category=Hardware`) - nothing in this increment
  touches `Mazesta.Hardware`, so no regression is suspected, but it was not re-confirmed either.
- **Navigating away from Test Center mid-run and back**, the scenario `TestEngine.RequestCancel`'s
  ownership design exists for, was not clicked through by hand - only covered at the engine level with
  fakes (`TestEngineTests`).
- **Repeat modes (Count/Unlimited) and Cancel were not exercised live** - the live run above used
  `Once`. `TestEngineTests` covers their logic with fakes; the live run only re-confirmed the `Once`
  path end to end.
- **The incomplete-session banner was not produced live** (would need killing the app mid-run and
  relaunching, deliberately, to leave a real stale checkpoint) - covered at the engine level by
  `FindIncompleteSession_reports_a_checkpoint_left_over_from_a_crash`.

These remaining gaps are recorded rather than implied away by the passing counts above.
