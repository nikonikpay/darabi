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
| H-03 | Three independent axes (run state / diagnosis / coverage) | partial | The outcomes cover what the code can produce. Coverage with a real denominator now exists where one can be counted: the all-thread CPU tests say on how many of the machine's logical processors the load actually ran (P- and E-cores and processor groups apart), the RAM tests how many MiB of the free memory, the VRAM test how many MiB in how many buffers. A single coverage field across every test is still open. |
| H-04 | Checkpoint not marked complete after a fault | done | Completed only when the queue reached its end; a fault leaves it at the item it was on (test). |
| H-05 | Per-step progress in the checkpoint; crash vs timeout vs device removal | done | Checkpoint v2 keeps the running test's round and progress and every finished result. A broken-off session now also says why it stopped, from the System and Application logs after the last checkpoint: a blue screen with its stop code (WER 1001), a power loss or forced reset (Kernel-Power 41, EventLog 6008), a normal restart, a crash of the app (Application Error 1000 / .NET Runtime 1026 naming it) or the app closed; display-driver resets (TDR, Display 4101) are counted beside. The queries were checked against this machine's logs. |
| H-06 | Benchmark result filed under the options it started with (§3A) | done | `RecordedBenchmark.Options` is a copy taken at start; record key, measured part and comparison log use it; the overclock mark is the one at start; a running row's options cannot change. |
| H-07 | One load at a time: tests, benchmarks, GPU tuning (§3B) | done | `WorkloadGate`; each refuses to start while another holds it and says which. The combined CPU+GPU power test is one test, so unaffected. |
| H-08 | GPU sensors of the tested card only; stale readings rejected (§3D) | done | `GpuDevices.SensorNode` (by name; the only GPU; ambiguous → nothing). `SensorEvidence.Latest` ignores readings older than 10 s. Used by the VRAM budget and the GPU tests' evidence. |
| H-09 | VRAM budget from the OS residency budget (DXGI QueryVideoMemoryInfo) | done | Capped at 90 % of DXGI's local budget for the tested adapter (found by LUID); checked read-only on the RTX 3090. |
| H-10 | Record versioning (§3E) | done (2026-10-01) | `HeadlineMetric.Version` was in the peer table key but not in the local record key, so a v3 RAM or v2 SSD run was compared with an earlier workload's record (second review, F04). `BenchmarkRecords.RecordKey` now adds `|v=N` from version 2 on; an earlier record stays in the file under its old key and is not compared. |

