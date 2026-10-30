# Scene RT power investigation — 2026-10-30

## Fix applied 2026-10-31 (read this first; the audit below is the state before it)

Measured only on the owner's AMD Radeon RX 9060 XT, in the published app, 1920×1080. **Ray-traced watts rose a lot but are still below raster: the goal "at or above raster" is not met.**

| Build | Run | Mode | Avg W | Peak W | Avg FPS | GPU load | Avg core MHz |
|---|---|---|--:|--:|--:|--:|--:|
| before | 30 s | raster | 157.3 | 188 | 72.1 | 88.8 % | 2986 |
| before | 30 s | ray-traced | 116.5 | 144 | 27.1 | 89.9 % | 3077 |
| after | 162 s walk, twice | raster | 175.6 / 175.7 | 187 / 200 | 82.9 / 82.7 | 97.8 / 97.9 % | 3203 / 3211 |
| after | 162 s walk, twice | ray-traced | 164.1 / 165.1 | 188 / 179 | 54.5 / 54.2 | 97.4 / 97.8 % | 3221 / 3233 |

The before and after rows differ in run length, and the longer run alone raises both modes, so compare within a row pair: the gap went from 40.8 W (26 %) to about 11 W (6 %). `--selftest=scene` read 181 W / 91.6 FPS and `--selftest=scenert` 174 W / 61.0 FPS, both clean; these are not averages over the same stretch.

What the runs showed:
- Rays sent from the colour pass's pixel shaders held the whole pass up: 20.4 ms of colour pass with rays there, 13.1 ms once the shadow fallbacks read maps, against about 5 ms in raster. In compute, half-size rays took 1.4 ms and mirror rays 0.15 to 0.3 ms.
- Across builds the watts follow the frame rate, roughly 92 W + 0.9 J a frame on the 30 s runs. That is a fit to this card's data, not a measured floor. Why a ray stalls a pixel shader (export order, occupancy, memory) was not captured: no RGP run was made.
- Tried and dropped, because power fell: 8× MSAA (152.6 W, 46 FPS), a 22×22 denoiser (152.7 W, 46.5 FPS).
- With every colour-pass ray off (glass too), one 60 s run read 161.7 W against 158 W with them. That is the most left in this direction on this frame.

What changed: `RaysCS` (key light, lamps) and `MirrorCS` (pool, polished solids) trace at half size before the colour pass; `ResolveCS` brings the key light up to full size; `GBufferPS` lays the solids' normals and roughness in the prepass. Edge samples, water, glass and the surroundings pictures read the shadow maps, which now stay under rays, and so do the courtyard's air beams. Ray-traced mode uses level 4's full-screen settings at level 3's samples. Glass, which is not in the depth, still sends rays from the pixel shader, and only at grazing angles for panes. Benchmark version 46.

Not verified: any other card or vendor. Pictures were checked by eye on five full-screen captures per mode, not at matching moments of the walk, so this is not an exact comparison. Half-size shadow and mirror quality in motion was not judged. Reflections on glass panes facing the eye are now the surroundings picture, not a ray.

Untried next step: moving glass rays to compute, and async compute to overlap rays with the shadow passes.

## Result

Reproduced lower board power and lower frame rate in the actual published application on **AMD Radeon RX 9060 XT**. This is not evidence that ray tracing is disabled. GPU timestamps place the dominant cost in the GPU's main colour and ray-shadow passes, not CPU command generation or acceleration-structure construction. The current RT path is a poor maximum-power/thermal workload compared with raster rendering; power alone cannot grade RT correctness or performance.

No rendering source, shader, published EXE, power limit, clock or fan settings were changed in this investigation. The working tree already contained research hooks in GardenRaster/GpuSceneExecutor/GardenRaster.hlsl and compiled shaders; these were preserved. MZ_X was cleared in the audit child processes, so their experimental ray-disable flags were not requested.

## Actual published-app reproduction

Ran `Mazesta-Admin.exe --selftest=tests27:gpu.scene.d3d@opt=raytracing:off` and the corresponding `on` case sequentially. Both completed **Passed, zero frame-check errors**, at 1920×1080, level 3, MSAA 4×, 9,393,900 scene triangles / 21,530 objects.

| Sensor/result | Raster | RT |
|---|---:|---:|
| Average board power | 150.2 W | 104.4 W |
| Peak board power | 186 W | 149 W |
| Average GPU load | 85.8% | 84.6% |
| Average FPS | 81.6 | 29.5 |
| App's average CPU use | 0.8 cores | 0.9 cores |

