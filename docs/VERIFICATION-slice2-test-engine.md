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
| **Mazesta.Diagnostics.Tests (new this increment)** | **10** | **0** | **0** | **10** |
| Mazesta.Monitoring.Tests | 35 | 0 | 0 | 35 |
| **Total** | **265** | **0** | **0** | **265** |

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

## 5. What was **not** verified this session

- **No GUI screenshot.** `TestCenterView.xaml` was not visually inspected running - no screenshot of the
  queue row, the progress bar, the incomplete-session banner, or the RTL/Persian rendering of the new
  strings. The published exe also carries `requireAdministrator`, and this session had no way to satisfy
  a UAC prompt, so it was not launched at all.
- **No live/elevated run of the CPU test against real hardware or real sensors.** `CpuMatrixStressExecutorTests`
  exercises the executor for real (genuine parallel computation, genuine timing, genuine verification),
  but always with `Engine: null` - the `DescribeMeasuredLoad` path that reads real `PollingEngine` history
  for the "measured CPU load avg …%" detail string has unit coverage of its null/empty-sample branches
  only (implicitly, via the `Engine: null` tests), not a run against a live provider.
- **No manual test of navigating away from Test Center mid-run and back**, the scenario
  `TestEngine.RequestCancel`'s ownership design and the design doc's §5 note are both about. The
  `TestEngineTests` cover cancellation and checkpoint detection at the engine level with fakes; the
  Desktop-layer VM-reconstruction behaviour itself was not clicked through.

These gaps are recorded here rather than implied away by the passing test count above. Closing them
needs a person at a Windows session that can accept the UAC prompt and take screenshots - this session
had neither.
