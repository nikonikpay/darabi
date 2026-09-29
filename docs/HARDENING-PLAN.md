# Hardening plan: status of the external review (2026-09-30)

The owner had the project reviewed against other diagnostic tools (a Persian brief, "دستورکار کامل توسعه و اصلاح Mazesta Test"). Each item
below was checked against the code on `slice-13/web-v3` before anything changed. This file is the plan's task list: an item is **done** only
when it is in the code with tests, **partial** says exactly which part, **open** is not started, and **declined** says why it was not taken.

Status: `done` · `partial` · `open` · `declined`. "Hardware" means it needs the real machine to verify, and was not run in this round.

## Result model and infrastructure (plan §2, §3)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| H-01 | A program error is never a hardware fault | done | `TestOutcome.Error`: an executor's exception becomes Error (no error counted against the part), the queue goes on, repeats stop. Report shows "test error (not a hardware fault)" and the verdict is Incomplete. |
| H-02 | "Ran without error but did not cover what was asked" is not a pass | done | `TestOutcome.Inconclusive`, used by single-core cycling (a core not reached or not pinned) and the ping test (no reply at all). Report verdict Incomplete. |
| H-03 | Three independent axes (run state / diagnosis / coverage) | partial | The two outcomes above cover the cases the code can produce today. A separate coverage field with a measurable denominator is open; no percentage is shown where there is no denominator. |
| H-04 | Checkpoint not marked complete after a fault | done | Completed only when the queue reached its end; a fault leaves it at the item it was on (test). |
| H-05 | Per-step progress in the checkpoint; crash vs timeout vs device removal | open | Needs a checkpoint schema v2 and a watchdog; GPU tests already put the exception's type and message in the detail, which is where a removed device shows today. |
| H-06 | Benchmark result filed under the options it started with (§3A) | done | `RecordedBenchmark.Options` is a copy taken at start; record key, measured part and comparison log use it; the overclock mark is the one at start; a running row's options cannot change. |
| H-07 | One load at a time: tests, benchmarks, GPU tuning (§3B) | done | `WorkloadGate`; each refuses to start while another holds it and says which. The combined CPU+GPU power test is one test, so unaffected. |
| H-08 | GPU sensors of the tested card only; stale readings rejected (§3D) | done | `GpuDevices.SensorNode` (by name; the only GPU; ambiguous → nothing). `SensorEvidence.Latest` ignores readings older than 10 s. Used by the VRAM budget and the GPU tests' evidence. |
| H-09 | VRAM budget from the OS residency budget (DXGI QueryVideoMemoryInfo) | open | Today: the card's own free-VRAM sensor, else 60 % of dedicated. The DXGI budget needs the adapter's LUID path; worth doing, not started. |
| H-10 | Record versioning (§3E) | done (already) | `HeadlineMetric.Version` is in the record and comparison table keys; raising it starts a new list. No change needed. |

## CPU (plan §5)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| C-01 | Matrix test checked 4 of 4096 cells | done | Four fixed seeded input sets; every product's full bit checksum must equal a precomputed constant (pinned by a test). One flipped bit in one cell is caught (test). GFLOPS reported (2n³). |
| C-02 | Vector test used its own first block as reference; only a lane sum was compared | done | Each lane compared with a scalar reference (`Math.FusedMultiplyAdd`, or mul+add for SSE2) computed before the run; wrong and non-finite lanes counted per lane (tests for a one-bit lane fault and NaN/∞). |
| C-03 | Core cycling: coverage, affinity failure, first core as reference | done | Precomputed reference; pin confirmed with `GetCurrentProcessorNumberEx`; any core not tested → Inconclusive, naming the cores (test). |
| C-04 | Linpack formula and threshold | declined (no change) | Already HPL's scaled residual `‖Ax−b‖∞ / (ε·n·(‖A‖∞‖x‖∞+‖b‖∞)) < 16` and `(2/3)n³+2n²`; the bit-for-bit comparison with iteration 1 is an extra check next to the independent residual, so it stays. |
| C-05 | More workloads: integer, FFT with independent check, hash/compression, P/E and processor-group coverage report, idle/load cycling | open | Each is a new executor with its own reference; not started. |