## CPU (plan §5)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| C-01 | Matrix test checked 4 of 4096 cells | done | Four fixed seeded input sets; every product's full bit checksum must equal a precomputed constant (pinned by a test). One flipped bit in one cell is caught (test). GFLOPS reported (2n³). |
| C-02 | Vector test used its own first block as reference; only a lane sum was compared | done | Each lane compared with a scalar reference (`Math.FusedMultiplyAdd`, or mul+add for SSE2) computed before the run; wrong and non-finite lanes counted per lane (tests for a one-bit lane fault and NaN/∞). |
| C-03 | Core cycling: coverage, affinity failure, first core as reference | done | Precomputed reference; pin confirmed with `GetCurrentProcessorNumberEx`; any core not tested → Inconclusive, naming the cores (test). |
| C-04 | Linpack formula and threshold | declined (no change) | Already HPL's scaled residual `‖Ax−b‖∞ / (ε·n·(‖A‖∞‖x‖∞+‖b‖∞)) < 16` and `(2/3)n³+2n²`; the bit-for-bit comparison with iteration 1 is an extra check next to the independent residual, so it stays. |
| C-05 | More workloads: integer, FFT with independent check, hash/compression, P/E and processor-group coverage report, idle/load cycling | done | Integer, FFT, and now hashing and compression (SHA-256 and a Deflate round trip on every thread; the expected hash was worked out independently in Python). The all-thread tests report the logical processors that actually ran the load, P/E and groups apart ("ran on 32 of 32" on the owner's Ryzen). Idle/load cycling: single-core variable load, GPU pulse. |

## Memory (plan §6)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| M-01 | More pattern families | done | 23 patterns plus moving inversions, block move and stride every fourth pass; and a bit fade test (all ones, then all zeros, each held untouched from when it is written for half of what is left of the run (under 60 s a hold ends Inconclusive), the RAM locked with VirtualLock so Windows cannot page it out; Inconclusive when it will not lock). Run on the owner's machine: 4 GiB, all 64 blocks locked, no fade. |
| M-02 | Honest scope: virtual offsets, not physical addresses or DIMMs; Windows-only share of RAM | done | Stated in the result and the log. |
| M-03 | Rowhammer as a simple loop | declined | A loop in Windows cannot hammer chosen physical rows; it would be a fake test. An offline tool (MemTest86) is the right path; importing its result is open. |
| M-04 | STREAM-style benchmark (Copy/Scale/Add/Triad, byte-count convention), latency by pointer chasing | done | STREAM kernels, and latency by a random single-cycle pointer chase over 256 MiB (Sattolo; includes 4 KiB page-table misses, as other tools' random latency does). Workload version 3. Owner's machine (DDR4-2133): Triad 19.6 GB/s, latency 118 ns. |

## GPU (plan §7)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| G-01 | An error in an intermediate dispatch was overwritten | done | Dispatches in a batch chain on the previous output; sampled threads are recomputed on the CPU through the whole chain (parallel). The result says how many thread results were verified, as a sample (test for a fault in any dispatch). |
| G-02 | VRAM: every allocated cell checked | done | Every cell is compared on the GPU; eight patterns now rotate: address and inverse, 0xAA/0x55, walking 1 and walking 0 (the bit moves per cell and per round), an address pattern written in stride order (every cell once, far from its neighbours) and a scramble. RTX 3090 from the app: 19712 MiB, 84 passes, no errors. |
| G-03 | Garden benchmark: fixed path, capture outside FPS, 1 % low definition | done (reviewed) | Already a fixed walk (frame n shows the walk at n/30 s) with the check frames drawn before and after the timed run; the 1 % low is the average of the slowest hundredth of frames, and the 99th-percentile frame time is now reported beside it (the other common definition). |
| G-04 | Real AI inference (image/detection/text models with accuracy) (§8) | done (language models) | The AI models page: nine GGUF language models (0.8B to 35B-A3B, Apache-2.0) downloaded only on request, pinned by SHA-256; a fit estimate before download (llmfit-style, from each file's own header figures and this machine's VRAM and free RAM); llama.cpp b11265 (Vulkan, downloaded the same way) measures prompt and generation tokens/s with llama-bench on the GPU or the CPU. Checked on the owner's RTX 3090: Qwen3 4B 193 tok/s generation (ceiling 376), 9.6 tok/s on the Ryzen 3950X; Qwen3.5 0.8B 367 tok/s. Image and detection models with accuracy are not included. The DirectML benchmark stays "matrix multiply throughput". |

## Storage and network (plan §9, §10)

| ID | Item | Status | What changed / why not |
|---|---|---|---|
| S-01 | Latency: whole run, read/write apart, P99.9 only with enough samples, not 1/IOPS | done | `LatencyHistogram` (5 % log buckets, exact mean/max); tests. |
| S-02 | Short reads, wrong bytes counted apart | done | Short read = one error of its own kind, never a full block. |
| S-03 | NVMe health log (critical warning, spare, media errors, error log), before/after snapshots | done | Log page 02h through IOCTL_STORAGE_QUERY_PROPERTY (Windows' inbox NVMe pass-through): on the drive card, in the final SMART check (a warning bit or media errors fail the drive), and compared before and after every storage test (new media errors or a newly raised warning fail that test; new error-log entries are evidence). Owner's MSI M390: no warning, 0 media errors, spare 100 %, 7 % used, 26.94 TB written. |
| S-04 | I/O matrix profiles, OS-like mixed profile, storage event log (129/153/157) with payload | done | The storage benchmark's matrix (SEQ1M Q8, RND4K Q32/Q1 read, RND4K Q32 write) now ends with a mix like a busy Windows drive (70/30 read/write, 4/16/64 KiB, Q4); workload version 2. Storage events after every test. Owner's NVMe: mixed 459 MB/s, 28035 IOPS. |
| S-05 | Data safety: only a new file, CreateNew, delete on close, free-space margin | done (already) | `StorageFile`; nothing raw, no format/erase. |
| N-01 | Blocked ICMP is not a NIC fault; jitter from one sample is not 0 | done | No reply → Inconclusive with a hint; jitter needs two replies (test and internet benchmark). |
| N-02 | LAN peer, TCP/UDP, loaded latency | done (TCP) | The app can be the LAN partner (network page, TCP 47315, off at every start); the LAN test measures speed both ways, the round trip at rest and during the download, and checks every byte end to end. UDP is not included. Checked over loopback only (no second computer here). |

## Profiles, cause analysis, other parts (plan §10–§12)

| ID | Item | Status |
|---|---|---|
| P-01 | Profiles (quick, shop standard, deep, transient, OS storage, comparable benchmark) with target length and minimum coverage apart | done: five test profiles; the comparable benchmark is the benchmarks' own versioned records. A profile sets time only; coverage stays each test's verdict. |
| P-02 | Report: observation / likely causes / next test to tell them apart | done: per part, for failed, inconclusive and errored tests, on the page and in the HTML and text reports. |
| P-03 | Capability matrix for fans, battery, display, audio, USB, PCIe, PSU (manual or evidence-only where no instrument exists) | done: the matrix below, and a hands-on checks page for the parts a person must judge (display, keyboard, speakers, microphone, mouse). The technician's call is not yet written into the report. |

### Capability matrix (what the app can and cannot establish on its own)

| Part | Automatic, measured | Needs a person or an instrument |
|---|---|---|
| Fans and cooling | RPM and temperatures from the sensors, before and under load | Noise, airflow, a fan that spins but moves no air |
| Battery | Not implemented yet (Windows' reported capacity and wear would be the source) | A cell test needs a discharge instrument |
| Display | - | Hands-on checks: full-screen colours, gradient and checkerboard; a person looks |
| Audio, microphone, keyboard, mouse | What the computer received: keys pressed, microphone level, clicks and wheel | Hands-on checks: a person hears the left/right tones and the sweep and judges |
| USB ports | - | Not implemented (a device plugged into each port, by hand) |
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

## Third round (asked by the owner on 2026-09-30)

- AI models page (G-04): see its row. The owner asked for "magnitude" to check which model suits the machine; no tool of that name was found,
  and the description matches llmfit (and CanIRunLLM), so the fit estimate follows the same idea from each file's own header figures.
- Every other open item of the plan above was done or narrowed, each checked on the owner's machine from the published app. DDR5 SPD profiles
  stay open: there is no DDR5 module here to check a decoder against, and a decoder checked against nothing would be guessed data.

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
