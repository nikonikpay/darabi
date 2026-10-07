# The 3-D scene benchmark's points

The Persian-garden benchmark (`bench.gpu.scene.d3d`) reports points, not only a frame rate, so two systems can be set side by side and the part that holds one back is seen. The method is the one other benchmarks use: a measured rate times a fixed constant, the parts joined by a weighted harmonic mean (3DMark: `S = 164 × 2 / (1/F_gt1 + 1/F_gt2)` for graphics, and `(0.85 + 0.15) / (0.85/S_graphics + 0.15/S_cpu)` for the whole; Cinebench and Geekbench likewise report points against a fixed reference). The constants are Mazesta's own, so the numbers compare with each other (same benchmark version) and with nothing else. They live in `SceneScore` (`src/Mazesta.Diagnostics/Benchmarks/SceneScore.cs`); changing one means raising the benchmark's `Version` in `BenchmarkRecords.Headlines`.

| Score | Formula | Measured from |
|---|---|---|
| Graphics (the list's headline) | `fps_gpu × 100` (not scaled by the pixel count: v29 did, and a 4K run then scored higher than a 720p one at the same frame rate; the resolution is part of the record's key instead) | the card's time per frame: the submission's start to its end (`D3D12Session.LastSubmitSeconds`), over the whole walk |
| CPU | `fps_cpu × 3` | the processor's time to record a frame (`LastRecordSeconds`): what a game loop pays per frame |
| RAM | `5000 × √((Triad GB/s ÷ 40) × (80 ns ÷ latency))` | a 4-second probe after the walk: STREAM Triad on every thread over 3 × 256 MiB, and a random pointer chase |
| Overall | weighted harmonic mean, 0.75 / 0.15 / 0.10 | the three above; absent when the RAM probe could not run (not enough free RAM) |

On the owner's RTX 3090 + Ryzen 9 3950X (a whole walk at 1280×720, measured 2026-10-07): graphics 4301, CPU 4463, RAM 2934, overall 4684 (the card 10.3 ms a frame against the processor's 0.67 ms: the card is the limit, so the CPU score hardly changes the overall one). Every submission of a frame is waited for, so a frame's time is the processor's share plus the card's; the longer one is the run's limit (`Held back by`: GPU, CPU or balanced within 10 %). A run that is not a whole number of walks has no scores (and is not ranked), as before.

Beside the scores, a run keeps what the monitor measured over it, in three groups (graphics card, processor, RAM), each folded until opened: peak and average clocks, memory clock, load, power with its peak (the spike), video memory used; the processor's clock apart for performance and efficiency cores and for each CCD (the cores that share a level-3 cache), its load, power and temperature; the RAM in use, its speed and the CAS latency of its SPD profile at that speed (Windows does not report the timings in use, so no other CL is shown). Two results side by side show, on the quicker side of each result figure, how many percent quicker it is.

## What each run records of its own load

Every benchmark's result and every test's evidence also carries what this program itself took of the machine while it ran (`RunFootprint`): the processor time of its own process as cores busy (average and peak) and as a share of the whole processor, and the RAM it held (working set, average and peak) - not the whole system's RAM in use, which says nothing of the test when other programs are open. The browser engine that draws the window is a process of its own and is not counted. A run under a second records none.

## Mesh smoothing (the 3-D scene's heavy option)

The scene has a `smoothing` option (off by default, so existing records keep their key): every triangle of the garden's meshes is made four (or sixteen) times when the scene is loaded, with the new vertices moved onto the curve their ends' normals describe (Phong tessellation, `MeshSmoother`), so the round things lose their facets and the card gets many times the geometry and the memory. It is the mesh refined in memory, not the GPU's hardware tessellation stage (that would need hull and domain shaders in every pass of the renderer, and in the ray tracer none at all). The option is part of a record's key.
