# Mazesta Test — Slice 2 (start): Test Engine + CPU Matrix Load

**Date:** 2026-09-18
**Status:** Implemented, build/tests verified; GUI not visually verified this session (see
`docs/VERIFICATION-slice2-test-engine.md`).
**Source spec:** «سند جامع ساخت سیستم تست مازستا» (`MAZESTA-COMPLETE-SPEC.fa-9-10-2026.md`, the owner's
copy outside this repo). Section references (§) point to that document. Slice order follows the slice 1
design doc §19: sensors/monitoring (slice 1, done) → **test engine** (this slice) → specialised tests →
alert rules → reporting → tray/Monitor edition → recovery/polish.

## 1. Scope of this increment

Slice 2 as a whole is "test engine + specialised tests" per the roadmap; this increment delivers the
**engine infrastructure** plus **one real, honest executor** end to end, rather than a wide but shallow
pass over every test family at once:

- A new `Mazesta.Diagnostics` project (`TestEngine`, `ITestExecutor`, `QueuedTest`, `TestRunResult`,
  crash checkpoint) - the piece every later specialised test plugs into.
- `Cpu.CpuMatrixStressExecutor`: a real CPU+memory workload (repeated dense matrix multiplication) with
  its own correctness verification, not a placeholder.
- The Test Center page (`Nav_Tests`, previously a `PlaceholderViewModel`) wired to the real engine:
  queue selection, duration/repeat configuration, live per-row progress, start/cancel, and a stale-
  checkpoint notice.

GPU/memory/storage/network executors, the OCCT-style Power test, alert-rule colouring, and the
customer-facing report are **not** in this increment - they are later Slice 2/3/4 work, same as slice 1
explicitly deferred them.

### Non-goals (explicitly out of scope for this increment)

Every specialised test other than the CPU matrix load; a real Linpack-equivalent (LU factorisation +
residual - spec §9 distinguishes this from a simple multiply, which is why the shipped executor is
named "matrix load", not Linpack); warning/critical colouring on test outcomes (still deferred, same
boundary slice 1 drew for sensor rows); a separate Test Progress page (kept on one Test Center page -
see §3); automatic resume of an incomplete session (detected and surfaced, not auto-resumed); reports of
any format.

## 2. TestEngine design

`Mazesta.Diagnostics.TestEngine` is a sequential async queue runner, not a dedicated background thread
like `PollingEngine`: there is no continuous cadence to own, only a plain pipeline over `ITestExecutor`
calls, so a CPU-bound executor does its own `Task.Run` internally and the caller (a WPF async
`RelayCommand`) awaits `RunAsync` without blocking the UI thread.

- **Registered as a singleton** in `Bootstrapper`, not created per page visit: a queue keeps running if
  the technician navigates away from Test Center and back. `TestEngine` owns its own
  `CancellationTokenSource` internally (`RequestCancel()`), not the caller, specifically so a *new*
  `TestCenterViewModel` instance (built fresh on re-navigation) can still cancel a run it did not start.
- **Repeat modes** (spec §2.5/§8 - "run in a LOOP … some faults don't show on first pass"): Once, Count
  (N iterations), Unlimited (loop until cancelled). A Cancelled or Unsupported iteration stops the loop
  immediately; a Failed iteration does not, so an intermittent fault a few loops in is still caught, with
  error counts accumulated across every iteration actually run.
- **Crash checkpoint** (spec §2.5/§8 - "after crash/reboot only incompleteness can be proven, not why"):
  written before every queue item and once more (`Completed = true`) when the queue ends, cancelled or
  not, under `AppPaths.SessionsDir/test-checkpoint.json`. `FindIncompleteSession()` returns non-null only
  when a checkpoint exists that never reached `Completed = true` - the honest signal that the process
  ended mid-queue. This increment surfaces that fact (Test Center banner, dismissible) but does **not**
  auto-resume - the spec explicitly warns against silently re-running as the default.
- **Distinct outcomes** (spec §8 - "cancelled, never-run, unsupported and failed must be distinct, never
  reported as a pass"): `TestOutcome.{NotRun, Queued, Running, Passed, Failed, Cancelled, Unsupported}`.

## 3. One page, not a separate progress page

Some earlier UI reference material (a separate, ChatGPT-built prototype outside this repo) splits Test
Center and Test Progress into two pages. This app keeps queue configuration and live per-row progress on
one Test Center page instead: the queue here is short (currently one test), so a second page would only
add navigation for no benefit yet. If a later slice's queue grows large enough that this stops being
true, splitting the page is a small, isolated follow-up - not a rework of the engine underneath it.

## 4. CpuMatrixStressExecutor

Repeated `NxN` (64×64) dense matrix multiplication per worker thread (default: one per logical
processor), for the configured duration. Real correctness check (spec §9 - "the test algorithm must
verify the correctness of the computation/memory, not just report a score"): after each multiply, 4
random result cells are recomputed independently via the same dot-product order and compared bit-for-
bit; because both computations use identical operation order, a healthy CPU/RAM must reproduce the exact
same `double` - any mismatch is real, observed corruption, counted as an error, not floating-point
rounding noise. Deliberately named "matrix load" (`Test_Cpu_Matrix`), never "Linpack" - spec §9 forbids
that label for a simple multiply; a genuine Linpack-equivalent (LU factorisation + residual) is left for
a later executor.

Passing is not just "it ran": when `PollingEngine` is available, `DescribeMeasuredLoad` reads the CPU
total-load sensor's already-recorded history for exactly the run's own time window and reports the
measured average into `TestRunResult.Detail` (spec §8 - "a speed score alone is not a health pass").
Returns null (never a fabricated number) when there is no engine, no such sensor, or no sample fell in
the window - the same "دریافت نشد" discipline slice 1 applied to sensor rows.

## 5. Deferred / known gaps

- A live, elevated, real-hardware run (see `docs/VERIFICATION-slice2-test-engine.md` §5) found and fixed
  two real bugs the unit suite could not see: `TestEngine` dropping `Detail` on a `Passed` outcome, and
  three `TestCenterView.xaml` TextBlocks whose visibility triggers had no path that ever made them
  visible. Both are fixed and re-verified live; see that document for the full account. Persian/RTL
  rendering of this increment's new strings, and the repeat/cancel/incomplete-session paths, are still
  only unit-tested, not clicked through - listed in that document's §6, not repeated here.
- `TestCenterViewModel` does not re-subscribe to a row's live progress if it is torn down and rebuilt
  mid-run (navigate away, then back): the engine keeps running and `IsRunning`/`State` sync correctly on
  reconstruction, but the specific row's percent/status resets to blank until the next event fires. Noted
  here rather than silently accepted; small enough to fix in a follow-up if it turns out to matter.
- Start's `CanExecute` does not react to per-row `IsSelected` changes (only to `IsRunning`): clicking
  Start with nothing selected is a harmless no-op rather than a disabled button. Deliberate scope cut,
  not an oversight - re-subscribing to every row's `PropertyChanged` for one button's enabled state was
  judged not worth it yet at one row.
