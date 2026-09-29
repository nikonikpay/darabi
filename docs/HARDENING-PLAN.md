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
| H-05 | Per-step progress in the checkpoint; crash vs timeout vs device removal | partial | Checkpoint v2 (migrated from v1): the running test's round and progress (saved every 10 s) and every finished test's result; a broken-off session says where it stopped and what it had found. Telling a crash from a timeout or a removed device automatically is open; GPU tests put the exception in the detail. |
| H-06 | Benchmark result filed under the options it started with (§3A) | done | `RecordedBenchmark.Options` is a copy taken at start; record key, measured part and comparison log use it; the overclock mark is the one at start; a running row's options cannot change. |
| H-07 | One load at a time: tests, benchmarks, GPU tuning (§3B) | done | `WorkloadGate`; each refuses to start while another holds it and says which. The combined CPU+GPU power test is one test, so unaffected. |
| H-08 | GPU sensors of the tested card only; stale readings rejected (§3D) | done | `GpuDevices.SensorNode` (by name; the only GPU; ambiguous → nothing). `SensorEvidence.Latest` ignores readings older than 10 s. Used by the VRAM budget and the GPU tests' evidence. |
| H-09 | VRAM budget from the OS residency budget (DXGI QueryVideoMemoryInfo) | done | Capped at 90 % of DXGI's local budget for the tested adapter (found by LUID); checked read-only on the RTX 3090. |
| H-10 | Record versioning (§3E) | done (already) | `HeadlineMetric.Version` is in the record and comparison table keys; raising it starts a new list. No change needed. |

## CPU (plan §5)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| C-01 | Matrix test checked 4 of 4096 cells | done | Four fixed seeded input sets; every product's full bit checksum must equal a precomputed constant (pinned by a test). One flipped bit in one cell is caught (test). GFLOPS reported (2n³). |
| C-02 | Vector test used its own first block as reference; only a lane sum was compared | done | Each lane compared with a scalar reference (`Math.FusedMultiplyAdd`, or mul+add for SSE2) computed before the run; wrong and non-finite lanes counted per lane (tests for a one-bit lane fault and NaN/∞). |
| C-03 | Core cycling: coverage, affinity failure, first core as reference | done | Precomputed reference; pin confirmed with `GetCurrentProcessorNumberEx`; any core not tested → Inconclusive, naming the cores (test). |
| C-04 | Linpack formula and threshold | declined (no change) | Already HPL's scaled residual `‖Ax−b‖∞ / (ε·n·(‖A‖∞‖x‖∞+‖b‖∞)) < 16` and `(2/3)n³+2n²`; the bit-for-bit comparison with iteration 1 is an extra check next to the independent residual, so it stays. |
| C-05 | More workloads: integer, FFT with independent check, hash/compression, P/E and processor-group coverage report, idle/load cycling | partial | Integer (multiply, divide, branch; pinned checksum) and FFT (checked against a direct DFT, round trip and Parseval, then bit for bit; in-cache and in-memory sizes) are done; the integer test ran on the owner's Ryzen. Idle/load cycling exists (single-core variable load, GPU pulse). Hash/compression and a per-group coverage report are open. |

## Memory (plan §6)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| M-01 | More pattern families | partial | 23 patterns (walking 1/0, 0xA5/0x5A, all zeros/ones, address, inverse address, random) plus moving inversions, block move and stride every fourth pass, each tested against a disturbed word. Bit fade (minutes of idle hold) is open. |
| M-02 | Honest scope: virtual offsets, not physical addresses or DIMMs; Windows-only share of RAM | done | Stated in the result and the log. |
| M-03 | Rowhammer as a simple loop | declined | A loop in Windows cannot hammer chosen physical rows; it would be a fake test. An offline tool (MemTest86) is the right path; importing its result is open. |
| M-04 | STREAM-style benchmark (Copy/Scale/Add/Triad, byte-count convention), latency by pointer chasing | partial | STREAM Copy/Scale/Add/Triad on every thread with STREAM's byte counting and result check, best round with the median Triad beside it; benchmark version raised to 2. Latency by pointer chasing is open. |

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
| S-04 | I/O matrix profiles, OS-like mixed profile, storage event log (129/153/157) with payload | partial | After every test the System log's disk/storport/stornvme/storahci/RST/NTFS events in the test's window are added to its evidence, grouped by provider, id and the device the text names (evidence, never the verdict). The I/O matrix and the OS-like mixed profile are open. |
| S-05 | Data safety: only a new file, CreateNew, delete on close, free-space margin | done (already) | `StorageFile`; nothing raw, no format/erase. |
| N-01 | Blocked ICMP is not a NIC fault; jitter from one sample is not 0 | done | No reply → Inconclusive with a hint; jitter needs two replies (test and internet benchmark). |
| N-02 | LAN peer, TCP/UDP, loaded latency | open | |

