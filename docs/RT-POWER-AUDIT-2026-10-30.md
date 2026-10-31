# Scene RT power investigation — 2026-10-30

## v49, 2026-10-31 (read this first)

Measured only on the owner's RX 9060 XT. **Ray-traced watts now match raster.** Published app, 162 s walk, 1 s samples while the scene draws:

| Mode | Avg W | Peak W | App's avg W | FPS | Worst half second | Avg core MHz |
|---|--:|--:|--:|--:|--:|--:|
| raster, 2 runs | 180.0 / 179.5 | 193 / 188 | 174.7 / 173.7 | 97.1 / 97.0 | 61.8 / 61.3 | 3288 / 3283 |
| ray-traced, last 3 of 4 runs | 181.1 / 180.5 / 180.6 | 194 / 200 / 195 | 175.8 / 175.5 / 175.3 | 48.4 | 27.3 / 27.3 / 27.1 | 3132 / 3129 / 3129 |

The first ray-traced walk after publishing read 180.9 W while drawing but 167.1 W / 48.1 FPS by the app's average, worst half second 9.6 FPS, peak 226 W, 343 samples. The others do not show this; the cause was not looked into, the first start after publishing was only a likely reason.

**The fault at the foundation:** the colour pass's shaders were compiled with all the ray code in them. Repeating each pass until it dominated the frame, at night: rays in compute 179.7 W, the colour pass 133.1 W (raster's 180.7 W). The same colour shaders with the ray code left out, sending no ray in either case: 171.5 W. Why (register budget and how many pixels run at once is the likely reason) was not captured: no RGP run.

Steps, steady test, day / night (raster 173.7 / 163.4 W): v47 151.1 / 138.3 W; surroundings' rays in compute and lamp bits read once 162.8 / 145.1 W; no ray code in the colour shaders 171.3 / 157.4 W; the air's light found with rays, and no hall maps 180.9 / 178.5 W.

The test scene (`D:\DFM APp\Cloudytlab`) also showed far dependent reads in an alpha test lower power (4 reads over 64 MB 158 W, 1 read 180 W). The garden's leaf uvs went into a table for that, which changed neither its watts nor its frame time.

Picture: as before v46 but for the light in the air, which is now traced (hall beams slightly softer, the courtyard's air back, as raster has it, and filtered by stained panes) and weak lamps' shadows. Mean difference over ten moments 0.6 to 5.5 of 255. Tea glasses and wall mirror compared by eye at matching moments. Frame rate is about half raster's, 48.4 FPS, against 49.0 at v47 with the same picture and 58.6 at v48 without the air.

Driver crash: three timeouts tonight (02:33, 03:10, 03:11, dumps name `rtlab.exe`, page fault) came from a bug in the test scene's shader, `GetDimensions` on a root buffer view, fixed 03:12. None since, through this build's six walks. A crash the owner saw without a dump or event is not explained by this if it came later.

Not verified: other cards; dispatched rays (compute already reaches the limit); RGP; Microsoft's samples (their build was refused by the permission check); motion quality; steam, whose light now comes from the surface behind it; pixels of over four layers of glass or water use the surroundings picture.

## v47, 2026-10-31 (replaced by v49 above)

Measured only on the owner's RX 9060 XT. **Ray-traced watts are still below raster: the goal is not met.** The picture is back to that of the build before v46, apart from real ray shadows of weak lamps at night and pixels listed below.

Published app, 162 s walk, 1 s samples while the scene draws (two runs each; the app's own averages, which count some loading, read 146.4 W and 174.7 W):

| Mode | Avg W | Peak W | App's FPS | Avg core MHz |
|---|--:|--:|--:|--:|
| raster | 180.1 / 179.9 | 184 / 190 | 97.0 / 97.0 | 3287 / 3286 |
| ray-traced | 150.3 / 151.3 | 178 / 179 | 48.9 / 49.0 | 3160 / 3159 |

A separate test scene (`D:\DFM APp\Cloudy\rtlab`, not in this repo; my own D3D12 program, 1080p, off screen, read through the same sensor) varied one thing at a time, 30 s runs (22 s counted; 25 s for the large-structure ones):

| Case | FPS | Watts | MHz |
|---|--:|--:|--:|
| no rays, shading only | 349 | 181 | 3269 |
| 16 rays a pixel, pixel shader, no shading | 40 | 148 | 3410 |
| 16 rays a pixel, compute, no shading | 90 | 181 | 2932 |
| 4 rays, compute, half size, with shading | 317 | 181 | 3052 |
| 4 rays, async compute queue, with shading | 192 | 182 | 2956 |
| 4 rays, compute, 24,000 instances + alpha test in the ray | 48 | 181 | 3069 |
| the same, pixel shader | 28 | 149 | 3432 |

So the card can draw its 181 W limit on rays alone: the low watts are this project's, not the card's. Rays from a pixel shader draw less and run slower than the same rays in compute. Random memory reads in a pixel shader do the same (64 reads a pixel: 111 W). Async compute changed no watts there, as the limit was already reached. Inline against dispatched rays was not tried.

The garden's steps, ray-traced, 162 s: all compute at full size with pixel-shader rays left for glass 118.8 W; glass and water in compute 133.9 W; no rays left in the colour pass but the pictures of the surroundings 146.4 W; with those off too 150.8 W (a shadow lost, so not kept). The v46 build's 164 W came partly from level 4's lens, which blurred the picture, and from shadow-map passes.

Picture differences from the build before v46, same camera and moment, mean of 255 over ten moments: 0.2 to 1.0, and 2.8 at night. Where glass has more than four layers at a pixel it mirrors the surroundings picture; solid glass there, or at an edge no neighbour listed, leaves the pixel to the samples round it; steam takes its light from the surface behind it. The hall's beams of light in the air still read its shadow map, as they did before v46; the courtyard's map-based air is off under rays again, as it was before v46.

Not verified: other cards or vendors; any D3D12 demo by someone else (Microsoft's samples were downloaded to read, but building them was refused by the permission check, so no third-party watts exist here); why a pixel shader's rays draw less (no RGP capture); pictures in motion. Untried: moving the surroundings pictures' rays to compute (at most about 4 W by the run above); a per-pass power measurement of the garden, which is the next thing to find the remaining 29 W.

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