## Memory (plan §6)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| M-01 | More pattern families | partial | Added all-zeros, all-ones and inverse address (23 patterns). Moving inversions, block move, stride/modulo and bit fade are open. |
| M-02 | Honest scope: virtual offsets, not physical addresses or DIMMs; Windows-only share of RAM | done | Stated in the result and the log. |
| M-03 | Rowhammer as a simple loop | declined | A loop in Windows cannot hammer chosen physical rows; it would be a fake test. An offline tool (MemTest86) is the right path; importing its result is open. |
| M-04 | STREAM-style benchmark (Copy/Scale/Add/Triad, byte-count convention), latency by pointer chasing | open | The memory benchmark measures write/read/copy (payload convention). A STREAM set needs a new benchmark version. |

## GPU (plan §7)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| G-01 | An error in an intermediate dispatch was overwritten | done | Dispatches in a batch chain on the previous output; sampled threads are recomputed on the CPU through the whole chain (parallel). The result says how many thread results were verified, as a sample (test for a fault in any dispatch). |
| G-02 | VRAM: every allocated cell checked | done (already) | The comparison runs on the GPU over every cell with an atomic counter. Walking bits and stride patterns are open. |
| G-03 | Garden benchmark: fixed path, capture outside FPS, 1 % low definition | open | Not reviewed in this round. |
| G-04 | Real AI inference (image/detection/text models with accuracy) (§8) | open | Needs model files, their licences and an inference runtime (ONNX Runtime/DirectML adds tens of MB). An owner decision first. The existing benchmark stays "matrix multiply throughput", not an AI score. |

## Storage and network (plan §9, §10)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| S-01 | Latency: whole run, read/write apart, P99.9 only with enough samples, not 1/IOPS | done | `LatencyHistogram` (5 % log buckets, exact mean/max); tests. |
| S-02 | Short reads, wrong bytes counted apart | done | Short read = one error of its own kind, never a full block. |
| S-03 | NVMe health log (critical warning, spare, media errors, error log), before/after snapshots | open | Needs `IOCTL_STORAGE_QUERY_PROPERTY` NVMe log pages; today SMART health comes from WMI (MSFT_StorageReliabilityCounter). |
| S-04 | I/O matrix profiles, OS-like mixed profile, storage event log (129/153/157) with payload | open | |
| S-05 | Data safety: only a new file, CreateNew, delete on close, free-space margin | done (already) | `StorageFile`; nothing raw, no format/erase. |
| N-01 | Blocked ICMP is not a NIC fault; jitter from one sample is not 0 | done | No reply → Inconclusive with a hint; jitter needs two replies (test and internet benchmark). |
| N-02 | LAN peer, TCP/UDP, loaded latency | open | |

## Profiles, cause analysis, other parts (plan §10–§12)

| ID | Item | Status |
|---|---|---|
| P-01 | Profiles (quick, shop standard, deep, transient, OS storage, comparable benchmark) with target length and minimum coverage apart | open |
| P-02 | Report: observation / likely causes / next test to tell them apart | open |
| P-03 | Capability matrix for fans, battery, display, audio, USB, PCIe, PSU (manual or evidence-only where no instrument exists) | open |

## Asked for alongside the plan: the live test monitor

Done. Start on the Tests page switches to Monitoring once the queue runs. A panel on top shows the test (n of m) in its part's colour, its
progress, elapsed time and outcome, Cancel, a way back, and a log of what runs: each step in the page's language with the formula it checks or
the command it runs (sfc/DISM print their own lines there). "Follow the tested part" (on by default) opens that part's sensor panels, folds the
others, scrolls to it and charts its key readings, and moves on as the queue does. The engine keeps the last 500 lines, so a page opened
mid-run shows what came before.

## Verification of this round

- `dotnet build -c Release`: 0 warnings. Non-hardware tests: all pass, with new tests for every fix above (fault injection for C-01, C-02,
  G-01, the outcomes, the checkpoint, the gate, the option snapshot, the GPU sensor mapping, the histogram).
- Browser preview (demo host): the live panel, the log with formulas, following the storage part, the Tests page's live-monitor button.
- **Not verified on the machine:** the GPU chain change and the VRAM budget with real cards (hardware tests were not run: they load the GPU);
  a full queue in the published app with the live monitor (it needs the elevated app and several minutes of load, left to the owner).
  `ZzRenderSnapshots.cs` and `ZzRenderSlice10.cs` (untracked WPF-era helpers that no longer compile) were renamed to `.cs.stale`, not deleted.