## Profiles, cause analysis, other parts (plan §10–§12)

| ID | Item | Status |
|---|---|---|
| P-01 | Profiles (quick, shop standard, deep, transient, OS storage, comparable benchmark) with target length and minimum coverage apart | done: five test profiles; the comparable benchmark is the benchmarks' own versioned records. A profile sets time only; coverage stays each test's verdict. |
| P-02 | Report: observation / likely causes / next test to tell them apart | done: per part, for failed, inconclusive and errored tests, on the page and in the HTML and text reports. |
| P-03 | Capability matrix for fans, battery, display, audio, USB, PCIe, PSU (manual or evidence-only where no instrument exists) | partial: the matrix below; PCIe links are now read for GPUs, NVMe and network cards. The interactive tests are open. |

### Capability matrix (what the app can and cannot establish on its own)

| Part | Automatic, measured | Needs a person or an instrument |
|---|---|---|
| Fans and cooling | RPM and temperatures from the sensors, before and under load | Noise, airflow, a fan that spins but moves no air |
| Battery | Not implemented yet (Windows' reported capacity and wear would be the source) | A cell test needs a discharge instrument |
| Display | - | Dead pixels, backlight bleed: patterns plus a person looking |
| Audio, microphone, USB, keyboard | - | Interactive or loopback tests; not implemented |
| PCIe links | Generation and lanes now and at most (GPU, NVMe, network), from Windows | Signal quality |
| Power supply | Only indirect: faults under the combined CPU+GPU load, rail voltages the board reports | Ripple, hold-up time, true output: an oscilloscope and a load tester |

## Asked for alongside the plan: the live test monitor

Done. Start on the Tests page switches to Monitoring once the queue runs. A panel on top shows the test (n of m) in its part's colour, its
progress, elapsed time and outcome, Cancel, a way back, and a log of what runs: each step in the page's language with the formula it checks or
the command it runs (sfc/DISM print their own lines there). "Follow the tested part" (on by default) opens that part's sensor panels, folds the
others, scrolls to it and charts its key readings, and moves on as the queue does. The engine keeps the last 500 lines, so a page opened
mid-run shows what came before.

## Second round (asked by the owner on 2026-09-30)

- Monitoring and the part pages are one family (monitoring, system, CPU, GPU, RAM, storage, network); RAM has its own page; the part
  pages no longer repeat the benchmarks; a sensor table's column header stays under the tabs while scrolling (measured at 47 px, the tab strip).
- Starting tests opens the tested part's page (Monitoring for tests without one) with the live panel on top; while following, the page moves
  with the queue; the finished run's panel stays until closed or the next session.
- Full specifications as cards, the less used rows folded (`PartSpecs`). Every reader was checked on the owner's machine against HWiNFO:
  caches and microcode, PNY board maker, PCIe 4.0 x16 and NVMe 3.0 x4, VBIOS, bus width, cores, ReBAR off (BAR1 256 MB), UEFI, Secure Boot
  off, TPM 2.0, and per module the SPD: XMP 1 4000 18-22-22-42 1.40 V, JEDEC 2666 19-19-19-43 down to 1600, chips by SK Hynix, XMP not in
  use at 2133 MT/s. SPD is read read-only over the SMBus LibreHardwareMonitor opened; DDR5 profiles are not decoded (no module to check them
  on). The XMP fine-correction bytes are zero on this module, so their offsets are not confirmed by it.

## Verification of this round

- `dotnet build -c Release`: 0 warnings. Non-hardware tests: all pass, with new tests for every fix above (fault injection for C-01, C-02,
  G-01, the outcomes, the checkpoint, the gate, the option snapshot, the GPU sensor mapping, the histogram).
- Browser preview (demo host): the live panel, the log with formulas, following the storage part, the Tests page's live-monitor button.
- Second round: the published app was driven over its DevTools port on the owner's machine: the RAM, CPU, GPU, storage, network and system
  cards read, the sticky header measured, and a 5-second integer test run from the Tests page (it opened the CPU page with the live panel,
  logged every step, the WHEA and storage-event checks, and passed at about 57-60 Gop/s on 32 threads).
- **Not verified on the machine:** the FFT, RAM algorithm, STREAM and profile runs at full length (unit and short tests only); the GPU chain change and the VRAM budget with real cards (hardware tests were not run: they load the GPU);
  a full queue in the published app with the live monitor (it needs the elevated app and several minutes of load, left to the owner).
  `ZzRenderSnapshots.cs` and `ZzRenderSlice10.cs` (untracked WPF-era helpers that no longer compile) were renamed to `.cs.stale`, not deleted.
