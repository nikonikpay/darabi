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

## Mesh smoothing (removed in v33)

The scene had a `smoothing` option that made every triangle of the garden's meshes four (or sixteen) times as many (`MeshSmoother`, Phong tessellation in memory). It is gone: it broke the plasterwork and the walls by the windows (their bevels and flat faces were bent toward the normals they share), and the preview's own hardware tessellation touches only what was marked for it. Older records that carry the key keep it; the benchmark's version moved, so lists start again.

## Weather, the processor and memory work, and NIS (v32)

The scene has weather (rain, gusts carrying leaves and twigs, simulated on every core against a grid of the garden solids) - option `weather`: off, on, high - and ends with NVIDIA Image Scaling - option `upscaling`: off, sharpen (default), quality, balanced, performance. Both are part of a record key (the defaults are left out of it). The processor time per frame now includes the weather step, so the CPU score has work to measure; `Bench_Scene_SimFrame` is that step alone. See `docs/SCENE-PERFORMANCE.md` for what a frame costs and why.

## The air, the collisions and the CPU score (v33)

The processor's share of a frame was a tenth of a millisecond of recording: the score had nothing to measure. Now the frame carries the work a game's CPU does, each part of it as a game does it:

* **The air** (`GardenWind`): the wind is a grid of velocities, a *stable-fluids* solver (Stam's method, as games simulate wind and smoke): the gusts and the breeze force it, it carries itself (semi-Lagrangian advection), the crowns of trees slow it, and a pressure projection (red-black Gauss-Seidel sweeps, warm-started) makes it go round the hall, its columns, the walls and the ground, which are the cells of `GardenVoxels` marked hard. The projection takes out only what the solids do: the weather's own wind is not divergence-free (a gust gathers air) and is left as it is. The grid is 0.67 million cells (29 MB) at the standard level and 2.2 million (98 MB) at the high one, in 16-byte cells read at neighbours' distances: the sweeps are limited by the memory's bandwidth and by the cores. They run on a crew of worker threads that spin at a barrier between passes (`GardenCrew`, as a game's job system does it: a thread pool's parallel loop costs hundreds of microseconds to start, a pass of a sweep is tens). About 4 ms a frame on a 32-thread Ryzen 9 at the standard level, 25 ms at the high one.
* **The bodies** (`GardenWeather`): rain, leaves and twigs are blown by that air, no longer by a formula. A leaf is its tip, stalk and two edges, a twig its two ends, each tested against the voxels along its step; the corner that strikes first turns the body. Leaves and twigs also collide with one another (a spatial hash, a counting sort into 30 cm cells, the 27 cells round each body), so a drift against a wall heaps up.
* Trees and bushes were given springs and a bending vertex shader in a first version of this step and it was taken out again: the plants stretched instead of moving. They lean as before (the formula in `Garden.hlsli`).
* A frame on its own (the check frames) never touches the air: its wind is the formula of the fronts, and the bodies are rebuilt from the start of their cycles as before, so the same time is still the same picture and the card's check (the same bits before and after the run) still holds.

The CPU score is 30 x the frames a second the processor and driver can prepare (it was 3 x): on the owner's machine the frame's processor time is 8.8 ms (6.8 of it the simulation) at the standard level, which is a score in the thousands beside the card's, and a slower or smaller processor now scores lower. The version is 33.
