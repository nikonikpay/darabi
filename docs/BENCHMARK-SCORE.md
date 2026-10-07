# The 3-D scene benchmark's points

The Persian-garden benchmark (`bench.gpu.scene.d3d`) reports points, not only a frame rate, so two systems can be set side by side and the part that holds one back is seen. The method is the one other benchmarks use: a measured rate times a fixed constant, the parts joined by a weighted harmonic mean (3DMark: `S = 164 × 2 / (1/F_gt1 + 1/F_gt2)` for graphics, and `(0.85 + 0.15) / (0.85/S_graphics + 0.15/S_cpu)` for the whole; Cinebench and Geekbench likewise report points against a fixed reference). The constants are Mazesta's own, so the numbers compare with each other (same benchmark version) and with nothing else. They live in `SceneScore` (`src/Mazesta.Diagnostics/Benchmarks/SceneScore.cs`); changing one means raising the benchmark's `Version` in `BenchmarkRecords.Headlines`.

| Score | Formula | Measured from |
|---|---|---|
| Graphics (the list's headline) | `fps_gpu × (w × h / 1920 × 1080) × 100` | the card's time per frame: the submission's start to its end (`D3D12Session.LastSubmitSeconds`), over the whole walk |
| CPU | `fps_cpu × 20` | the processor's time to record a frame (`LastRecordSeconds`): what a game loop pays per frame |
| RAM | `5000 × √((Triad GB/s ÷ 40) × (80 ns ÷ latency))` | a 4-second probe after the walk: STREAM Triad on every thread over 3 × 256 MiB, and a random pointer chase |
| Overall | weighted harmonic mean, 0.75 / 0.15 / 0.10 | the three above; absent when the RAM probe could not run (not enough free RAM) |

Every submission of a frame is waited for, so a frame's time is the processor's share plus the card's; the longer one is the run's limit (`Held back by`: GPU, CPU or balanced within 10 %). A run that is not a whole number of walks has no scores (and is not ranked), as before.

Beside the scores, a run keeps what the monitor measured over it, in three groups (graphics card, processor, RAM), each folded until opened: peak and average clocks, memory clock, load, power with its peak (the spike), video memory used; the processor's clock apart for performance and efficiency cores and for each CCD (the cores that share a level-3 cache), its load, power and temperature; the RAM in use, its speed and the CAS latency of its SPD profile at that speed (Windows does not report the timings in use, so no other CL is shown). Two results side by side show, on the quicker side of each result figure, how many percent quicker it is.