These short whole-run sensor averages include setup/check-frame overhead and are not a controlled fixed-camera power benchmark. The GPU-test project was built during part of the RT run; use the longer existing 75-second app reports as corroboration: raster **169.8 W / 103.1 FPS / 93.4% GPU**, RT **123.5 W / 35.8 FPS / 92.8% GPU**. Both comparisons show the reported trend; exact watts depend on the camera, warm-up and other host activity.

Raw fresh reports: `artifacts/rt-power-audit/off.txt`, `on.txt`. Per-pass snapshots from those app runs: `passes-off.txt`, `passes-on.txt`. These snapshots are individual frames, not averaged pass timings.

## Averaged GPU profiling

Built the existing GPU-test project successfully (zero warnings/errors), then ran its existing `GardenProfileHardwareTests.Passes` cases with `MAZESTA_PROFILE=1`, `MAZESTA_PROFILE_T0=0`, no experimental ray-disable flags, in an elevated process. Both cases passed. This is the real scene renderer at 1920×1080 / level 3, timed with GPU timestamps for six seconds after warm-up, one frame in flight and without presentation. It isolates renderer costs; it is separate from the published app's two-frame presentation path.

| Average time | Raster | RT |
|---|---:|---:|
| GPU total | 12.78 ms | 37.74 ms |
| Whole frame | 13.67 ms | 38.99 ms |
| Main colour pass | 4.56 ms | 25.20 ms |
| AO + ray sun shadow | 0.44 ms | 6.01 ms |
| Surroundings face | 0.53 ms | 2.48 ms |
| Separate pool reflection | 2.29 ms | no geometry pass |
| RT structure build | — | 0.29 ms |

The GPU accounts for about 97% of the profiled RT frame time. Main colour plus AO/ray shadows account for about 83% of its GPU time. Prioritizing TLAS build or increasing CPU use would target a small part of this result. Raw output: `artifacts/rt-power-audit/profile.txt`.

## Relevant implementation

- `Scene/SceneView.cs`, `Present`: `Present(0, AllowTearing)` where supported; no explicit FPS sleep/cap. Frames-in-flight selection is available and defaults to two. The 25 ms sleep in the benchmark window-pump thread does not cap the separate render thread.
- `Scene/GardenRaster.cs`, constructor: RT sets `ReflectionDivisor = 0`. RT replaces the separate raster pool reflection and sun-shadow-map work; it is not raster work plus an unchanged extra RT layer.
- `Shaders/GardenRaster.hlsl`, `Lit`, `LampShadow`, `LampRay`, `Mirrored`, `Reflected`: inline ray queries are mixed into material shading. Polished surfaces trace reflected hits and shade those hits, including shadow visibility. Strong nearby lamps cast individual shadow rays. These functions lie in the expensive colour pass.
- `Shaders/GardenTrace.hlsli`, `Occluded`, `Trace`, `AlphaPass`: rays traverse the scene and evaluate alpha-tested leaf candidates using geometry/texture reads. This is a plausible source of traversal/latency/divergence costs, but GPU timestamps do **not** identify the exact cache, VGPR or traversal bottleneck.
- `Scene/GpuSceneExecutor.cs:112` and `Benchmarks/GpuSceneBenchmark.cs:57`: RT quality is forced to level 3 and the raster quality control is hidden when RT is on. Selecting a heavier raster level does not make the RT run heavier.

Busy percentage is not the percentage of every shader lane doing arithmetic. Traversal, dependent memory reads and register/occupancy pressure can make the GPU the limiting component while drawing less power than a throughput-heavy raster pass. This is consistent with the observed data, not a measured identification of the exact hardware stall. Establish that with an RGP capture (occupancy, VGPR allocation, traversal and memory counters): https://gpuopen.com/learn/occupancy-explained/ .

## Recommended next implementation work

1. Optimize the measured colour/shadow hot paths: separate reflection evaluation from the large material shader, investigate reduced-resolution reflections with temporal reconstruction, and budget/cache lamp shadow visibility. Check the mirror, pool, glass and foliage quality before accepting changes.
2. Profile RT-specific geometry/alpha-test costs and use suitable ray/shadow LODs where visually valid; keep alpha-cutout correctness.
3. Provide explicit RT quality/ray budgets instead of forcing level 3. Keep comparable benchmark settings/versioning; more rays do not guarantee higher board watts or better throughput.
4. Use the existing dedicated GPU thermal/stress test for maximum sustained power. Keep scene RT as an RT performance/correctness workload; do not inflate watts using unrelated hidden work.

No performance fix or higher-wattage result is claimed by this audit. The identified optimization target is the GPU material/visibility shader path; the precise occupancy/memory mechanism remains to be captured.
